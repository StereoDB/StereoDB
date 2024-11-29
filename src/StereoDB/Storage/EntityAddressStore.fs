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
open RepoDb.Enumerations
open IcedTasks
open StereoDB

module internal Sqlite = 
    
    let orderByAddress = OrderField.Parse({| Address = Order.Ascending |})
    
    [<Literal>]
    let EntityAddressTableQuery =
        $"""
            CREATE TABLE IF NOT EXISTS EntityAddress (
                Id TEXT NOT NULL,
                TableIndex INTEGER NOT NULL,
                Address INTEGER NOT NULL,
                PRIMARY KEY (Id, TableIndex)
            );
            
            CREATE INDEX IF NOT EXISTS idx_address_asc ON EntityAddress(Address ASC);
        """
        
    let createEntityAddressTable (connection: SqliteConnection) =
        connection.Open()
        use command = new SqliteCommand(EntityAddressTableQuery, connection)
        command.ExecuteNonQuery() |> ignore        
    
    let updateAddresses (connection: SqliteConnection) (addresses: EntityAddress seq) =
        let forUpsert = addresses |> Seq.filter(fun x -> not (EntityAddress.isRemoved x))
        let forDelete = addresses |> Seq.filter(EntityAddress.isRemoved)
        
        use transaction = connection.EnsureOpen().BeginTransaction()
        
        try
            let rowsUpdated = connection.MergeAll(forUpsert, transaction = transaction)
            
            if not (Seq.isEmpty forDelete) then
                let rowsDeleted = connection.Delete<EntityAddress>(forDelete, transaction = transaction)
                ignore rowsDeleted
                
            transaction.Commit()
            Ok ()
        with
            ex -> transaction.Rollback()
                  Error ex
    
    let loadAddresses (connection: SqliteConnection) page rowsPerPage =        
        connection.EnsureOpen().BatchQuery<EntityAddress>(
            page,
            rowsPerPage,
            orderBy = orderByAddress,
            where = fun x -> x.Address > 0
        )       
    
type internal EntityAddressStore(fasterLog: FasterLog,
                                 connection: SqliteConnection,
                                 // tableIndex * logEntry * logAddress
                                 getEntityAddress: byte * ReadOnlyMemory<byte> * int64 -> Result<EntityAddress,exn>,
                                 // tableIndex * logEntry
                                 updateEntity: byte * ReadOnlyMemory<byte> -> unit) =
    
    let mutable _latestSavedAddress = 0L
    
    let saveCheckpoint (records: Dictionary<string, EntityAddress>) =
        let addresses = records |> Seq.map(_.Value)
        match Sqlite.updateAddresses connection addresses with
        | Ok _ ->
            let maxAddress = addresses |> EntityAddress.getMaxAddress
            _latestSavedAddress <- maxAddress.Address            
            
        | Error ex -> ()        
    
    member this.LatestSavedAddress = _latestSavedAddress
    
    member this.LoadEntities() = valueTask {        
        let mutable stop = false
        let mutable pageNumber = 50
        
        while not stop do
            let addresses = Sqlite.loadAddresses connection pageNumber 10
            
            if Seq.isEmpty addresses then
                stop <- true        
            else
                for address in addresses do
                    let! memoryOwner, length = fasterLog.ReadAsync(address.Address, MemoryPool.Shared)
                    let tableIndex = memoryOwner.Memory.Span[0]
                    let logEntry = memoryOwner.Memory.Slice(1, length - 1) // skip tableIndex
                    updateEntity(tableIndex, logEntry)
                    _latestSavedAddress <- address.Address
            
            pageNumber <- pageNumber + 1
            
        return _latestSavedAddress            
    }
    
    member this.StartCheckpoint(cancelToken: CancellationToken) = task {        
        do! Task.Yield()        
        
        use iterator = fasterLog.Scan(_latestSavedAddress, fasterLog.SafeTailAddress, name = null, recover = false)
    
        let records = Dictionary<string, EntityAddress>(100)
        let mutable entry: IMemoryOwner<byte> = null
        let mutable currentAddress = 0L
        let mutable entryLength = 0
        
        while iterator.GetNext(MemoryPool.Shared, &entry, &entryLength, &currentAddress) do
            use e = entry
            let tableIndex = entry.Memory.Span[0]
            
            if not cancelToken.IsCancellationRequested && records.Count >= Constants.LoadAddressPullBatchSize then
                saveCheckpoint records
                records.Clear()
            
            if not cancelToken.IsCancellationRequested && tableIndex <> Constants.BulkRecordTableIndex then            
                
                let logEntry = entry.Memory.Slice(1, entryLength - 1) // skip tableIndex
                
                match getEntityAddress(tableIndex, logEntry, currentAddress) with
                | Ok recordAddress ->
                    let id = EntityAddress.getCompositeKey recordAddress
                    records[id] <- recordAddress
                    
                | Error ex -> ()

        // final check for checkpoint               
        if not cancelToken.IsCancellationRequested && records.Count > 0 then
            saveCheckpoint records            
    }
    
    interface IDisposable with
        member this.Dispose() =
            connection.Dispose()
            
    static member Init(fasterLog, getEntityAddress, updateEntity) =
        GlobalConfiguration.Setup().UseSqlite() |> ignore
        //todo: add WAL and other perf optimizations
        let connection = new SqliteConnection("Data Source=stereo_db_key_store;") // Pooling=True;Max Pool Size=100;        
        Sqlite.createEntityAddressTable connection
        new EntityAddressStore(fasterLog, connection, getEntityAddress, updateEntity)