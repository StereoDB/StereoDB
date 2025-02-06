namespace StereoDB.SecondaryIndex

open System
open System.Runtime.CompilerServices
open StereoDB
open StereoDB.Infra.SkipList

type internal RangeScanIndex<'TId, 'TEntity, 'TValue when 'TValue : equality and 'TValue :> IComparable<'TValue>> 
    (getValue: 'TEntity -> 'TValue) =
    
    let _skipList = SkipList<'TValue>()
    let _valueIndex = ValueIndex<'TId, 'TEntity, 'TValue>(getValue)        
    
    let addToIndex id entity =
        let value = getValue entity
        if typeof<'TValue>.IsClass && isNull(value :> obj) then
            ()
        else            
            _skipList.Add(value)
            _valueIndex.AddToIndex(id, value)        
    
    let removeFromIndex id entity =
        let value = getValue entity
        if typeof<'TValue>.IsClass && isNull(value :> obj) then
            ()
        else            
            _skipList.Remove(value) |> ignore
            _valueIndex.RemoveFromIndex(id, value)        
    
    let tryReIndex id oldEntity newEntity =
        let oldValue = getValue oldEntity
        let newValue = getValue newEntity
        
        if oldValue <> newValue then  // check if values are different and we should reindex
            removeFromIndex id oldEntity
            addToIndex id newEntity
        
    member inline this.FindIds(value) = _valueIndex.FindIds(value)        
        
    member this.SelectRangeIds(fromValue, toValue) =
        seq {                
            let range = _skipList.SelectRange(fromValue, toValue)                
            for id in range do
                yield! _valueIndex.FindIds(id)            
        }    
    
    interface ISecondaryIndex<'TId, 'TEntity> with
    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]        
        member this.AddToIndex(id, entity) = addToIndex id entity
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.TryReIndex(id, oldEntity, newEntity) = tryReIndex id oldEntity newEntity
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.RemoveFromIndex(id, entity) = removeFromIndex id entity