namespace StereoDB.Table

open System
open System.Buffers
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.IO
open StereoDB
open StereoDB.Infra.Utils
open StereoDB.SecondaryIndex
open StereoDB.Storage

type internal TableRecord<'TEntity> = {
    mutable Entity: 'TEntity
    mutable IsEmpty: bool
}

type internal MemoryData<'TId, 'TEntity> = {
    TableIndex: byte
    Data: Dictionary<'TId, TableRecord<'TEntity>>
    Indexes: ResizeArray<ISecondaryIndex<'TId, 'TEntity>>
}

type internal ChangeTracking<'TId,'TEntity> = {
    mutable Changes: Dictionary<'TId, ChangedRecord<'TId,'TEntity>>
    mutable IsEnabled: bool
}

module internal Changes =
    
    let emptyDict = Dictionary<byte,byte>()  
    
    let createPool () =
        ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>()

    let rentDictForChanges (pool: ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>) =
        match pool.TryDequeue() with
        | true, v  -> v
        | false, _ -> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>()
        
    let returnChangesToPool
        (pool: ConcurrentQueue<Dictionary<'TId, ChangedRecord<'TId,'TEntity>>>)
        (changes: Collections.IDictionary) =
        
        if not (Object.ReferenceEquals(changes, emptyDict)) then
            changes.Clear()            
            pool.Enqueue(changes :?> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>)        
    
module internal TableOperations =
    
    let createMemData<'TId, 'TEntity when 'TId: equality> tableName =
        { TableIndex = tableName |> DeterministicHash.strToHash |> DeterministicHash.mapToByte
          Data = Dictionary<'TId, TableRecord<'TEntity>>()
          Indexes = ResizeArray<ISecondaryIndex<'TId,'TEntity>>() }
    
    let inline getIds (data: Dictionary<'TId,TableRecord<'TEntity>>) =
        data.Keys |> Seq.map id 

    let inline get id (data: Dictionary<'TId,TableRecord<'TEntity>>) =
        match data.TryGetValue id with
        | true, v  -> ValueSome v.Entity
        | false, _ -> ValueNone
        
    let set id entity
        (changeTracking: ChangeTracking<'TId,'TEntity>)
        (memData: MemoryData<'TId,'TEntity>) =
        
        match memData.Data.TryGetValue id with
        | true, oldRecord ->
            for index in memData.Indexes do
                index.TryReIndex(id, oldRecord.Entity, entity)
            
            oldRecord.Entity <- entity
            oldRecord.IsEmpty <- false
            memData.Data[id] <- oldRecord
            
            if changeTracking.IsEnabled then
                changeTracking.Changes[id] <- { Id = id; Entity = entity; IsRemoved = false }
        
        | _ ->
            for index in memData.Indexes do
                index.AddToIndex(id, entity)
                
            memData.Data[id] <- { Entity = entity; IsEmpty = false }
            
            if changeTracking.IsEnabled then
                changeTracking.Changes[id] <- { Id = id; Entity = entity; IsRemoved = false }
                
    let delete id
        (changeTracking: ChangeTracking<'TId,'TEntity>)
        (memData: MemoryData<'TId,'TEntity>) =            
        
        match memData.Data.TryGetValue id with
        | true, record ->                
            for index in memData.Indexes do
                index.RemoveFromIndex(id, record.Entity)                
        
            if changeTracking.IsEnabled then
                changeTracking.Changes[id] <- { Id = id; Entity = Unchecked.defaultof<_>; IsRemoved = true }
        
        | false, _ -> ()        
        
        memData.Data.Remove id
        
    let writeToLog<'TId,'TEntity> tableIndex (tableChanges: Collections.IDictionary) (storage: StorageLog option) =
        match storage with
        | Some store when tableChanges.Count > 0 ->            
            let tableChanges = tableChanges :?> Dictionary<'TId, ChangedRecord<'TId,'TEntity>>
            store.WriteStartBulk()                
            let recordsCount = store.WriteTableChanges(tableIndex, tableChanges)                
            store.WriteEndBulk recordsCount            
        
        | _ -> ()
        
module internal TableIndex =
    
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
