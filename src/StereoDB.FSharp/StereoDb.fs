namespace StereoDB.FSharp

open System.Threading
open StereoDB
open StereoDB.Sql

type IStereoDb<'TSchema> =
    abstract ReadTransaction: transaction:(ReadOnlyTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> 'T voption) -> 'T voption
    abstract WriteTransaction: transaction:(ReadWriteTsContext<'TSchema> -> unit) -> unit
    abstract ExecuteSql: (ReadWriteTsContext<'TSchema> * string) -> unit
    abstract ExecuteSql: sql: (string) -> unit
    abstract ExecuteSql: (ReadOnlyTsContext<'TSchema> * string) -> System.Collections.Generic.List<'TResult> voption
    abstract ExecuteSql: sql: (string) -> System.Collections.Generic.List<'TResult> voption

type StereoDbEngine<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    let _rCtx = ReadOnlyTsContext(schema)
    let _rwCtx = ReadWriteTsContext(schema)

    let readQueryExecution (context: ReadOnlyTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write _ ->
            failwith "Execution of UPDATE and DELETE queries from this method is not supported. Please use ExecuteSql<T>(string)"
        | QueryBuilder.Read caller -> 
            let value = caller.Invoke context
            if value = null then ValueNone else ValueSome (value :?> System.Collections.Generic.List<'T>)

    let writeQueryExecution (context: ReadWriteTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write caller ->
            caller.Invoke context
        | QueryBuilder.Read _ -> failwith "Execution of SELECT query from this method is not supported. Please use ExecuteSql<T>(string)"
            
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
        this.WriteTransaction (fun x -> writeQueryExecution x func)
        
    member this.ExecuteSql(context: ReadWriteTsContext<'TSchema>, sql: string) =
        let query = SqlParser.parseSql sql
        let func = QueryBuilder.buildQuery<'TSchema, unit> query _rwCtx schema
        writeQueryExecution context func
        
    member this.ExecuteSql<'T>(sql: string): System.Collections.Generic.List<'T> voption =
        let query = SqlParser.parseSql sql
        let func = QueryBuilder.buildQuery<'TSchema, 'T> query _rwCtx schema
        this.ReadTransaction (fun x -> readQueryExecution x func)
        
    member this.ExecuteSql<'T>(context: ReadOnlyTsContext<'TSchema>, sql: string): System.Collections.Generic.List<'T> voption =
        let query = SqlParser.parseSql sql
        let func = QueryBuilder.buildQuery<'TSchema, 'T> query _rwCtx schema
        readQueryExecution context func
                
    static member CreateTable() =
        StereoDbTable<'TId, 'TEntity>()
        :> ITable<'TId, 'TEntity>