namespace StereoDB.FSharp

open System.Collections.Generic
open System.Runtime.CompilerServices
open StereoDB

type IReadOnlyTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ITable<'TId, 'TEntity>
    abstract GetIds: unit -> 'TId seq
    abstract Get: id:'TId -> 'TEntity voption    
    
type IReadWriteTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit IReadOnlyTable<'TId, 'TEntity>    
    abstract Set: entity:'TEntity -> unit
    abstract Delete: id:'TId -> bool
    
type internal StereoDbTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId> and 'TId: equality>() =
    
    let _data = Dictionary<'TId, 'TEntity>()
    let _indexes = ResizeArray<ISecondaryIndex<'TId, 'TEntity>>()
    
    interface IReadOnlyTable<'TId, 'TEntity> with
                    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]            
        member this.GetIds() = _data.Keys |> Seq.map id
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]                    
        member this.Get(id) =
            match _data.TryGetValue id with
            | true, v -> ValueSome v
            | _       -> ValueNone

        member this.AddValueIndex(getValue) =
            
            let index = ValueIndex<'TId, 'TEntity, 'TValue>(getValue.Invoke)            
            _indexes.Add(index :> ISecondaryIndex<'TId, 'TEntity>)
            
            {
                new IValueIndex<'TValue, 'TEntity> with
                    member this.Find(value) =
                        let ids = (index :> IInternalValueIndex<'TId, 'TValue>).Find(value)
                        seq {
                            for id in ids do
                                match _data.TryGetValue id with
                                | true, v -> v
                                | _       -> ()
                        }
            }
            
    interface IReadWriteTable<'TId, 'TEntity> with
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.Set(entity) =
            match _data.TryGetValue entity.Id with
            | true, oldEntity ->
                for index in _indexes do
                    index.TryReIndex(oldEntity, entity)
                
            | _ ->
                for index in _indexes do
                    index.AddToIndex(entity)
                    
            _data[entity.Id] <- entity                    
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.Delete(id) =
            match _data.TryGetValue id with
            | true, entity ->                
                for index in _indexes do
                    index.RemoveFromIndex(entity)                
            
            | _ -> ()
            
            _data.Remove id