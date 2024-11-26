namespace StereoDB

open System
open System.Collections
open System.Runtime.InteropServices
open StereoDB.Storage

type ISecondaryIndex = interface end

type internal ISecondaryIndex<'TId, 'TEntity> =
    inherit ISecondaryIndex
    abstract AddToIndex: id:'TId * entity:'TEntity -> unit
    abstract TryReIndex: id:'TId * oldEntity:'TEntity * newEntity:'TEntity -> unit
    abstract RemoveFromIndex: id:'TId * entity:'TEntity -> unit

type IValueIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit ISecondaryIndex
    abstract Find: value:'TValue -> 'TEntity seq
    
type IRangeScanIndex<'TValue, 'TEntity when 'TValue : equality and 'TValue :> IComparable<'TValue>> =
    inherit IValueIndex<'TValue, 'TEntity>
    abstract SelectRange: fromValue:'TValue * toValue: 'TValue -> 'TEntity seq

type ITable =
    abstract TableName: string
    abstract TableIndex: byte

type ITable<'TId, 'TEntity> =
    inherit ITable

type internal ITableControl =
    abstract InitStorage:            StorageLog -> unit
    abstract DeserializeAndUpdateDb: logEntry:ReadOnlyMemory<byte> -> unit 
    abstract GetChangesAndReset:     unit -> IDictionary
    abstract WriteToLog:             tableChanges:IDictionary -> unit
    abstract ReturnChangesToPool:    tableChanges:IDictionary -> unit
    
type IConfigurationTable<'TId, 'TEntity> =    
    inherit ITable<'TId, 'TEntity>    
    abstract AddValueIndex: getValue:Func<'TEntity, 'TValue> -> IValueIndex<'TValue, 'TEntity>    
    
    abstract AddMultiValueIndex:
        getValues:Func<'TEntity, 'TValue seq> *
        [<Optional; DefaultParameterValue(false:bool)>] unsafeReindexByObjRefCompare:bool -> IValueIndex<'TValue, 'TEntity>        
    
    abstract AddRangeScanIndex: getValue:Func<'TEntity, 'TValue> -> IRangeScanIndex<'TValue, 'TEntity>

type IDbSchema =
    abstract AllTables: ITable seq 

type ReadOnlyTsContext<'TSchema> = {
    Schema: 'TSchema
}

type ReadWriteTsContext<'TSchema> = {
    Schema: 'TSchema
}