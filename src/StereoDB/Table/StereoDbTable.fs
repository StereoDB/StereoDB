namespace StereoDB.Table

open System
open Serilog
open StereoDB
open StereoDB.Table.Domain

type internal StereoDbTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality>(tableName) =
     
    let _changesPool = ChangesDictPool.createPool()
    let _memData = TableOperations.createMemoryData tableName _changesPool
    let _tableId = _memData.TableId

    let mutable _logger = Unchecked.defaultof<ILogger>    
    let mutable _storageLog = None
    let mutable _entityAddressStore = None
    let mutable _entitySerializer = None
    
    let getChangesAndReset () =
        if _memData.ChangeTracking.Changes.Count > 0 then
           let oldChanges = _memData.ChangeTracking.Changes
           _memData.ChangeTracking.Changes <- ChangesDictPool.rentDict _changesPool
           oldChanges :> Collections.IDictionary
        else
           ChangesDictPool.emptyDict
    
    interface ITable with
        member this.TableName = tableName
        member this.TableId = _tableId
        
    interface ITableControl with
        member this.Init(logger) = _logger <- logger
        
        member this.SetStorage(storageLog, entityAddressStore, serializer) =
            _storageLog <- Some storageLog
            _entityAddressStore <- Some entityAddressStore
            _entitySerializer <- Some serializer
            
        member this.UpdateEntity(logEntry) = TableOperations.updateEntity _memData logEntry _entitySerializer.Value        
        member this.GetEntityAddress(logEntry, logAddress) = TableOperations.getEntityAddress _tableId logEntry logAddress
        
        member this.EnableChangeTracking()            = _memData.ChangeTracking.IsEnabled <- true        
        member this.GetChangesAndReset()              = getChangesAndReset()
        member this.WriteToLog(tableChanges)          = TableOperations.writeToLog<'TId,'TEntity> _tableId tableChanges _storageLog        
        member this.ReturnChangesToPool(tableChanges) = ChangesDictPool.returnDictToPool _changesPool tableChanges                                                   
        
    interface IConfigurationTable<'TId, 'TEntity> with        
        member this.AddRangeScanIndex(getValue) = SecondaryIndex.addRangeScanIndex _memData getValue
        member this.AddValueIndex(getValue)     = SecondaryIndex.addValueIndex _memData getValue
        
        member this.AddMultiValueIndex(getValue, unsafeReindexByObjRefCompare) =
            SecondaryIndex.addMultiValueIndex _memData getValue unsafeReindexByObjRefCompare        
        
    interface CSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = TableOperations.getIds _memData.Data
        
        member this.TryGet(id, entity) =
            match TableOperations.get id _memData.Data with
            | ValueSome v ->
                entity <- v
                true
            
            | ValueNone -> false
    
    interface CSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = TableOperations.set id entity _memData        
        member this.Delete(id) = TableOperations.delete id _memData
        
    interface FSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = TableOperations.getIds _memData.Data                          
        member this.Get(id) = TableOperations.get id _memData.Data
        
    interface FSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = TableOperations.set id entity _memData         
        member this.Delete(id) = TableOperations.delete id _memData