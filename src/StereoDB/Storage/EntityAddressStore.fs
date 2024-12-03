namespace StereoDB.Storage
#nowarn "3391" // warning about implicit conversion

open System
open System.Buffers
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FASTER.core
open Microsoft.Data.Sqlite
open RepoDb
open IcedTasks
open StereoDB

type internal EntityAddressStore(fasterLog: FasterLog,
                                 connection: SqliteConnection,
                                 // tableId -> logEntry -> logAddress
                                 getEntityAddress: byte -> ReadOnlyMemory<byte> -> int64 -> Result<EntityAddress,exn>,
                                 // tableId -> logEntry
                                 updateEntity: byte -> ReadOnlyMemory<byte> -> unit) =
    
    let mutable _latestSavedAddress = 0L
    
    let saveCheckpoint (records: Dictionary<string, EntityAddress>) =
        let addresses = records |> Seq.map(_.Value)
        match Sqlite.updateAddresses connection addresses with
        | Ok _ ->
            let maxAddress = addresses |> EntityAddress.getMaxAddress
            _latestSavedAddress <- maxAddress.Address            
            
        | Error ex -> ()
    
    member this.LatestSavedAddress = _latestSavedAddress
    
    member this.GetLowestAddress() = Sqlite.getLowestAddress connection
    
    member this.LoadEntities() = valueTask {        
        let mutable stop = false
        let mutable pageNumber = 0
        
        while not stop do
            let addresses = Sqlite.loadAddresses connection pageNumber Constants.LoadAddressPullBatchSize
            
            if Seq.isEmpty addresses then
                stop <- true        
            else
                for address in addresses do
                    let! memoryOwner, length = fasterLog.ReadAsync(address.Address, MemoryPool.Shared)
                    if length > 0 then
                        let tableId = memoryOwner.Memory.Span[0]
                        let logEntry = memoryOwner.Memory.Slice(1, length - 1) // skip tableIndex
                        updateEntity tableId logEntry
                        _latestSavedAddress <- address.Address
            
            pageNumber <- pageNumber + 1
            
        return _latestSavedAddress            
    }
    
    member this.StartCheckpoint(cancelToken: CancellationToken) = task {        
        do! Task.Yield()        

        use iterator = fasterLog.Scan(_latestSavedAddress, fasterLog.TailAddress, name = null, recover = false)
    
        let records = Dictionary<string, EntityAddress>(100)
        let mutable entry: IMemoryOwner<byte> = Unchecked.defaultof<_>
        let mutable currentAddress = 0L
        let mutable entryLength = 0
        
        while iterator.GetNext(MemoryPool.Shared, &entry, &entryLength, &currentAddress) do
            use e = entry
            let tableId = entry.Memory.Span[0]
            
            if not cancelToken.IsCancellationRequested && records.Count >= Constants.LoadAddressPullBatchSize then
                saveCheckpoint records
                records.Clear()
            
            if not cancelToken.IsCancellationRequested && tableId <> Constants.BulkRecordTableId then            
                
                let logEntry = entry.Memory.Slice(1, entryLength - 1) // skip tableIndex
                
                match getEntityAddress tableId logEntry currentAddress with
                | Ok recordAddress ->
                    let id = EntityAddress.getCompositeKey recordAddress
                    records[id] <- recordAddress
                    
                | Error ex -> ()

        // final check for checkpoint               
        if not cancelToken.IsCancellationRequested && records.Count > 0 then
            saveCheckpoint records            
    }
    
    interface IAsyncDisposable with
        member this.DisposeAsync() =
            valueTask {
                do! this.StartCheckpoint CancellationToken.None
                connection.Dispose()
            }
            |> ValueTask.toUnit
            
    static member Init(sqliteDbPath, fasterLog, getEntityAddress, updateEntity) =
        GlobalConfiguration.Setup().UseSqlite() |> ignore
        
        let connection = new SqliteConnection(Sqlite.Query.createConnectionString sqliteDbPath)
        Sqlite.execCommand connection Sqlite.Query.CreateEntityAddressTable
        Sqlite.execCommand connection Sqlite.Query.OptimizationSetup
        
        EntityAddressStore(fasterLog, connection, getEntityAddress, updateEntity)