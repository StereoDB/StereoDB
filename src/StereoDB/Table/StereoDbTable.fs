namespace StereoDB.Table

open System
open MessagePack
open StereoDB
open StereoDB.Storage

type internal StereoDbTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality>(tableName) =

    let _memData = TableOperations.createMemData<'TId, 'TEntity> tableName
    let _data = _memData.Data    
    
    let _changesPool = Changes.createPool()
    let _changeTracking = { Changes = Changes.rentDictForChanges _changesPool; IsEnabled = false } 
    let mutable _storage: StorageLog option = None
   
    let deserializeAndUpdateDb (logEntry: ReadOnlyMemory<byte>) =
        try        
            let mutable reader = MessagePackReader(logEntry)
            let header = MessagePackSerializer.Deserialize<RecordHeader<'TId>>(&reader)
            if header.IsRemoved then
                TableOperations.delete header.Id _changeTracking _memData |> ignore
            else
                let endPosition = reader.Position.GetInteger() - 1
                let payload = logEntry.Slice endPosition                
                let entity = MessagePackSerializer.Deserialize<'TEntity>(payload)                
                TableOperations.set header.Id entity _changeTracking _memData
        with
            ex -> ()
    
    let getChangesAndReset () =
        if _changeTracking.Changes.Count > 0 then
           let oldChanges = _changeTracking.Changes
           _changeTracking.Changes <- Changes.rentDictForChanges _changesPool
           oldChanges :> Collections.IDictionary
        else
           Changes.emptyDict
    
    interface ITable with
        member this.TableName = tableName
        member this.TableIndex = _memData.TableIndex
        
    interface ITableControl with
        member this.InitStorage(storage)              = _storage <- Some storage        
        member this.DeserializeAndUpdateDb(logEntry)  = deserializeAndUpdateDb logEntry
        member this.EnableChangeTracking()            = _changeTracking.IsEnabled <- true        
        member this.GetChangesAndReset()              = getChangesAndReset()
        member this.WriteToLog(tableChanges)          = TableOperations.writeToLog<'TId,'TEntity> _memData.TableIndex tableChanges _storage        
        member this.ReturnChangesToPool(tableChanges) = Changes.returnChangesToPool _changesPool tableChanges                                   
        
    interface IConfigurationTable<'TId, 'TEntity> with        
        member this.AddRangeScanIndex(getValue) = TableIndex.addRangeScanIndex _memData getValue
        member this.AddValueIndex(getValue)     = TableIndex.addValueIndex _memData getValue
        
        member this.AddMultiValueIndex(getValue, unsafeReindexByObjRefCompare) =
            TableIndex.addMultiValueIndex _memData getValue unsafeReindexByObjRefCompare        
        
    interface CSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = TableOperations.getIds _data
        
        member this.TryGet(id, entity) =
            match TableOperations.get id _data with
            | ValueSome v ->
                entity <- v
                true
            
            | ValueNone -> false
    
    interface CSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = TableOperations.set id entity _changeTracking _memData        
        member this.Delete(id) = TableOperations.delete id _changeTracking _memData
        
    interface FSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = TableOperations.getIds _data                          
        member this.Get(id) = TableOperations.get id _data        
        
    interface FSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = TableOperations.set id entity _changeTracking _memData         
        member this.Delete(id) = TableOperations.delete id _changeTracking _memData