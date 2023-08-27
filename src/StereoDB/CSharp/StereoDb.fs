namespace StereoDB.CSharp

open System
open System.Runtime.CompilerServices
open System.Threading
open StereoDB

type ReadOnlyTsContext<'TSchema>(schema: 'TSchema) =
    member this.Schema = schema
    member inline this.UseTable(table: ITable<'TId, 'TEntity>) =
        table :?> IReadOnlyTable<'TId, 'TEntity>
        
type ReadWriteTsContext<'TSchema>(schema: 'TSchema) =
    member this.Schema = schema
    member inline this.UseTable(table: ITable<'TId, 'TEntity>) =
        table :?> IReadWriteTable<'TId, 'TEntity>

type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:Func<ReadOnlyTsContext<'TSchema>, 'T> -> 'T
    abstract WriteTransaction: transaction:Func<ReadWriteTsContext<'TSchema>, 'T> -> 'T
    abstract WriteTransaction: transaction:Action<ReadWriteTsContext<'TSchema>> -> unit

type StereoDbEngine<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _rCtx = ReadOnlyTsContext(schema)
    let _rwCtx = ReadWriteTsContext(schema)

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]        
    member this.ReadTransaction<'T>(transaction: Func<ReadOnlyTsContext<'TSchema>, 'T>) =
        try
            _lockSlim.EnterReadLock()
            transaction.Invoke(_rCtx)            
        finally
            _lockSlim.ExitReadLock()            
    
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member this.WriteTransaction<'T>(transaction: Func<ReadWriteTsContext<'TSchema>, 'T>) =
        try
            _lockSlim.EnterWriteLock()
            transaction.Invoke(_rwCtx)
        finally
            _lockSlim.ExitWriteLock()
           
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]                
    member this.WriteTransaction(transaction: Action<ReadWriteTsContext<'TSchema>>) =
        try
            _lockSlim.EnterWriteLock()
            transaction.Invoke(_rwCtx)
        finally
            _lockSlim.ExitWriteLock()
                
    static member CreateTable() =
        StereoDbTable<'TId, 'TEntity>()
        :> ITable<'TId, 'TEntity>