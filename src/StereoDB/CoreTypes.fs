namespace StereoDB

open System
open System.Runtime.InteropServices

type IEntity<'TId> =
    abstract Id: 'TId

type ISecondaryIndex = interface end

type internal ISecondaryIndex<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ISecondaryIndex
    abstract AddToIndex: entity:'TEntity -> unit
    abstract TryReIndex: oldEntity:'TEntity * newEntity:'TEntity -> unit
    abstract RemoveFromIndex: entity:'TEntity -> unit

type IValueIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TEntity seq
    
type IRangeScanIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit IValueIndex<'TValue, 'TEntity>
    abstract SelectRange: fromValue:'TValue * toValue: 'TValue -> 'TEntity seq

type ITable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> = interface end    
    
type IConfigurationTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ITable<'TId, 'TEntity>
    abstract AddValueIndex: getValue:Func<'TEntity, 'TValue> -> IValueIndex<'TValue, 'TEntity>    
    abstract AddMultiValueIndex:
        getValues:Func<'TEntity, 'TValue seq> *
        [<Optional; DefaultParameterValue(false:bool)>] unsafeReindexByObjRefCompare:bool -> IValueIndex<'TValue, 'TEntity>        
    abstract AddRangeScanIndex: getValue:Func<'TEntity, 'TValue> -> IRangeScanIndex<'TValue, 'TEntity>

type ReadOnlyTsContext<'TSchema> = {
    Schema: 'TSchema
}

type ReadWriteTsContext<'TSchema> = {
    Schema: 'TSchema
}