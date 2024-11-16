namespace FasterDemo.Contracts

open System
open System.Buffers
open System.Runtime.CompilerServices
open MessagePack

// TPL DataFlow for serialization/deserialization
// Layout: 1Byte (TableIndex) * RecordHeader<'TId> * 'TEntity

[<MessagePackObject>]
type User = {
    [<Key(0)>] Id: int
    [<Key(1)>] Age: int
    [<Key(2)>] Data: byte[]
}

[<MessagePackObject>]
[<Struct; IsReadOnly>] // maybe IsByRef
type RecordHeader<'T> = {
    [<Key(0)>] Id: 'T
    [<Key(1)>] IsRemoved: bool
    [<Key(2)>] TableIndex: byte
    
    // [<Key(3)>] Offset: int64
}

type DbRecord<'T> = {
    IsOnDisk: bool
    Offset: int64
    Value: 'T
}