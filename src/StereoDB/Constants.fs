module internal StereoDB.Constants

open System

[<Literal>]
let BulkRecordTableIndex = 0uy

[<Literal>]
let LoadAddressPullBatchSize = 100 // how many records we pull from Sqlite per query

[<Literal>]
let WriteTsLimitToCheckpoint = 2_000 // how many write transactions we wait before checkpoint  

let AutoCommitDelay = TimeSpan.FromSeconds 2
let CheckpointDelay = TimeSpan.FromMinutes 1