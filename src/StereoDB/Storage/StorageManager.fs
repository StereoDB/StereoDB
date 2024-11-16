namespace StereoDB.Storage

open System
open System.Buffers
open System.Runtime.CompilerServices
open FASTER.core
open IcedTasks
open MessagePack
open Microsoft.IO

[<MessagePackObject; Struct; IsReadOnly>]
type RecordHeader<'T> = {
    [<Key(0)>] Id: 'T
    [<Key(1)>] IsRemoved: bool
    [<Key(2)>] Offset: int64
}

type internal StorageManager(fasterLog: FasterLog) =
        
    let _memoryManager = RecyclableMemoryStreamManager()
    let mutable _currentOffset = 0L
    
    member this.Enqueue(tableIndex, id, entity, isRemoved) =
        use stream = _memoryManager.GetStream()
        _currentOffset <- _currentOffset + 1L
        let header = { Id = id; IsRemoved = isRemoved; Offset = _currentOffset }
        
        stream.WriteByte tableIndex
        MessagePackSerializer.Serialize(writer = stream, value = header)
        
        if not isRemoved then
            MessagePackSerializer.Serialize(writer = stream, value = entity)
        
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        fasterLog.Enqueue msg
        
    member this.Read<'TId,'TEntity>(logAddress) = valueTask {
        let! memoryOwner, ln = fasterLog.ReadAsync(logAddress, MemoryPool<byte>.Shared)        
        
        let mutable reader = MessagePackReader(memoryOwner.Memory.Slice(1)) // skip tableIndex       
        let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(&reader)
        
        //todo: check how to read only body, maybe reader.Skip()        
        let endPosition = reader.Position.GetInteger()
        let payload = memoryOwner.Memory.Slice endPosition
        let entity = MessagePackSerializer.Deserialize<'TEntity>(payload)
        return entity
    }
    
    member this.Commit() = fasterLog.CommitAsync()        
    
    static member Init() =
        let config = new FasterLogSettings("stereo_db")
        let fasterLog = new FasterLog(config)
        StorageManager(fasterLog)