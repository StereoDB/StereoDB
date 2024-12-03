module internal StereoDB.Constants

open System

let [<Literal>] DbFileName = "stereo_db" 

let [<Literal>] BulkRecordTableId = 0uy

let [<Literal>] LoadAddressPullBatchSize = 100 // how many records we pull from Sqlite per query

let [<Literal>] WriteTsLimitToCheckpoint = 2_000 // how many write transactions we wait before checkpoint  

let CommitDelay = TimeSpan.FromSeconds 1

let CheckpointDelay = TimeSpan.FromMinutes 1