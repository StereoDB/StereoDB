namespace StereoDB.Storage

open System.Runtime.CompilerServices
open MessagePack

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

[<Struct; IsReadOnly>]
type internal EntityAddress = {
    Id: string
    TableIndex: byte
    Address: int64
}

module internal EntityAddress =
    
    let inline getCompositeKey (record: EntityAddress) =
        $"{record.Id}-{record.TableIndex}"

module internal Constants =
    
    [<Literal>]
    let BulkRecord = 0uy