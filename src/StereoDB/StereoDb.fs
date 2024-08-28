namespace StereoDB

open System
open System.Threading
open StereoDB

type internal StereoDb<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }
           
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
        static member Create(schema: 'TSchema) =
            schema |> StereoDb :> IStereoDb<_>
        
        static member CreateTable() =
            StereoDbTable<'TId, 'TEntity>()
            :> ITable<_, _>
            
namespace StereoDB.FSharp

    open StereoDB
    
    module StereoDb =    
        
        let create (schema: 'TSchema) =
            schema |> StereoDb :> IStereoDb<_>
        
        let createTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId> and 'TId: equality> () =
            StereoDbTable<'TId, 'TEntity>()
            :> ITable<_, _>                     