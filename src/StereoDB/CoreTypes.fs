namespace StereoDB

open System

type IEntity<'TId> =
    abstract Id: 'TId

type ISecondaryIndex = interface end

type internal ISecondaryIndex<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ISecondaryIndex
    abstract ReIndex: entity:'TEntity -> unit
    abstract RemoveFromIndex: id:'TId -> unit

type IValueIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TEntity seq
    
type ITable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    abstract AddValueIndex: getValue:Func<'TEntity, 'TValue> -> IValueIndex<'TValue, 'TEntity>  