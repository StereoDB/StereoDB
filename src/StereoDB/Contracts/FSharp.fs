namespace StereoDB.FSharp

open System.Runtime.CompilerServices
open StereoDB

type IReadOnlyTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ITable<'TId, 'TEntity>
    abstract GetIds: unit -> 'TId seq
    abstract Get: id:'TId -> 'TEntity voption    
    
type IReadWriteTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit IReadOnlyTable<'TId, 'TEntity>    
    abstract Set: entity:'TEntity -> unit
    abstract Delete: id:'TId -> bool
    
[<Extension>]
type ReadOnlyTsContextExt() =    
    [<Extension>]
    static member inline UseTable(ctx: ReadOnlyTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadOnlyTable<'TId, 'TEntity>
    
[<Extension>]
type ReadWriteTsContextExt() =
    [<Extension>]
    static member inline UseTable(ctx: ReadWriteTsContext<'TSchema>, table: ITable<'TId, 'TEntity>) =
        table :?> IReadWriteTable<'TId, 'TEntity>
        
type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit
    abstract ExecSql: sql:string -> unit
    abstract ExecSql: sql:string -> ResizeArray<'TResult> voption
    // abstract ExecSql: transaction:(ReadOnlyTsContext<'TSchema> * string) -> ResizeArray<'TResult> voption
    // abstract ExecSql: transaction:(ReadWriteTsContext<'TSchema> * string) -> unit