namespace StereoDB.SecondaryIndex

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open StereoDB

type internal MultiValueIndex<'TId, 'TEntity, 'TValue when 'TValue : equality> 
    (getValues: 'TEntity -> 'TValue seq, unsafeReindexByObjRefCompare: bool) =
    
    let _valueIds = Dictionary<'TValue, HashSet<'TId>>()    
    
    let addNewValue id newValue =
        let mutable idsExist = false
        let ids = &CollectionsMarshal.GetValueRefOrAddDefault(_valueIds, newValue, &idsExist)
        if idsExist then
            ids.Add(id) |> ignore            
        else
            ids <- HashSet<'TId>()
            ids.Add(id) |> ignore
    
    let removeOldValue id oldValue =
        match _valueIds.TryGetValue oldValue with
        | true, ids -> ids.Remove(id) |> ignore
        | _         -> ()
    
    member this.AddToIndex(id, entity) =
        let values = getValues entity
        for v in values do
            addNewValue id v
        
    member inline this.AddToIndex(entityId, value) = addNewValue entityId value
    
    member this.TryReIndex(id, oldEntity, newEntity) =
        let oldValues = getValues oldEntity
        let newValues = getValues newEntity
        
        let shouldReindex =
            if unsafeReindexByObjRefCompare then
                 not (Object.ReferenceEquals(oldValues, newValues))                 
            else
                true
        
        if shouldReindex then
            for v in oldValues do
                removeOldValue id v
                
            for v in newValues do
                addNewValue id v
    
    member this.RemoveFromIndex(id, entity) =
        let values = getValues entity
        for v in values do            
            removeOldValue id v
    
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