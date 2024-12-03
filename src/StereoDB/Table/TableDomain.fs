module internal StereoDB.Table.Domain

open System
open System.Collections.Concurrent
open System.Collections.Generic
open MessagePack
open StereoDB
open StereoDB.Infra.Utils
open StereoDB.SecondaryIndex
open StereoDB.Storage

type TableRecord<'TEntity> = {
    mutable Entity: 'TEntity
    mutable IsEmpty: bool
}

type ChangeTracking<'TId,'TEntity> = {
    mutable Changes: Dictionary<'TId, ChangedRecord<'TId,'TEntity>>
    mutable IsEnabled: bool
}

type MemoryData<'TId,'TEntity> = {    
    TableId: byte
    Data: Dictionary<'TId, TableRecord<'TEntity>>
    Indexes: ResizeArray<ISecondaryIndex<'TId,'TEntity>>
    ChangeTracking: ChangeTracking<'TId,'TEntity>    
}

module ChangesDictPool =
    
    let emptyDict = Dictionary<byte,byte>()  
    
    let createPool () =
        ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>()

    let rentDict (pool: ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>) =
        match pool.TryDequeue() with
        | true, v  -> v
        | false, _ -> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>()
        
    let returnDictToPool
        (pool: ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>)
        (changes: Collections.IDictionary) =
        
        if not (Object.ReferenceEquals(changes, emptyDict)) then
            changes.Clear()            
            pool.Enqueue(changes :?> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>)        
    
module TableOperations =
    
    let parseTableId tableName =
        tableName |> DeterministicHash.strToHash |> DeterministicHash.mapToByte |> DeterministicHash.castToNotReservedBytes
    
    let createMemoryData tableName changesDictPool =
        let tableId = parseTableId tableName        
        let _changeTracking = { Changes = ChangesDictPool.rentDict changesDictPool; IsEnabled = false }
        
        { TableId = tableId
          Data = Dictionary()
          Indexes = ResizeArray()
          ChangeTracking = _changeTracking }
    
    let inline getIds (data: Dictionary<'TId,TableRecord<'TEntity>>) =
        data.Keys |> Seq.map id 

    let inline get id (data: Dictionary<'TId,TableRecord<'TEntity>>) =
        match data.TryGetValue id with
        | true, v  -> ValueSome v.Entity
        | false, _ -> ValueNone
        
    let set id entity (memData: MemoryData<'TId,'TEntity>) =
        
        match memData.Data.TryGetValue id with
        | true, oldRecord ->
            for index in memData.Indexes do
                index.TryReIndex(id, oldRecord.Entity, entity)
            
            oldRecord.Entity <- entity
            oldRecord.IsEmpty <- false
            memData.Data[id] <- oldRecord
            
            if memData.ChangeTracking.IsEnabled then
                memData.ChangeTracking.Changes[id] <- { Id = id; Entity = entity; IsRemoved = false }
        
        | _ ->
            for index in memData.Indexes do
                index.AddToIndex(id, entity)
                
            memData.Data[id] <- { Entity = entity; IsEmpty = false }
            
            if memData.ChangeTracking.IsEnabled then
                memData.ChangeTracking.Changes[id] <- { Id = id; Entity = entity; IsRemoved = false }
                
    let delete id (memData: MemoryData<'TId,'TEntity>) =            
        
        match memData.Data.TryGetValue id with
        | true, record ->                
            for index in memData.Indexes do
                index.RemoveFromIndex(id, record.Entity)                
        
            if memData.ChangeTracking.IsEnabled then
                memData.ChangeTracking.Changes[id] <- { Id = id; Entity = Unchecked.defaultof<_>; IsRemoved = true }
        
        | false, _ -> ()        
        
        memData.Data.Remove id
        
    let writeToLog<'TId,'TEntity> tableId (tableChanges: Collections.IDictionary) (storage: StorageLog option) =
        match storage with
        | Some store when tableChanges.Count > 0 ->            
            let tableChanges = tableChanges :?> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>
            store.WriteStartBulk()                
            let recordsCount = store.WriteTableChanges(tableId, tableChanges)                
            store.WriteEndBulk recordsCount            
        
        | _ -> ()
        
    let updateEntity (memData: MemoryData<'TId, 'TEntity>)
                     (logEntry: ReadOnlyMemory<byte>)
                     (entitySerializer: IEntitySerializer) =        
        try
            let mutable reader = MessagePackReader(logEntry)
            let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(&reader, MessagePack.defaultOptions)
            if header.IsRemoved then
                delete header.Id memData |> ignore
            else
                let endPosition = reader.Position.GetInteger() - 1
                let payload = logEntry.Slice endPosition                
                let entity = entitySerializer.Deserialize<'TEntity>(payload)                
                set header.Id entity memData
        with
            ex -> ()
            
    let getEntityAddress<'TId> tableId (logEntry: ReadOnlyMemory<byte>) logAddress =
        try
            let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(logEntry, MessagePack.defaultOptions)
            if header.IsRemoved then
                Ok (EntityAddress.createRemoved header.Id tableId)
            else
                Ok (EntityAddress.create header.Id tableId logAddress)
        with
            ex -> Error ex            
        
module SecondaryIndex =
    
    let addValueIndex (memData: MemoryData<'TId, 'TEntity>) (getValue: Func<'TEntity, 'TValue>) =
        let index = ValueIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)            
        memData.Indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match memData.Data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }
        
    let addMultiValueIndex (memData: MemoryData<'TId, 'TEntity>) (getValues: Func<'TEntity, 'TValue seq>) (unsafeReindexByObjRefCompare) =
        let index = MultiValueIndex<'TId, 'TEntity, 'TValue>(getValues.Invoke, unsafeReindexByObjRefCompare)            
        memData.Indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match memData.Data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }       
    
    let addRangeScanIndex (memData: MemoryData<'TId, 'TEntity>) (getValue: Func<'TEntity, 'TValue>) =
        let index = RangeScanIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)
        memData.Indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IRangeScanIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match memData.Data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
                    
                member this.SelectRange(fromValue, toValue) =
                    let ids = index.SelectRangeIds(fromValue, toValue)
                    seq {
                        for id in ids do
                            match memData.Data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }
