namespace StereoDB.CSharp

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading.Tasks
open StereoDB

type IReadOnlyTable<'TId, 'TEntity> =
    inherit ITable<'TId, 'TEntity>
    abstract GetIds: unit -> 'TId seq 
    abstract TryGet: id:'TId * [<Out>]entity:'TEntity byref -> bool
    
type IReadWriteTable<'TId, 'TEntity> =
    inherit IReadOnlyTable<'TId, 'TEntity>    
    abstract Set: id:'TId * entity:'TEntity -> unit
    abstract Delete: id:'TId -> bool

type ReadOnlyTsContextExt =    
    [<Extension>]
    static member inline UseTable(ctx: ReadOnlyTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadOnlyTable<'TId, 'TEntity>
    
type ReadWriteTsContextExt =
    [<Extension>]
    static member inline UseTable(ctx: ReadWriteTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadWriteTable<'TId, 'TEntity>
        
type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:Func<ReadOnlyTsContext<'TSchema>, 'T> -> 'T
    abstract WriteTransaction: transaction:Func<ReadWriteTsContext<'TSchema>, 'T> -> 'T
    abstract WriteTransaction: transaction:Action<ReadWriteTsContext<'TSchema>> -> unit
    abstract CommitAsync: unit -> ValueTask