namespace StereoDB

open System
open System.Threading
open StereoDB
open StereoDB.Sql

type internal StereoDb<'TSchema>(schema: 'TSchema) =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    
    let _rCtx = { ReadOnlyTsContext.Schema = schema }
    let _rwCtx = { ReadWriteTsContext.Schema = schema }
           
    let readQueryExecution (context: ReadOnlyTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write _ ->
            failwith "Execution of UPDATE and DELETE queries from this method is not supported. Please use ExecuteSql<T>(string)"
        
        | QueryBuilder.Read caller -> 
            let value = caller.Invoke context
            if value = null then ValueNone else ValueSome (value :?> System.Collections.Generic.List<'T>)

    let writeQueryExecution (context: ReadWriteTsContext<'TSchema>) (func:QueryBuilder.QueryExecution<'TSchema>) =
        match func with
        | QueryBuilder.Write caller -> caller.Invoke context
        | QueryBuilder.Read _       -> failwith "Execution of SELECT query from this method is not supported. Please use ExecuteSql<T>(string)"           
           
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

        member this.ExecuteSql(sql) =
            let query = SqlParser.parseSql sql
            let func = QueryBuilder.buildQuery<'TSchema, unit> query _rwCtx schema
            writeQueryExecution _rwCtx func

        member this.ExecuteSql<'TResult>(sql: string): ResizeArray<'TResult> voption =
            let query = SqlParser.parseSql sql
            let func = QueryBuilder.buildQuery<'TSchema, unit> query _rCtx schema
            readQueryExecution _rCtx func               

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