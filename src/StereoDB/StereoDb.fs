namespace StereoDB

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open StereoDB
open StereoDB.Storage
open StereoDB.Infra.Utils

type StereoDbSettings = {
    LocalPersistenceEnabled: bool
}
with
    static member Default = {
        LocalPersistenceEnabled = false
    }

type internal StereoDb<'TSchema when 'TSchema :> IDbSchema>(schema: 'TSchema, settings: StereoDbSettings) =
    
    let mutable _working = true
    let mutable _currentCheckpointTask = Task.CompletedTask
    let mutable _currentCheckpointCancelToken = new CancellationTokenSource()
    
    let mutable _storageLog: StorageLog option = None
    let mutable _entityAddressStore: EntityAddressStore option = None
    let mutable _writeTsCountFromLatestCheckpoint = 0L
    let mutable _writeTsCountFromLatestCommit = 0L
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _allTables = schema.AllTables |> Seq.cast<ITableControl> |> Seq.toArray
    let _allTablesDict = _allTables |> Seq.map(fun x -> (x :?> ITable).TableIndex, x) |> readOnlyDict
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }
    
    let updateEntity (tableIndex, logEntry) =
        _allTablesDict[tableIndex].UpdateEntity logEntry
        
    let getEntityAddress (tableIndex, logEntry, logAddress) =
        _allTablesDict[tableIndex].GetEntityAddress(logEntry, logAddress)

    let restoreDb (storageLog: StorageLog) (addressStore: EntityAddressStore) =
        valueTask {
            let! latestSavedAddress = addressStore.LoadEntities()
            
            storageLog.RestoreFrom latestSavedAddress
            
            _allTables |> Array.iter(_.EnableChangeTracking())
        }
        
    let commit (storageLog: StorageLog) = valueTask {
        if Interlocked.Read(&_writeTsCountFromLatestCommit) > 0 then
            
            let tablesChanges =                
                try
                    _lockSlim.EnterWriteLock()
                    Interlocked.Exchange(&_writeTsCountFromLatestCommit, 0L) |> ignore
                    _allTables |> Array.map(_.GetChangesAndReset())                    
                finally
                    _lockSlim.ExitWriteLock()        
            
            for i = 0 to _allTables.Length - 1 do        
                let changes = tablesChanges[i]
                let table = _allTables[i]
                
                table.WriteToLog changes
                table.ReturnChangesToPool changes            
            
            do! storageLog.CommitAsync()
    }
    
    let startAutoCommit (storageLog: StorageLog) = valueTask {
        while _working do
            do! commit storageLog            
            do! Task.Delay Constants.AutoCommitDelay
    }
        
    let startAutoCheckpoint (addressStore: EntityAddressStore) = valueTask {        
        while _working do
            try
                if Interlocked.Read(&_writeTsCountFromLatestCheckpoint) > Constants.WriteTsLimitToCheckpoint && _currentCheckpointTask.IsCompleted then
                    Interlocked.Exchange(&_writeTsCountFromLatestCheckpoint, 0L) |> ignore
                    _currentCheckpointCancelToken <- new CancellationTokenSource()
                    _currentCheckpointTask <- addressStore.StartCheckpoint(_currentCheckpointCancelToken.Token)
                
                do! Task.Delay Constants.CheckpointDelay
            with
                ex -> ()
    }
    
    member this.InitDb() = valueTask {
        MessagePack.initDefaultOptions()
        
        if settings.LocalPersistenceEnabled then            
            let storageLog   = StorageLog.Init updateEntity
            let addressStore = EntityAddressStore.Init(storageLog.FasterLog, getEntityAddress, updateEntity)
            
            _storageLog         <- Some storageLog
            _entityAddressStore <- Some addressStore
            
            _allTables |> Array.iter(_.SetStorage(storageLog, addressStore))
            
            do! restoreDb storageLog addressStore
            
            startAutoCommit storageLog |> ignore
            startAutoCheckpoint addressStore |> ignore
    }
         
    interface IAsyncDisposable with
        member this.DisposeAsync() =
            valueTask {
                _working <- false
                
                if _storageLog.IsSome then
                    do! commit _storageLog.Value
                    
                    _currentCheckpointCancelToken.Cancel()
                    _currentCheckpointTask.Wait()            
                    
                    use _ = _storageLog.Value
                    use _ = _entityAddressStore.Value
                    
                    return ()
            }
            |> ValueTask.toUnit
           
    interface CSharp.IStereoDb<'TSchema> with           
            
        member this.ReadTransaction<'T>(transaction: Func<ReadOnlyTsContext<'TSchema>, 'T>) =            
            try
                _lockSlim.EnterReadLock()
                transaction.Invoke(_rCtx)                
            finally
                _lockSlim.ExitReadLock()            
            
        member this.WriteTransaction<'T>(transaction: Func<ReadWriteTsContext<'TSchema>, 'T>) =
            Interlocked.Increment(&_writeTsCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTsCountFromLatestCommit) |> ignore
            
            try
                _lockSlim.EnterWriteLock()                 
                transaction.Invoke(_rwCtx)
            finally
                _lockSlim.ExitWriteLock()                            
                        
        member this.WriteTransaction(transaction: Action<ReadWriteTsContext<'TSchema>>) =
            Interlocked.Increment(&_writeTsCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTsCountFromLatestCommit) |> ignore
            
            try
                _lockSlim.EnterWriteLock()
                transaction.Invoke(_rwCtx)
            finally
                _lockSlim.ExitWriteLock()
                
    interface FSharp.IStereoDb<'TSchema> with        
        member this.ReadTransaction(transaction: ReadOnlyTsContext<'TSchema> -> 'T voption) =
            try
                _lockSlim.EnterReadLock()
                transaction _rCtx            
            finally
                _lockSlim.ExitReadLock()
            
        member this.WriteTransaction<'T>(transaction: ReadWriteTsContext<'TSchema> -> 'T voption) =
            Interlocked.Increment(&_writeTsCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTsCountFromLatestCommit) |> ignore
            
            try
                _lockSlim.EnterWriteLock()
                transaction _rwCtx            
            finally
                _lockSlim.ExitWriteLock()
        
        member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> unit) =
            Interlocked.Increment(&_writeTsCountFromLatestCheckpoint) |> ignore
            Interlocked.Increment(&_writeTsCountFromLatestCommit) |> ignore
            
            try
                _lockSlim.EnterWriteLock()
                transaction _rwCtx
            finally
                _lockSlim.ExitWriteLock()

namespace StereoDB.CSharp

    open IcedTasks
    open StereoDB
    open StereoDB.Table
    
    type StereoDb =
        static member Init(schema, settings) = valueTask {
            let db = new StereoDb<'TSchema>(schema, settings)
            do! db.InitDb()
            return db :> IStereoDb<_>
        }
        
        static member CreateTable(tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>            
            
namespace StereoDB.FSharp

    open IcedTasks
    open System.Runtime.CompilerServices
    open StereoDB
    open StereoDB.Table
    
    type StereoDbExtensions =
    
        [<Extension>]
        static member inline Set(table: IReadWriteTable<'TId, 'TEntity>, entity: 'TEntity when 'TEntity : (member Id: 'TId)) =
            table.Set(entity.Id, entity)
    
    module StereoDb =    
        
        let init (schema, settings) = valueTask {
            let db = new StereoDb<'TSchema>(schema, settings)
            do! db.InitDb()
            return db :> IStereoDb<_>
        }
        
        let createTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality> (tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>