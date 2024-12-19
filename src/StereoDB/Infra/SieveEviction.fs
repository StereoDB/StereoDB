module internal StereoDB.Infra.SieveEviction

open System
open System.Collections.Generic
open System.Runtime.InteropServices

[<ReferenceEquality>]
type Node<'K> = {
    Key: 'K
    mutable Next: Node<'K>
    mutable Visited: bool
}   

let rec getEvictionNode prev node =
    if node.Visited then
        node.Visited <- false
        getEvictionNode node node.Next
    else        
        struct (prev, node)

let inline getTail head =
    head.Next

let inline createEmptyKey key =
    { Key = key; Next = Unchecked.defaultof<_>; Visited = false }
    
type SieveEviction<'K when 'K: equality>(capacity) =
    
    let mutable _handPointer = Unchecked.defaultof<Node<'K>>
    let mutable _head = Unchecked.defaultof<Node<'K>>    
    let _cachedKeys = Dictionary<'K, Node<'K>>()
    
    let addAndEvict (newKey: Node<'K>) =
        let count = _cachedKeys.Count
        let mutable evKey = ValueNone
        
        if count > 2 then
            if count > capacity then
                let struct (prev, evicted) = getEvictionNode _handPointer _handPointer.Next
                prev.Next <- evicted.Next
                _handPointer <- prev
                evKey <- ValueSome evicted
                _cachedKeys.Remove(prev.Key) |> ignore
                
                if Object.ReferenceEquals(_head, evicted) then 
                    _head <- prev                       
                
            newKey.Next <- getTail _head  // update newKey to point to TAIL
            _head.Next <- newKey          // newKey is HEAD
        
        elif count = 2 then                       
            newKey.Next <- _head          // build HEAD to TAIL relation
            _head.Next <- newKey          // newKey is HEAD
        
        // update hand pointer to skip newRecord for next scan
        if Object.ReferenceEquals(_head, _handPointer) then 
            _handPointer <- newKey 
        
        _head <- newKey
        evKey
    
    member this.HandPointer = _handPointer
    member this.Tail = _head |> getTail
    member this.Head = _head
    member this.Keys = _cachedKeys :> IReadOnlyDictionary<_,_>
    
    member this.HitAndEvict(key: 'K) =
        let mutable keyExist = false
        let existedKey = &CollectionsMarshal.GetValueRefOrAddDefault(_cachedKeys, key, &keyExist)
        
        if keyExist then
            existedKey.Visited <- true
            ValueNone
        else            
            existedKey <- createEmptyKey key
            addAndEvict existedKey