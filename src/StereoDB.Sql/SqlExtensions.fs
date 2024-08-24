namespace StereoDB.FSharp

[<AutoOpen>]
module SqlExtensions =
    open StereoDB
    open StereoDB.Sql
           
    let internal readQueryExecution (context: ReadOnlyTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write _ ->
            failwith "Execution of UPDATE and DELETE queries from this method is not supported. Please use ExecuteSql<T>(string)"
        
        | QueryBuilder.Read caller -> 
            let value = caller.Invoke context
            if value = null then ValueNone else ValueSome (value :?> System.Collections.Generic.List<'T>)

    let internal writeQueryExecution (context: ReadWriteTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write caller -> caller.Invoke context
        | QueryBuilder.Read _       -> failwith "Execution of SELECT query from this method is not supported. Please use ExecuteSql<T>(string)"

    type StereoDB.FSharp.IStereoDb<'TSchema> with
        member this.ExecuteNonQuery(sql: string) =
            let query = SqlParser.parseSql sql
            this.WriteTransaction(fun (_rwCtx) -> 
                let func = QueryBuilder.buildQuery<'TSchema, unit> query _rwCtx _rwCtx.Schema
                writeQueryExecution _rwCtx func)

        member this.ExecSql<'TResult>(sql: string): ResizeArray<'TResult> voption =
            let query = SqlParser.parseSql sql
            this.ReadTransaction(fun (_rCtx) -> 
                let func = QueryBuilder.buildQuery<'TSchema, 'TResult> query _rCtx _rCtx.Schema
                readQueryExecution _rCtx func)              
    
        // abstract ExecSql: transaction:(ReadOnlyTsContext<'TSchema> * string) -> ResizeArray<'TResult> voption
        // abstract ExecSql: transaction:(ReadWriteTsContext<'TSchema> * string) -> unit