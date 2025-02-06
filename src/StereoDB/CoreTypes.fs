namespace StereoDB

open System
open System.Runtime.InteropServices

type ISecondaryIndex = interface end

type internal ISecondaryIndex<'TId, 'TEntity> =
    inherit ISecondaryIndex
    abstract AddToIndex: id:'TId * entity:'TEntity -> unit
    abstract TryReIndex: id:'TId * oldEntity:'TEntity * newEntity:'TEntity -> unit
    abstract RemoveFromIndex: id:'TId * entity:'TEntity -> unit

type IValueIndex<'TValue,'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TEntity seq
    
type IValueIndexIds<'TValue,'TEntity,'TId when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TEntity seq
    abstract FindIds: value:'TValue -> 'TId seq
    
type IRangeScanIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract SelectRange: fromValue:'TValue * toValue: 'TValue -> 'TEntity seq
    
type IRangeScanIndexIds<'TValue,'TEntity,'TId when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract SelectRange: fromValue:'TValue * toValue: 'TValue -> 'TEntity seq
    abstract SelectRangeIds: fromValue:'TValue * toValue: 'TValue -> 'TId seq

type ITable =
    abstract TableName: string
    abstract TableIndex: int

type ITable<'TId, 'TEntity> =
    inherit ITable
    
type IConfigurationTable<'TId, 'TEntity> =    
    inherit ITable<'TId, 'TEntity>
    abstract AddValueIndex: getValue:Func<'TEntity,'TValue> -> IValueIndex<'TValue,'TEntity>
    abstract AddValueIndexIds: getValue:Func<'TEntity,'TValue> -> IValueIndexIds<'TValue,'TEntity,'TId>
    abstract AddMultiValueIndex:
        getValues:Func<'TEntity, 'TValue seq> *
        [<Optional; DefaultParameterValue(false:bool)>] unsafeReindexByObjRefCompare:bool -> IValueIndex<'TValue, 'TEntity>        
    abstract AddRangeScanIndex: getValue:Func<'TEntity, 'TValue> -> IRangeScanIndex<'TValue, 'TEntity>
    abstract AddRangeScanIndexIds: getValue:Func<'TEntity, 'TValue> -> IRangeScanIndexIds<'TValue,'TEntity,'TId>

type IDbSchema =
    abstract AllTables: ITable seq 

type ReadOnlyTsContext<'TSchema> = {
    Schema: 'TSchema
}

type ReadWriteTsContext<'TSchema> = {
    Schema: 'TSchema
}