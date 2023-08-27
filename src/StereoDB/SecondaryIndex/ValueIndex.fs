namespace StereoDB

open System
open System.Collections.Generic
    
type internal IInternalValueIndex<'TId, 'TValue> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TId seq
    
type internal ValueIndex<'TId, 'TEntity, 'TValue when 'TId : equality and 'TEntity :> IEntity<'TId> and 'TValue : equality and 'TValue :> IComparable<'TValue>> 
    (getValue: 'TEntity -> 'TValue) =
    
    let _valueIds = Dictionary<'TValue, HashSet<'TId>>()
    let _idValues = Dictionary<'TId, 'TValue>()
    
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
        member this.ReIndex(entity) =
            let id = entity.Id
            let value = getValue entity

            match _idValues.TryGetValue id with // check if ID already in index
            | true, oldValue ->
                if oldValue.CompareTo value <> 0 then // check if values are different and we should reindex
                    removeOldValue id oldValue
                    addNewValue id value
                    _idValues[id] <- value                
            
            | _ ->                             // indexing ID and Value                
                addNewValue id value
                _idValues[id] <- value            
        
        member this.RemoveFromIndex(id) =
            match _idValues.TryGetValue id with
            | true, value ->
                removeOldValue id value
                _idValues.Remove(id) |> ignore
            
            | _ -> ()
        
    interface IInternalValueIndex<'TId, 'TValue> with        
        member this.Find(value) =
            match _valueIds.TryGetValue value with
            | true, ids -> ids
            | _         -> Array.Empty<'TId>()