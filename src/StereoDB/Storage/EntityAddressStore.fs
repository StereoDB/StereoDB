namespace StereoDB.Storage
#nowarn "3391" // warning about implicit conversion

open System
open System.Buffers
open System.Collections.Generic
open FASTER.core
open Microsoft.Data.Sqlite
open RepoDb

module internal SqlQueries = 
    
    [<Literal>]
    let EntityAddressTableQuery =
        $"""
            CREATE TABLE IF NOT EXISTS entity_address (
                Id TEXT NOT NULL,
                TableIndex INTEGER NOT NULL,
                Address INTEGER NOT NULL,
                PRIMARY KEY (Id, TableIndex)
            );
        """
        
    let createEntityAddressTable (connection: SqliteConnection) =
        use command = new SqliteCommand(EntityAddressTableQuery, connection)
        command.ExecuteNonQuery() |> ignore    
    
type internal EntityAddressStore(fasterLog: FasterLog,
                                 // tableIndex * logEntry * logAddress
                                 getEntityAddress: byte * ReadOnlyMemory<byte> * int64 -> Result<EntityAddress,exn>) =
    
    let _connection = new SqliteConnection("Data Source=stereo_db_key_store;") // Pooling=True;Max Pool Size=100;
    
    member this.Init() =
        GlobalConfiguration.Setup().UseSqlite() |> ignore
        _connection.Open()
        SqlQueries.createEntityAddressTable _connection        
    
    member this.StartEntityAddressUpdate() =
        use iterator = fasterLog.Scan(fasterLog.BeginAddress, fasterLog.SafeTailAddress, name = null, recover = false)
        
        let records = Dictionary<string, EntityAddress>()
        let mutable entry: IMemoryOwner<byte> = null
        let mutable currentAddress = 0L
        let mutable entryLength = 0
        
        while iterator.GetNext(MemoryPool.Shared, &entry, &entryLength, &currentAddress) do
            use e = entry
            let tableIndex = entry.Memory.Span[0]
            
            if tableIndex <> Constants.BulkRecord then                
                match getEntityAddress(tableIndex, entry.Memory.Slice(1), currentAddress) with
                | Ok recordAddress ->
                    let id = EntityAddress.getCompositeKey recordAddress
                    records[id] <- recordAddress
                    
                | Error ex -> ()