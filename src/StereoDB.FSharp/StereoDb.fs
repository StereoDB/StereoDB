namespace StereoDB.FSharp

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
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit

type StereoDbEngine<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _rCtx = ReadOnlyTsContext(schema)
    let _rwCtx = ReadWriteTsContext(schema)
        
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]        
    member this.ReadTransaction(transaction: ReadOnlyTsContext<'TSchema> -> 'T voption) =
        try
            _lockSlim.EnterReadLock()
            transaction _rCtx            
        finally
            _lockSlim.ExitReadLock()            
    
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> 'T voption) =
        try
            _lockSlim.EnterWriteLock()
            transaction _rwCtx            
        finally
            _lockSlim.ExitWriteLock()
           
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]                
    member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> unit) =
        try
            _lockSlim.EnterWriteLock()
            transaction _rwCtx
        finally
            _lockSlim.ExitWriteLock()           
                
    static member CreateTable() =
        StereoDbTable<'TId, 'TEntity>()
        :> ITable<'TId, 'TEntity>