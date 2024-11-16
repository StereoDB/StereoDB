namespace StereoDB

open System
open System.Buffers
open System.Collections.Generic
open StereoDB
open StereoDB.Infra.Utils
open StereoDB.SecondaryIndex
open StereoDB.Storage

type internal TableRecord<'TEntity> = {
    mutable Entity: 'TEntity
    mutable IsEmpty: bool
    mutable LogAddress: int64
    mutable Version: byte    
}

type internal ChangedRecord<'TId, 'TEntity> = {
    Id: 'TId
    Entity: 'TEntity
    IsRemoved: bool
    Version: byte    
    mutable LogAddress: int64
}

type internal RecordsForCommit<'TId, 'TEntity> = {
    mutable PoolData: ChangedRecord<'TId, 'TEntity>[]
    mutable Count: int
}

type internal StereoDbTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality>(tableName) =

    let _tableIndex = tableName |> DeterministicHash.strToHash |> DeterministicHash.mapToByte

    let _data = Dictionary<'TId, TableRecord<'TEntity>>()    
    let _indexes = ResizeArray<ISecondaryIndex<'TId, 'TEntity>>()
    
    let _recordsChanged = Dictionary<'TId, ChangedRecord<'TId, 'TEntity>>()
    let _recordsForCommit = { PoolData = Array.empty; Count = 0 }     
    let mutable _storage: StorageManager option = None

    let getIds () =        
        _data.Keys |> Seq.map id       
        
    let get id =
        match _data.TryGetValue id with
        | true, v ->
            match _storage with
            | Some store ->
                if v.IsEmpty then
                    store.Read(v.LogAddress).GetAwaiter().GetResult() |> ValueSome
                else
                    ValueSome v.Entity
                    
            | None -> ValueSome v.Entity
                    
        | _ -> ValueNone
           
    let set id entity =
        
        match _data.TryGetValue id with
        | true, oldRecord ->
            for index in _indexes do
                index.TryReIndex(id, oldRecord.Entity, entity)
            
            oldRecord.LogAddress <- 0
            oldRecord.Entity <- entity
            oldRecord.Version <- oldRecord.Version + 1uy
            _recordsChanged[id] <- { Id = id; Entity = entity; IsRemoved = false; Version = oldRecord.Version; LogAddress = 0 }
            _data[id] <- oldRecord
            
        | _ ->
            for index in _indexes do
                index.AddToIndex(id, entity)            
            
            _recordsChanged[id] <- { Id = id; Entity = entity; IsRemoved = false; Version = 0uy; LogAddress = 0 }
            _data[id] <- { Entity = entity; IsEmpty = false; LogAddress = 0; Version = 0uy }            
            
    let delete id =            
        match _data.TryGetValue id with
        | true, record ->                
            for index in _indexes do
                index.RemoveFromIndex(id, record.Entity)                
        
            _recordsChanged[id] <- { Id = id; Entity = Unchecked.defaultof<_>; IsRemoved = true; Version = 0uy; LogAddress = 0 }
        
        | _ -> ()        
        
        _data.Remove id            
            
    let addRangeScanIndex (getValue: Func<'TEntity, 'TValue>) =
        let index = RangeScanIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IRangeScanIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
                    
                member this.SelectRange(fromValue, toValue) =
                    let ids = index.SelectRangeIds(fromValue, toValue)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }
        
    let addValueIndex (getValue: Func<'TEntity, 'TValue>) =
        let index = ValueIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)            
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }
        
    let addMultiValueIndex (getValues: Func<'TEntity, 'TValue seq>) (unsafeReindexByObjRefCompare) =
        let index = MultiValueIndex<'TId, 'TEntity, 'TValue>(getValues.Invoke, unsafeReindexByObjRefCompare)            
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v.Entity
                            | _       -> ()
                    }
        }   
   
    interface ITable with
        member this.TableName = tableName
        member this.TableIndex = _tableIndex
        
    interface ITableControl with
        member this.InitStorage(storage) = _storage <- Some storage        
        
        member this.PrepareForCommit() =
            if _recordsChanged.Count > 0 && _recordsForCommit.Count = 0 then                
                _recordsForCommit.Count <- _recordsChanged.Count
                _recordsForCommit.PoolData <- ArrayPool.Shared.Rent _recordsChanged.Count
                
                let mutable i = 0
                for r in _recordsChanged do
                    _recordsForCommit.PoolData[i] <- r.Value
                    i <- i + 1
                
                _recordsChanged.Clear()            
        
        member this.SerializeToLog() =
            if _recordsForCommit.Count > 0 then
                
                for i = 0 to _recordsForCommit.Count - 1 do
                    let record = _recordsForCommit.PoolData[i]
                    
                    let logAddress = _storage.Value.Enqueue(_tableIndex, record.Id, record.Entity, record.IsRemoved)
                    record.LogAddress <- logAddress

        member this.Commit() =
            if _recordsForCommit.Count > 0 then
                
                for i = 0 to _recordsForCommit.Count - 1 do
                    let commited = _recordsForCommit.PoolData[i]
                    
                    match _data.TryGetValue commited.Id with
                    | true, tableRecord ->
                        if tableRecord.Version = commited.Version then
                            tableRecord.LogAddress <- commited.LogAddress
                            tableRecord.IsEmpty <- true
                            tableRecord.Entity <- Unchecked.defaultof<_>
                            
                    | false, _ -> ()
                    
                ArrayPool.Shared.Return _recordsForCommit.PoolData
                _recordsForCommit.PoolData <- Array.empty
                _recordsForCommit.Count <- 0                
        
    interface IConfigurationTable<'TId, 'TEntity> with        
        member this.AddRangeScanIndex(getValue) = addRangeScanIndex getValue            
        member this.AddValueIndex(getValue) = addValueIndex getValue
        member this.AddMultiValueIndex(getValue, unsafeReindexByObjRefCompare) = addMultiValueIndex getValue unsafeReindexByObjRefCompare        
        
    interface CSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = getIds()
        member this.TryGet(id, entity) =
            match get id with
            | ValueSome v ->
                entity <- v
                true
            
            | ValueNone -> false
    
    interface CSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = set id entity        
        member this.Delete(id) = delete id
        
    interface FSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = getIds()                          
        member this.Get(id) = get id        
        
    interface FSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = set id entity        
        member this.Delete(id) = delete id