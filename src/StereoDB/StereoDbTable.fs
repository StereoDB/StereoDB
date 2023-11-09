namespace StereoDB

open System
open System.Collections.Generic
open StereoDB
open StereoDB.SecondaryIndex

type internal StereoDbTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId> and 'TId: equality>() =
    
    let _data = Dictionary<'TId, 'TEntity>()    
    let _indexes = ResizeArray<ISecondaryIndex<'TId, 'TEntity>>()
        
    let getIds () =
        _data.Keys |> Seq.map id       
        
    let get (id) =
        match _data.TryGetValue id with
        | true, v -> ValueSome v
        | _       -> ValueNone
           
    let set (entity: 'TEntity) =            
        match _data.TryGetValue entity.Id with
        | true, oldEntity ->
            for index in _indexes do
                index.TryReIndex(oldEntity, entity)
            
        | _ ->
            for index in _indexes do
                index.AddToIndex(entity)
                
        _data[entity.Id] <- entity           
            
    let delete (id) =            
        match _data.TryGetValue id with
        | true, entity ->                
            for index in _indexes do
                index.RemoveFromIndex(entity)                
        
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
    
    interface ITable<'TId, 'TEntity> with
        member this.AddRangeScanIndex(getValue) = addRangeScanIndex getValue            
        member this.AddValueIndex(getValue) = addValueIndex getValue
        
    interface CSharp.IReadOnlyTable<'TId, 'TEntity> with
        member this.GetIds() = getIds()
        member this.TryGet(id, entity) =
            match _data.TryGetValue id with
            | true, v ->
                entity <- v
                true
                
            | _  -> false        
        
    interface FSharp.IReadOnlyTable<'TId, 'TEntity> with
        member this.GetIds() = getIds()                          
        member this.Get(id) = get id
    
    interface CSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(entity) = set entity        
        member this.Delete(id) = delete id
        
    interface FSharp.IReadWriteTable<'TId, 'TEntity> with        
        member this.Set(entity) = set entity        
        member this.Delete(id) = delete id        