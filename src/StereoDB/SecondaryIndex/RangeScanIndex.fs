module internal StereoDB.SecondaryIndex.RangeScanIndex

open System
open System.Runtime.CompilerServices
open StereoDB
open StereoDB.Infra.SkipList
open StereoDB.SecondaryIndex.ValueIndex

type RangeScanIndex<'TId, 'TEntity, 'TValue when 'TId : equality and 'TEntity :> IEntity<'TId> and 'TValue : equality and 'TValue :> IComparable<'TValue>> 
    (getValue: 'TEntity -> 'TValue) =
    
    let _skipList = SkipList<'TValue>()
    let _valueIndex = ValueIndex<'TId, 'TEntity, 'TValue>(getValue)        
    
    let addToIndex (entity) =
        let value = getValue entity
        _skipList.Add(value)
        _valueIndex.AddToIndex(entity.Id, value)        
    
    let removeFromIndex (entity) =
        let value = getValue entity
        _skipList.Remove(value) |> ignore
        _valueIndex.RemoveFromIndex(entity.Id, value)        
    
    let tryReIndex (oldEntity) (newEntity) =
        let oldValue = getValue oldEntity
        let newValue = getValue newEntity
        
        if oldValue <> newValue then  // check if values are different and we should reindex
            removeFromIndex oldEntity
            addToIndex newEntity
    
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    member this.FindIds(value) = _valueIndex.FindIds(value)        
        
    member this.SelectRangeIds(fromValue, toValue) =
        seq {                
            let range = _skipList.SelectRange(fromValue, toValue)                
            for id in range do
                yield! _valueIndex.FindIds(id)            
        }    
    
    interface ISecondaryIndex<'TId, 'TEntity> with
    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.AddToIndex(entity) = addToIndex entity
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.RemoveFromIndex(entity) = removeFromIndex entity
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.TryReIndex(oldEntity, newEntity) = tryReIndex oldEntity newEntity