module FasterDemo.App

open System
open System.Buffers
open System.Collections.Generic
open FASTER.core
open FasterDemo.Contracts
open MessagePack
open Microsoft.IO
open FsToolkit.ErrorHandling
open IcedTasks
open System.Runtime.Serialization

#nowarn "3391"

let memoryManager = RecyclableMemoryStreamManager()
let config = new FasterLogSettings("stereo_db", deleteDirOnDispose = false)
config.SegmentSize <- 4194304
let log = new FasterLog(config)
let logAddresses = Dictionary<int,int64>()

let dd = ArrayPool<int>.Shared.Rent(10)
ArrayPool<int>.Shared.Return(dd)

let write (users: User list) =
    for u in users do
        use stream = memoryManager.GetStream()
        // let header = encodeMsgHeader 1uy false
        // stream.WriteByte header
        
        let header = { Id = u.Id; IsRemoved = false; TableIndex = 0uy }
        MessagePackSerializer.Serialize(writer = stream, value = header)
        
        MessagePackSerializer.Serialize(writer = stream, value = u)
        let msg = stream.GetBuffer().AsSpan(0, int stream.Length)
        
        let logAddress = log.Enqueue msg
        logAddresses[u.Id] <- logAddress
    
    log.CommitAsync()
        
let deserialize (msgOwner: IMemoryOwner<byte>) =
    let mutable reader = MessagePackReader(msgOwner.Memory)
    let header = MessagePackSerializer.Deserialize<RecordHeader<int>>(&reader)
    
    let endPosition = reader.Position.GetInteger()
    let payload = msgOwner.Memory.Slice(endPosition)
    let dbRecord = MessagePackSerializer.Deserialize<User>(payload)
    
    // let span = msgOwner.Memory.Span    
    // let struct (isRemoved, tableIndex) = decodeMsgHeader span[0]
    // let dbRecordBytes = msgOwner.Memory.Slice(1)
    // let dbRecord = MessagePackSerializer.Deserialize<User>(dbRecordBytes)
    dbRecord
        
let read (address: int64) = valueTask {
    let! msg, ln = log.ReadAsync(address, MemoryPool<byte>.Shared)
    use msgOwner = msg
    return deserialize msgOwner    
}

let users =
    let data = Array.create 100_0000 1uy // 1MB
    [0..10]
    |> List.map(fun x -> { Id = x; Age = x; Data = data })

let writeReadFlow = valueTask {
    try
        do! users |> write
        // let! record1 = read logAddresses[0]
        // let! record2 = read logAddresses[1]
        return ()
    with
    | ex -> return ()    
}

// let restoreFlow = valueTask {
//     try
//         do! users |> write
//         let! record1 = read logAddresses[0]
//         let! record2 = read logAddresses[1]
//         return ()
//     with
//     | ex -> return ()    
// }

writeReadFlow.AsTask().Wait()

let truncateFlow = valueTask {
    try        
        log.TruncateUntilPageStart(logAddresses[9])
        do! log.CommitAsync()
        let! record2 = read logAddresses[9]
        let! record1 = read logAddresses[1]
        let! record0 = read logAddresses[0]
        return ()
    with
    | ex -> return ()    
}

truncateFlow.AsTask().Wait()