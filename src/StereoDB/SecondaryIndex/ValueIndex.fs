namespace StereoDB.SecondaryIndex

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open StereoDB

type internal ValueIndex<'TId, 'TEntity, 'TValue when 'TValue : equality>
    (getValue: 'TEntity -> 'TValue) =
    
    let _valueIds = Dictionary<'TValue, HashSet<'TId>>()    
    
    let addNewValue id newValue =
        match _valueIds.TryGetValue newValue with
        | true, ids ->
            ids.Add(id) |> ignore
            
        | _ ->
            let ids = HashSet<'TId>()
            ids.Add(id) |> ignore
            _valueIds[newValue] <- ids
    
    let removeOldValue id oldValue =
        match _valueIds.TryGetValue oldValue with
        | true, ids -> ids.Remove(id) |> ignore
        | _         -> ()
    
    member this.AddToIndex(id, entity) =
        let value = getValue entity
        addNewValue id value
        
    member inline this.AddToIndex(id, value) = addNewValue id value
    
    member this.TryReIndex(id, oldEntity, newEntity) =
        let oldValue = getValue oldEntity
        let newValue = getValue newEntity
        
        if oldValue <> newValue then  // check if values are different and we should reindex
            removeOldValue id oldValue
            addNewValue id newValue
    
    member this.RemoveFromIndex(id, entity) =
        let value = getValue entity
        removeOldValue id value
        
    member inline this.RemoveFromIndex(entityId, value) = removeOldValue entityId value
    
    member this.FindIds(value): 'TId seq  =
        match _valueIds.TryGetValue value with
        | true, ids -> ids
        | _         -> Array.Empty<'TId>()
    
    interface ISecondaryIndex<'TId, 'TEntity> with
    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.AddToIndex(id, entity) = this.AddToIndex(id, entity)
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.TryReIndex(id, oldEntity, newEntity) = this.TryReIndex(id, oldEntity, newEntity)
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.RemoveFromIndex(id, entity) = this.RemoveFromIndex(id, entity)