namespace StereoDB.FSharp

open System.Threading
open StereoDB
open StereoDB.Sql

type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit
    abstract ExecuteSql: sql: string -> unit

type StereoDbEngine<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _rCtx = ReadOnlyTsContext(schema)
    let _rwCtx = ReadWriteTsContext(schema)       
            
    member this.ReadTransaction(transaction: ReadOnlyTsContext<'TSchema> -> 'T voption) =
        try
            _lockSlim.EnterReadLock()
            transaction _rCtx            
        finally
            _lockSlim.ExitReadLock()            
        
    member this.WriteTransaction(transaction: ReadWriteTsContext<'TSchema> -> 'T voption) =
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
        
    member this.ExecuteSql(sql: string) =
        let query = SqlParser.parseSql sql
        let func = QueryBuilder.buildQuery<'TSchema> query _rwCtx schema
        printfn "%A" query
        match func with
        | QueryBuilder.Write caller ->
            this.WriteTransaction (fun x -> caller.Invoke x)
        | QueryBuilder.Read _ -> failwith "Not implemented"
                
    static member CreateTable() =
        StereoDbTable<'TId, 'TEntity>()
        :> ITable<'TId, 'TEntity>