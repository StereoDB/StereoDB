namespace StereoDB.FSharp

open System.Threading
open StereoDB
open StereoDB.Sql

type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit
    abstract ExecuteSql: sql: string -> unit
    abstract ExecuteSql: sql: string -> System.Collections.Generic.List<'TResult> voption

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
        let func = QueryBuilder.buildQuery<'TSchema, unit> query _rwCtx schema
        match func with
        | QueryBuilder.Write caller ->
            this.WriteTransaction (fun x -> caller.Invoke x)
        | QueryBuilder.Read _ -> failwith "Execution of SELECT query from this method is not supported. Please use ExecuteSql<T>(string)"
        
    member this.ExecuteSql<'T>(sql: string): System.Collections.Generic.List<'T> voption =
        let query = SqlParser.parseSql sql
        let func = QueryBuilder.buildQuery<'TSchema, 'T> query _rwCtx schema
        match func with
        | QueryBuilder.Write _ ->
            failwith "Execution of UPDATE and DELETE queries from this method is not supported. Please use ExecuteSql<T>(string)"
        | QueryBuilder.Read caller -> 
            this.ReadTransaction (fun x ->
                let value = caller.Invoke x
                if value = null then ValueNone else ValueSome (value :?> System.Collections.Generic.List<'T>))
                
    static member CreateTable() =
        StereoDbTable<'TId, 'TEntity>()
        :> ITable<'TId, 'TEntity>