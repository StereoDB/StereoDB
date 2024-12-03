namespace StereoDB.Storage

open System.IO
open System.Runtime.CompilerServices
open MessagePack
open StereoDB

[<MessagePackObject; Struct; IsReadOnly>]
type internal BulkHeader = {
    [<Key(0)>] BulkNumber: int64
    [<Key(1)>] IsStart: bool
    [<Key(2)>] RecordsCount: int
}

[<MessagePackObject; Struct; IsReadOnly>]
type internal RecordHeader<'TId> = {
    [<Key(0)>] Id: 'TId
    [<Key(1)>] IsRemoved: bool    
}

[<Struct; IsReadOnly>]
type internal ChangedRecord<'TId, 'TEntity> = {
    Id: 'TId
    Entity: 'TEntity
    IsRemoved: bool        
}

type EntityAddress = {
    Id: string
    TableId: byte
    Address: int64
}

// todo: optimise string allocation
module internal EntityAddress =
        
    let inline getCompositeKey (record: EntityAddress) =
        $"{record.Id}-{record.TableId}"
        
    let inline isRemoved (record: EntityAddress) =
        record.Address = 0
        
    let inline createRemoved id tableId =
        { Id = id.ToString(); TableId = tableId; Address = 0 }
        
    let inline create id tableId address =
        { Id = id.ToString(); TableId = tableId; Address = address }
        
    let inline getMaxAddress (addresses: EntityAddress seq) =
        addresses |> Seq.maxBy(_.Address)
        
module internal StorageOperations =

    let createDbFilePath dbFolder =
        let dbLogFolder = Path.Combine(dbFolder, $"{Constants.DbFileName}_log")
        let sqliteDb = Path.Combine(dbLogFolder, $"{Constants.DbFileName}_sqlite")
        {| DbLogFolder = dbLogFolder; SqliteDbPath = sqliteDb |}
        
    let removeDb dbFolder =
        try
            let filePath = createDbFilePath dbFolder        
            Directory.Delete(filePath.DbLogFolder, recursive = true)
        with
            ex -> ()            