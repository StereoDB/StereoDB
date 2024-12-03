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
open StereoDB
open StereoDB.Infra.Utils

type internal StorageLog(fasterLog: FasterLog, updateEntity: byte -> ReadOnlyMemory<byte> -> unit) = // tableId * entry
    
    let _memoryManager = RecyclableMemoryStreamManager()
    let mutable _currentBulkNumber = 0L
    
    let writeBulk (bulk: BulkHeader) =
        use stream = _memoryManager.GetStream()
        
        stream.WriteByte Constants.BulkRecordTableId
        MessagePackSerializer.Serialize(writer = stream, value = bulk, options = MessagePack.defaultOptions)
        
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        fasterLog.Enqueue msg
    
    let writeChange tableId (change: ChangedRecord<'TId, 'TEntity>) =        
        use stream = _memoryManager.GetStream()
        
        stream.WriteByte tableId
        let header = { Id = change.Id; IsRemoved = change.IsRemoved }
        MessagePackSerializer.Serialize(writer = stream, value = header, options = MessagePack.defaultOptions)
        
        if not header.IsRemoved then        
            MessagePackSerializer.Serialize(writer = stream, value = change.Entity, options = MessagePack.defaultOptions)
        
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        fasterLog.Enqueue msg        
    
    let writeTableChanges tableId (tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =
        let chArray = ArrayPool.Shared.Rent tableChanges.Count
        let mutable itemsWritten = 0        
        try            
            Array.copyDictToArray tableChanges chArray
            
            Parallel.For(0, tableChanges.Count, fun i ->
                let change = chArray[i]
                try
                    change |> writeChange(tableId) |> ignore
                    Interlocked.Increment(&itemsWritten) |> ignore
                with
                    ex -> ()
            )
            |> ignore
        finally
            ArrayPool.Shared.Return chArray
            
        itemsWritten
        
    let writeTableChangesSingleThreaded tableId (tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =        
        let mutable itemsWritten = 0        
        
        for ch in tableChanges do
            let change = ch.Value
            try
                change |> writeChange(tableId) |> ignore
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
    
    member this.WriteTableChanges(tableId, tableChanges: Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>) =        
        // writeTableChanges tableId tableChanges
        writeTableChangesSingleThreaded tableId tableChanges
        
    // member this.Read<'TId,'TEntity>(logAddress) = valueTask {
    //     let! memoryOwner, ln = fasterLog.ReadAsync(logAddress, MemoryPool.Shared)        
    //     
    //     let mutable reader = MessagePackReader(memoryOwner.Memory.Slice(1)) // skip tableId       
    //     let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(&reader, options = MessagePack.defaultOptions)
    //     
    //     //todo: check how to read only body, maybe reader.Skip()        
    //     let endPosition = reader.Position.GetInteger()
    //     let payload = memoryOwner.Memory.Slice endPosition
    //     let entity = MessagePackSerializer.Deserialize<'TEntity>(payload, options = MessagePack.defaultOptions)
    //     return entity
    // }
    
    member this.RestoreFrom(fromAddress) =        
        use iterator = fasterLog.Scan(fromAddress, fasterLog.TailAddress, name = null, recover = false)
        
        let mutable entry: IMemoryOwner<byte> = Unchecked.defaultof<_>
        let mutable currentAddress = 0L
        let mutable entryLength = 0
        
        while iterator.GetNext(MemoryPool.Shared, &entry, &entryLength, &currentAddress) do
            use e = entry
            let tableId = entry.Memory.Span[0]             
            if tableId <> Constants.BulkRecordTableId then
                let logEntry = entry.Memory.Slice(1, entryLength - 1) // skip tableIndex
                updateEntity tableId logEntry  
    
    member this.CommitAsync() =
        fasterLog.CommitAsync()        
    
    member this.TruncateUntil(fromAddress) =
        fasterLog.TruncateUntilPageStart(fromAddress)
        fasterLog.CommitAsync()
    
    interface IAsyncDisposable with
        member this.DisposeAsync() =
            valueTask {
                do! fasterLog.CommitAsync()
                fasterLog.Dispose()
            }
            |> ValueTask.toUnit
            
    static member Init(dbLogFolder, updateEntity) =        
        let config = new FasterLogSettings(dbLogFolder, deleteDirOnDispose = false)
        let fasterLog = new FasterLog(config)
        StorageLog(fasterLog, updateEntity)