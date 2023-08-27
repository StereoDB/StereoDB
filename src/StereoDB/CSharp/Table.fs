namespace StereoDB.CSharp

open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open StereoDB

type IReadOnlyTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit ITable<'TId, 'TEntity>
    abstract TryGet: id:'TId * [<Out>]entity:'TEntity byref -> bool    
    
type IReadWriteTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId>> =
    inherit IReadOnlyTable<'TId, 'TEntity>    
    abstract Set: entity:'TEntity -> unit
    abstract Delete: id:'TId -> bool
    
type internal StereoDbTable<'TId, 'TEntity when 'TEntity :> IEntity<'TId> and 'TId: equality>() =
    
    let _data = Dictionary<'TId, 'TEntity>()    
    let _indexes = ResizeArray<ISecondaryIndex<'TId, 'TEntity>>()
    
    interface IReadOnlyTable<'TId, 'TEntity> with
                    
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]                    
        member this.TryGet(id, item) =
            match _data.TryGetValue id with
            | true, v ->
                item <- v
                true
                
            | _  -> false

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
            _data[entity.Id] <- entity
            
            for index in _indexes do
                index.ReIndex entity
        
        [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
        member this.Delete(id) =            
            for index in _indexes do
                index.RemoveFromIndex id
                
            _data.Remove id