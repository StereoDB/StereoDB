module StereoDB.Redis.Schema
open StereoDB.FSharp
open StereoDB

type RedisRecord = {
    Id: string
    Value: string
}

type Schema() =
    let _records = {| Table = StereoDb.createTable<string, RedisRecord>("records") |}
    
    member this.Records = _records
    
    interface IDbSchema with
        member this.AllTables = [_records.Table]