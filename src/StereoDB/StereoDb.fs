namespace StereoDB

open System
open System.Threading
open IcedTasks
open StereoDB
open StereoDB.Storage

type StereoDbSettings = {
    DataSizeLargerRAM: bool
}
with
    static member Default = {
        DataSizeLargerRAM = false
    }

type internal StereoDb<'TSchema when 'TSchema :> IDbSchema>(schema: 'TSchema, settings: StereoDbSettings) =
    
    let _storage = StorageManager.Init()
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _allTables = schema.AllTables |> Seq.cast<ITableControl> |> Seq.toArray
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }
    
    do
        if settings.DataSizeLargerRAM then
            _allTables |> Array.iter(fun x -> x.InitStorage _storage)            
          
    let commit () = valueTask {
        try
            _lockSlim.EnterWriteLock()
            _allTables |> Array.iter(_.PrepareForCommit())      
        finally
            _lockSlim.ExitWriteLock()
                  
        _allTables |> Array.Parallel.iter(_.SerializeToLog())
        
        try
            _lockSlim.EnterWriteLock()
            _allTables |> Array.iter(_.Commit())
        finally
            _lockSlim.ExitWriteLock()
            
        do! _storage.Commit()            
    }
           
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
    
    type StereoDb =
        static member Create(schema, settings) =
            StereoDb(schema, settings) :> IStereoDb<_>
        
        static member CreateTable(tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName)
            :> IConfigurationTable<_, _>
            
    // type StereoDbExtensions =
    //
    //     [<Extension>]
    //     static member inline Set(table: IReadWriteTable<'TId, 'TEntity>, entity: 'TEntity when 'TEntity : (member Id: 'TId)) =
    //         table.Set(entity.Id, entity)            
            
namespace StereoDB.FSharp

    open System.Runtime.CompilerServices
    open StereoDB
    
    type StereoDbExtensions =
    
        [<Extension>]
        static member inline Set(table: IReadWriteTable<'TId, 'TEntity>, entity: 'TEntity when 'TEntity : (member Id: 'TId)) =
            table.Set(entity.Id, entity)
    
    module StereoDb =    
        
        let create (schema, settings) =
            StereoDb(schema, settings)  :> IStereoDb<_>
        
        let createTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality> (tableName) =
            StereoDbTable<'TId, 'TEntity>(tableName)
            :> IConfigurationTable<_, _>