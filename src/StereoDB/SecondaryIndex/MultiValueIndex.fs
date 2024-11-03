namespace StereoDB.SecondaryIndex

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open StereoDB
    
type internal MultiValueIndex<'TId, 'TEntity, 'TValue when 'TId : equality and 'TEntity :> IEntity<'TId> and 'TValue : equality> 
    (getValues: 'TEntity -> 'TValue seq, unsafeReindexByObjRefCompare: bool) =
    
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
    
    member this.AddToIndex(entity) =
        let values = getValues entity
        for v in values do
            addNewValue entity.Id v
        
    member inline this.AddToIndex(entityId, value) = addNewValue entityId value
    
    member this.TryReIndex(oldEntity, newEntity) =
        let oldValues = getValues oldEntity
        let newValues = getValues newEntity
        
        let shouldReindex =
            if unsafeReindexByObjRefCompare then
                 not (Object.ReferenceEquals(oldValues, newValues))                 
            else
                true
        
        if shouldReindex then
            for v in oldValues do
                removeOldValue oldEntity.Id v
                
            for v in newValues do
                addNewValue oldEntity.Id v
    
    member this.RemoveFromIndex(entity) =
        let values = getValues entity
        for v in values do            
            removeOldValue entity.Id v
    
    member this.FindIds(value): 'TId seq  =
        match _valueIds.TryGetValue value with
        | true, ids -> ids
        | _         -> Array.Empty<'TId>()
    
    interface ISecondaryIndex<'TId, 'TEntity> with
    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.AddToIndex(entity) = this.AddToIndex(entity)
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.TryReIndex(oldEntity, newEntity) = this.TryReIndex(oldEntity, newEntity)
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.RemoveFromIndex(entity) = this.RemoveFromIndex(entity)