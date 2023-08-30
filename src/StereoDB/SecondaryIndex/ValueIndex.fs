namespace StereoDB

open System
open System.Collections.Generic
    
type internal IInternalValueIndex<'TId, 'TValue> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TId seq
    
type internal ValueIndex<'TId, 'TEntity, 'TValue when 'TId : equality and 'TEntity :> IEntity<'TId> and 'TValue : equality> 
    (getValue: 'TEntity -> 'TValue) =
    
    let _valueIds = Dictionary<'TValue, HashSet<'TId>>()    
    
    let addNewValue (id: 'TId) (newValue: 'TValue) =
        match _valueIds.TryGetValue newValue with
        | true, ids ->
            ids.Add(id) |> ignore
            
        | _ ->
            let ids = HashSet<'TId>()
            ids.Add(id) |> ignore
            _valueIds[newValue] <- ids
    
    let removeOldValue (id: 'TId) (oldValue: 'TValue) =
        match _valueIds.TryGetValue oldValue with
        | true, ids -> ids.Remove(id) |> ignore
        | _         -> ()
    
    interface ISecondaryIndex<'TId, 'TEntity> with
        member this.AddToIndex(entity) =
            let value = getValue entity
            addNewValue entity.Id value            
        
        member this.TryReIndex(oldEntity, newEntity) =
            let oldValue = getValue oldEntity
            let newValue = getValue newEntity
            
            if oldValue <> newValue then  // check if values are different and we should reindex
                removeOldValue oldEntity.Id oldValue
                addNewValue oldEntity.Id newValue           
        
        member this.RemoveFromIndex(entity) =
            let value = getValue entity
            removeOldValue entity.Id value
        
    interface IInternalValueIndex<'TId, 'TValue> with        
        member this.Find(value) =
            match _valueIds.TryGetValue value with
            | true, ids -> ids
            | _         -> Array.Empty<'TId>()