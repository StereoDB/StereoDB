namespace StereoDB.FSharp

open System
open System.Runtime.CompilerServices
open StereoDB

type IReadOnlyTable<'TId, 'TEntity> =
    inherit ITable<'TId, 'TEntity>
    abstract GetIds: unit -> 'TId seq
    abstract Get: id:'TId -> 'TEntity voption    
    
type IReadWriteTable<'TId, 'TEntity> =
    inherit IReadOnlyTable<'TId, 'TEntity>    
    abstract Set: id:'TId * entity:'TEntity -> unit
    abstract Delete: id:'TId -> bool
    
type StereoDbExt =    
    [<Extension>]
    static member inline Set(table: IReadWriteTable<'TId, 'TEntity>, entity: 'TEntity when 'TEntity : (member Id: 'TId)) =
        table.Set(entity.Id, entity)     
    
type ReadOnlyTsContextExt =    
    [<Extension>]
    static member inline UseTable(ctx: ReadOnlyTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadOnlyTable<'TId, 'TEntity>

type ReadWriteTsContextExt =
    [<Extension>]
    static member inline UseTable(ctx: ReadWriteTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadWriteTable<'TId, 'TEntity>
        
type IStereoDb<'TSchema> =
    inherit IAsyncDisposable
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit   