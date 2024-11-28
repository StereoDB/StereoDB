namespace StereoDB

open System
open System.Threading
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
    
    let mutable _storageLog: StorageLog option = None
    let mutable _entityAddressStore: EntityAddressStore option = None
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _allTables = schema.AllTables |> Seq.cast<ITableControl> |> Seq.toArray
    let _allTablesDict = _allTables |> Seq.map(fun x -> (x :?> ITable).TableIndex, x) |> readOnlyDict
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }
    
    let updateEntity (tableIndex, logEntry: ReadOnlyMemory<byte>) =
        _allTablesDict[tableIndex].UpdateEntity logEntry
        
    let getEntityAddress (tableIndex, logEntry: ReadOnlyMemory<byte>, logAddress: int64) =
        _allTablesDict[tableIndex].GetEntityAddress(logEntry, logAddress)
                    
    do
        MessagePack.initDefaultOptions()
        
        if settings.LocalPersistenceEnabled then
            _storageLog <- Some (StorageLog.Init(updateEntity))
            _entityAddressStore <- Some (EntityAddressStore(_storageLog.Value.FasterLog, getEntityAddress))
            _entityAddressStore.Value.Init()
            _allTables |> Array.iter(_.InitStorage(_storageLog.Value, _entityAddressStore.Value))            
          
    let commit () =
        match _storageLog with
        | Some store ->
            let tablesChanges =
                try
                    _lockSlim.EnterWriteLock()
                    _allTables |> Array.map(_.GetChangesAndReset())                
                finally
                    _lockSlim.ExitWriteLock()        
            
            for i = 0 to _allTables.Length - 1 do        
                let changes = tablesChanges[i]
                let table = _allTables[i]
                
                table.WriteToLog changes
                table.ReturnChangesToPool changes
            
            valueTask {
                do! store.CommitAsync()
            }
        
        | None -> ValueTask.singleton()            
           
    member internal this.Restore() =
        if _storageLog.IsSome then
            _storageLog.Value.Restore()
            _entityAddressStore.Value.StartEntityAddressUpdate()
            
        _allTables |> Array.iter(_.EnableChangeTracking())
         
    interface IDisposable with
        member this.Dispose() =
            if _storageLog.IsSome then
                use _ = _storageLog.Value
                ()
           
    interface CSharp.IStereoDb<'TSchema> with           
            
        member this.ReadTransaction<'T>(transaction: Func<ReadOnlyTsContext<'TSchema>, 'T>) =
            try
                _lockSlim.EnterReadLock()
                transaction.Invoke(_rCtx)            
            finally
                _lockSlim.ExitReadLock()            
            
        member this.WriteTransaction<'T>(transaction: Func<ReadWriteTsContext<'TSchema>, 'T>) =
            try
                _lockSlim.EnterWriteLock()
                transaction.Invoke(_rwCtx)
            finally
                _lockSlim.ExitWriteLock()           
                        
        member this.WriteTransaction(transaction: Action<ReadWriteTsContext<'TSchema>>) =
            try
                _lockSlim.EnterWriteLock()
                transaction.Invoke(_rwCtx)
            finally
                _lockSlim.ExitWriteLock()

        member this.CommitAsync() = commit() |> ValueTask.toUnit            
                
    interface FSharp.IStereoDb<'TSchema> with        
        member this.ReadTransaction(transaction: ReadOnlyTsContext<'TSchema> -> 'T voption) =
            try
                _lockSlim.EnterReadLock()
                transaction _rCtx            
            finally
                _lockSlim.ExitReadLock()
            
        member this.WriteTransaction<'T>(transaction: ReadWriteTsContext<'TSchema> -> 'T voption) =
            try
                _lockSlim.EnterWriteLock()
                transaction _rwCtx            
            finally
                _lockSlim.ExitWriteLock()
        
        member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> unit) =
            try
                _lockSlim.EnterWriteLock()
                transaction _rwCtx
            finally
                _lockSlim.ExitWriteLock()

namespace StereoDB.CSharp

    open StereoDB
    open StereoDB.Table
    
    type StereoDb =
        static member Create(schema, settings) =
            let db = new StereoDb<'TSchema>(schema, settings)
            db.Restore()
            db :> IStereoDb<_>
        
        static member CreateTable(tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>            
            
namespace StereoDB.FSharp

    open System.Runtime.CompilerServices
    open StereoDB
    open StereoDB.Table
    
    type StereoDbExtensions =
    
        [<Extension>]
        static member inline Set(table: IReadWriteTable<'TId, 'TEntity>, entity: 'TEntity when 'TEntity : (member Id: 'TId)) =
            table.Set(entity.Id, entity)
    
    module StereoDb =    
        
        let create (schema, settings) =
            let db = new StereoDb<'TSchema>(schema, settings)
            db.Restore()
            db :> IStereoDb<_>            
        
        let createTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality> (tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName) :> IConfigurationTable<_, _>