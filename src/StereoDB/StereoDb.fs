namespace StereoDB
#nowarn "3261"

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open Serilog
open StereoDB
open StereoDB.Infra.ConcurrencyControl
open StereoDB.Storage
open StereoDB.Infra.Utils

type internal StereoDb<'TSchema when 'TSchema :> IDbSchema>(
    logger: ILogger, threadLock: IThreadLock, schema: 'TSchema, settings: StereoDbSettings) =
    
    let mutable _working = true
    let mutable _currentCommitTask = Task.CompletedTask
    let mutable _currentCheckpointTask = Task.CompletedTask
    let mutable _currentCommitCancelToken = new CancellationTokenSource()
    let mutable _currentCheckpointCancelToken = new CancellationTokenSource()
    
    let mutable _storageLog = None
    let mutable _entityAddressStore = None
    let mutable _writeTxnCountFromLatestCheckpoint = 0L
    let mutable _writeTxnCountFromLatestCommit = 0L
        
    let _allTables = schema.AllTables |> Seq.cast<ITableControl> |> Seq.toArray
    let _allTablesDict = _allTables |> Seq.map(fun x -> (x :?> ITable).TableId, x) |> readOnlyDict
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }

    let updateEntity tableId logEntry =
        _allTablesDict[tableId].UpdateEntity logEntry
        
    let getEntityAddress tableId logEntry logAddress =
        _allTablesDict[tableId].GetEntityAddress(logEntry, logAddress)

    let restoreDb (storageLog: StorageLog) (addressStore: EntityAddressStore) =
        valueTask {
            let! latestSavedAddress = addressStore.LoadEntities()
            
            storageLog.RestoreFrom latestSavedAddress
            
            let lowestAddress = addressStore.GetLowestAddress()            
            if lowestAddress.IsSome then
                do! storageLog.TruncateUntil lowestAddress.Value                
            
            _allTables |> Array.iter(_.EnableChangeTracking())
        }
        
    let commit (storageLog: StorageLog) = valueTask {
        if Interlocked.Read(&_writeTxnCountFromLatestCommit) > 0 then
            
            let tablesChanges =
                let threadId = Thread.CurrentThread.ManagedThreadId
                try
                    threadLock.EnterWriteLock threadId                    
                    Interlocked.Exchange(&_writeTxnCountFromLatestCommit, 0L) |> ignore
                    _allTables |> Array.map(_.GetChangesAndReset())                    
                finally
                    threadLock.ReleaseWriteLock threadId        
            
            for i = 0 to _allTables.Length - 1 do        
                let changes = tablesChanges[i]
                let table = _allTables[i]
                
                table.WriteToLog changes
                table.ReturnChangesToPool changes            
            
            do! storageLog.CommitAsync()
    }
    
    let startAutoCommit (storageLog: StorageLog) = task {
        while _working do            
            try
                do! Task.Delay(Constants.CommitDelay, _currentCommitCancelToken.Token)
            with
                ex -> ()
            
            do! commit storageLog
    }
        
    let startAutoCheckpoint (addressStore: EntityAddressStore) = task {        
        while _working do
            try
                if Interlocked.Read(&_writeTxnCountFromLatestCheckpoint) > Constants.WriteTsLimitToCheckpoint
                   && _currentCheckpointTask.IsCompleted then
                    
                    Interlocked.Exchange(&_writeTxnCountFromLatestCheckpoint, 0L) |> ignore
                    _currentCheckpointCancelToken <- new CancellationTokenSource()
                    
                    do! addressStore.StartCheckpoint(_currentCheckpointCancelToken.Token)
                
                do! Task.Delay(Constants.CheckpointDelay, _currentCheckpointCancelToken.Token)
            with
                ex -> ()
    }    

    member this.InitDb() = valueTask {        
        _allTables |> Array.iter(fun x -> x.Init logger)

        if settings.LocalPersistenceEnabled then
            let dbPath = StorageOperations.createDbFilePath settings.DbFolderPath
            let storageLog   = StorageLog.Init(dbPath.DbLogFolder, updateEntity)
            let addressStore = EntityAddressStore.Init(dbPath.SqliteDbPath, storageLog.FasterLog, getEntityAddress, updateEntity)
            
            _storageLog         <- Some storageLog
            _entityAddressStore <- Some addressStore
            
            _allTables |> Array.iter(_.SetStorage(storageLog, addressStore, settings.EntitySerializer))
            
            do! restoreDb storageLog addressStore
            
            _currentCommitTask     <- startAutoCommit storageLog
            _currentCheckpointTask <- startAutoCheckpoint addressStore
    }
           
    interface CSharp.IStereoDb<'TSchema> with           
            
        member this.ReadTransaction<'T>(transaction: Func<ReadOnlyTsContext<'TSchema>, 'T>) =
            let threadId = Thread.CurrentThread.ManagedThreadId
            try
                threadLock.EnterReadLock threadId                
                transaction.Invoke(_rCtx)                
            finally
                threadLock.ReleaseReadLock threadId  
            
        member this.WriteTransaction<'T>(transaction: Func<ReadWriteTsContext<'TSchema>, 'T>) =            
            Interlocked.Increment(&_writeTxnCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTxnCountFromLatestCommit) |> ignore
            
            let threadId = Thread.CurrentThread.ManagedThreadId
            
            try
                threadLock.EnterWriteLock threadId         
                transaction.Invoke(_rwCtx)
            finally
                threadLock.ReleaseWriteLock threadId                  
                        
        member this.WriteTransaction(transaction: Action<ReadWriteTsContext<'TSchema>>) =
            Interlocked.Increment(&_writeTxnCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTxnCountFromLatestCommit) |> ignore
            
            let threadId = Thread.CurrentThread.ManagedThreadId
            
            try
                threadLock.EnterWriteLock threadId
                transaction.Invoke(_rwCtx)
            finally
                threadLock.ReleaseWriteLock threadId
                
    interface FSharp.IStereoDb<'TSchema> with        
        member this.ReadTransaction(transaction: ReadOnlyTsContext<'TSchema> -> 'T voption) =
            let threadId = Thread.CurrentThread.ManagedThreadId
            try
                threadLock.EnterReadLock threadId
                transaction _rCtx            
            finally
                threadLock.ReleaseReadLock threadId
            
        member this.WriteTransaction<'T>(transaction: ReadWriteTsContext<'TSchema> -> 'T voption) =
            Interlocked.Increment(&_writeTxnCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTxnCountFromLatestCommit) |> ignore
            
            let threadId = Thread.CurrentThread.ManagedThreadId
            
            try
                threadLock.EnterWriteLock threadId
                transaction _rwCtx            
            finally
                threadLock.ReleaseWriteLock threadId
        
        member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> unit) =
            Interlocked.Increment(&_writeTxnCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTxnCountFromLatestCommit) |> ignore
            
            let threadId = Thread.CurrentThread.ManagedThreadId
            
            try
                threadLock.EnterWriteLock threadId
                transaction _rwCtx            
            finally
                threadLock.ReleaseWriteLock threadId
                
    interface IAsyncDisposable with
        member this.DisposeAsync() =
            valueTask {
                if _working then
                    _working <- false // todo: working set true and via transaction, also validate via transaction
                    
                    if _storageLog.IsSome then
                        _currentCommitCancelToken.Cancel()
                        _currentCheckpointCancelToken.Cancel()
                        
                        do! Task.WhenAll(_currentCommitTask, _currentCheckpointTask)                    
                        do! disposeAsync _entityAddressStore.Value
                        do! disposeAsync _storageLog.Value
            }
            |> ValueTask.toUnit                

namespace StereoDB.CSharp

    open IcedTasks
    open Serilog
    open StereoDB
    open StereoDB.Infra.ConcurrencyControl
    open StereoDB.Storage
    open StereoDB.Table
    
    type StereoDb =
        static member Init(schema, settings) = valueTask {
            let logger = LoggerConfiguration().CreateLogger()            
            let db = new StereoDb<'TSchema>(logger, CasSpinLock(), schema, settings)
            do! db.InitDb()
            return db :> IStereoDb<_>
        }
        
        static member Remove(settings) = 
            StorageOperations.removeDb settings.DbFolderPath
        
        static member CreateTable(tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>            
            
namespace StereoDB.FSharp

    open IcedTasks
    open Serilog
    open StereoDB
    open StereoDB.Infra.ConcurrencyControl
    open StereoDB.Storage
    open StereoDB.Table
    
    module StereoDb =
        
        let init (schema, settings) = valueTask {
            let logger = LoggerConfiguration().CreateLogger()
            let db = new StereoDb<'TSchema>(logger, CasSpinLock(), schema, settings)
            do! db.InitDb()
            return db :> IStereoDb<_>
        }
        
        let remove settings =
            StorageOperations.removeDb settings.DbFolderPath
        
        let createTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality> (tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>