namespace StereoDB.Storage

open Microsoft.Data.Sqlite
open RepoDb
open RepoDb.Enumerations
open StereoDB.Infra.Utils

module internal Sqlite = 
    
    module Query =
    
        let [<Literal>] ConnectionString = "Data Source=stereo_db_sqlite;"    
    
        let [<Literal>] OptimizationSetup = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA journal_size_limit = 6144000;"
        
        let [<Literal>] CreateEntityAddressTable =
            $"""
                CREATE TABLE IF NOT EXISTS EntityAddress (
                    Id TEXT NOT NULL,
                    TableId INTEGER NOT NULL,
                    Address INTEGER NOT NULL,
                    PRIMARY KEY (Id, TableId)
                );
                
                CREATE INDEX IF NOT EXISTS idx_address_asc ON EntityAddress(Address ASC);
            """
    
    let orderByAddress = OrderField.Parse({| Address = Order.Ascending |})    
        
    let execCommand (connection: SqliteConnection) command =
        connection.Open()
        use command = new SqliteCommand(command, connection)        
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
        
    let getLowestAddress (connection: SqliteConnection) =
        let result = connection.EnsureOpen().Query<EntityAddress>(
            where = (fun x -> x.Address > 0),
            orderBy = orderByAddress,
            top = 1
        )
        result |> Seq.tryHeadV |> ValueOption.map(_.Address)

