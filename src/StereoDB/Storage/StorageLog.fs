namespace StereoDB.Storage
#nowarn "3391" // warning about implicit conversion

open System
open System.Buffers
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FASTER.core
open IcedTasks
open MessagePack
open Microsoft.IO
open StereoDB.Infra.Utils

type internal StorageLog(fasterLog: FasterLog, updateEntity: byte * ReadOnlyMemory<byte> -> unit) = // tableIndex * entry
    
    let _memoryManager = RecyclableMemoryStreamManager()
    let mutable _currentBulkNumber = 0L
    
    let writeBulk (bulk: BulkHeader) =
        use stream = _memoryManager.GetStream()
        
        stream.WriteByte Constants.BulkRecord
        MessagePackSerializer.Serialize(writer = stream, value = bulk)
        
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        fasterLog.Enqueue msg
    
    let writeChange tableIndex (change: ChangedRecord<'TId, 'TEntity>) =        
        use stream = _memoryManager.GetStream()
        
        stream.WriteByte tableIndex
        let header = { Id = change.Id; IsRemoved = change.IsRemoved }
        MessagePackSerializer.Serialize(writer = stream, value = header)
        
        if not header.IsRemoved then        
            MessagePackSerializer.Serialize(writer = stream, value = change.Entity)
        
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        fasterLog.Enqueue msg        
    
    let writeTableChanges tableIndex (tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =
        let chArray = ArrayPool.Shared.Rent tableChanges.Count
        let mutable itemsWritten = 0        
        try            
            Array.copyDictToArray tableChanges chArray
            
            Parallel.For(0, tableChanges.Count, fun i ->
                let change = chArray[i]
                try
                    change |> writeChange(tableIndex) |> ignore
                    Interlocked.Increment(&itemsWritten) |> ignore
                with
                    ex -> ()
            )
            |> ignore
        finally
            ArrayPool.Shared.Return chArray
            
        itemsWritten
        
    let writeTableChangesSingleThreaded tableIndex (tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =        
        let mutable itemsWritten = 0        
        
        for ch in tableChanges do
            let change = ch.Value
            try
                change |> writeChange(tableIndex) |> ignore
                itemsWritten <- itemsWritten + 1
            with
                ex -> ()
            
        itemsWritten
    
    member this.FasterLog = fasterLog
    
    member this.WriteStartBulk() =
        _currentBulkNumber <- _currentBulkNumber + 1L
        let startBulk = { BulkNumber = _currentBulkNumber; IsStart = true; RecordsCount = 0 }        
        writeBulk startBulk |> ignore
        
    member this.WriteEndBulk(recordsCount) =        
        let endBulk = { BulkNumber = _currentBulkNumber; IsStart = false; RecordsCount = recordsCount }
        writeBulk endBulk |> ignore
    
    member this.WriteTableChanges(tableIndex, tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =        
        // writeTableChanges tableIndex tableChanges
        writeTableChangesSingleThreaded tableIndex tableChanges
        
    member this.Read<'TId,'TEntity>(logAddress) = valueTask {
        let! memoryOwner, ln = fasterLog.ReadAsync(logAddress, MemoryPool.Shared)        
        
        let mutable reader = MessagePackReader(memoryOwner.Memory.Slice(1)) // skip tableIndex       
        let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(&reader)
        
        //todo: check how to read only body, maybe reader.Skip()        
        let endPosition = reader.Position.GetInteger()
        let payload = memoryOwner.Memory.Slice endPosition
        let entity = MessagePackSerializer.Deserialize<'TEntity>(payload)
        return entity
    }
    
    member this.CommitAsync() =
        fasterLog.Commit(spinWait = true)
        fasterLog.CommitAsync()        
    
    member this.Restore() =
        use iterator = fasterLog.Scan(fasterLog.BeginAddress, fasterLog.SafeTailAddress, name = null, recover = false)
        
        let mutable entry: IMemoryOwner<byte> = null
        let mutable currentAddress = 0L
        let mutable entryLength = 0        
        
        while iterator.GetNext(MemoryPool.Shared, &entry, &entryLength, &currentAddress) do
            use e = entry
            let tableIndex = entry.Memory.Span[0]             
            if tableIndex <> Constants.BulkRecord then
                let logEntry = entry.Memory.Slice(1, entryLength - 1) // skip tableIndex
                updateEntity(tableIndex, logEntry)            
    
    interface IDisposable with
        member this.Dispose() =
            fasterLog.Commit(spinWait = true)
            fasterLog.Dispose()            
    
    static member Init(updateEntity) =
        let config = new FasterLogSettings("stereo_db", deleteDirOnDispose = false)
        let fasterLog = new FasterLog(config)
        new StorageLog(fasterLog, updateEntity)