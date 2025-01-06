namespace StereoDB

open System
open System.Collections.Generic
open StereoDB
open StereoDB.Infra.Utils
open StereoDB.SecondaryIndex

type internal StereoDbTable<'TId, 'TEntity when 'TId: equality and 'TEntity: equality>(tableName) =
    
    let _tableIndex = Hash.calcDeterministicHash tableName
    let _data = Dictionary<'TId, 'TEntity>()
    let _indexes = ResizeArray<ISecondaryIndex<'TId, 'TEntity>>()

    let getIds () =
        _data.Keys |> Seq.map id
        
    let getAll () =
        _data.Values |> Seq.map id        
        
    let get id =
        match _data.TryGetValue id with
        | true, v -> ValueSome v
        | _       -> ValueNone
           
    let set id entity =            
        match _data.TryGetValue id with
        | true, oldEntity ->
            for index in _indexes do
                index.TryReIndex(id, oldEntity, entity)
            
        | _ ->
            for index in _indexes do
                index.AddToIndex(id, entity)
                
        _data[id] <- entity           
            
    let delete id =            
        match _data.TryGetValue id with
        | true, entity ->                
            for index in _indexes do
                index.RemoveFromIndex(id, entity)                
        
        | _ -> ()
        
        _data.Remove id            
            
    let addRangeScanIndex (getValue: Func<'TEntity, 'TValue>) =
        let index = RangeScanIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IRangeScanIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v
                            | _       -> ()
                    }
                    
                member this.SelectRange(fromValue, toValue) =
                    let ids = index.SelectRangeIds(fromValue, toValue)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v
                            | _       -> ()
                    }
        }
        
    let addValueIndex (getValue: Func<'TEntity, 'TValue>) =
        let index = ValueIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)            
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v
                            | _       -> ()
                    }
        }
        
    let addMultiValueIndex (getValues: Func<'TEntity, 'TValue seq>) (unsafeReindexByObjRefCompare) =
        let index = MultiValueIndex<'TId, 'TEntity, 'TValue>(getValues.Invoke, unsafeReindexByObjRefCompare)            
        _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
        
        {
            new IValueIndex<'TValue, 'TEntity> with
                member this.Find(value) =
                    let ids = index.FindIds(value)
                    seq {
                        for id in ids do
                            match _data.TryGetValue id with
                            | true, v -> v
                            | _       -> ()
                    }
        }
    
    interface IConfigurationTable<'TId, 'TEntity> with        
        member this.AddRangeScanIndex(getValue) = addRangeScanIndex getValue            
        member this.AddValueIndex(getValue) = addValueIndex getValue
        member this.AddMultiValueIndex(getValue, unsafeReindexByObjRefCompare) = addMultiValueIndex getValue unsafeReindexByObjRefCompare 
        
    interface ITable with
        member this.TableName = tableName
        member this.TableIndex = _tableIndex
        
    interface CSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = getIds()
        member this.GetAll() = getAll()
        member this.TryGet(id, entity) =
            match _data.TryGetValue id with
            | true, v ->
                entity <- v
                true
                
            | _  -> false
    
    interface CSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = set id entity        
        member this.Delete(id) = delete id
        
    interface FSharp.IReadOnlyTable<'TId, 'TEntity> with        
        member this.GetIds() = getIds()
        member this.GetAll() = getAll()
        member this.Get(id) = get id        
        
    interface FSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(id, entity) = set id entity        
        member this.Delete(id) = delete id