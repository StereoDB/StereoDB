module internal StereoDB.Infra.SkipList

open System

[<Literal>]
let MAX_ALLOWED_LEVEL = 32

[<AllowNullLiteral>]
type Node<'TValue when 'TValue :> IComparable<'TValue>>(value: 'TValue, next: Node<'TValue>[]) =    
    member this.Value = value
    member this.Next = next

type SkipList<'TValue when 'TValue :> IComparable<'TValue>>() =
    
    let _random = Random()    
    let _head = Node(Unchecked.defaultof<_>, Array.zeroCreate MAX_ALLOWED_LEVEL)
    let mutable _maxLevel = 0
    
    let getRandomLevel () =
        let mutable level = 0
        
        while _random.NextDouble() > 0.5 && level <= MAX_ALLOWED_LEVEL do
            level <- level + 1
            
        if level > _maxLevel then
            _maxLevel <- level
            
        level
        
    member this.Add(value) =
        let randomLevel = getRandomLevel()
        let newNode = Node(value, Array.zeroCreate(randomLevel + 1))
                
        // start from the top-level node (head) and move down through levels to find insertion points.
        let mutable current = _head
        
        for level = _maxLevel downto 0 do
            
            // move right until value < next.Value
            while current.Next[level] <> null && current.Next[level].Value.CompareTo(value) < 0 do
                current <- current.Next[level]
            
            // update nodes addresses
            if level <= randomLevel then            
                newNode.Next[level] <- current.Next[level]
                current.Next[level] <- newNode
                
    member this.Contains(value) =
        
        let rec contains (head: Node<'TValue>) (value: 'TValue) (level: int) =            
            let mutable current = head
                        
            while current.Next[level] <> null && current.Next[level].Value.CompareTo(value) < 0 do
                current <- current.Next[level]
                    
            // if the next node at this level contains the value, return true.                                
            let result = current.Next[level] <> null && current.Next[level].Value.CompareTo(value) = 0
            
            if result then result
            elif level > 0 then contains current value (level - 1)
            else false
            
        contains _head value _maxLevel
        
    member this.SelectRange(fromValue: 'TValue, toValue: 'TValue) =
        seq {
            let mutable current = _head
            
            for level = _maxLevel downto 0 do                        

                // move right until value < next.Value                
                while current.Next[level] <> null && current.Next[level].Value.CompareTo(fromValue) <= 0 do
                    current <- current.Next[level]
                    
                if level = 0 then
                    if current.Value.CompareTo(fromValue) = 0 then
                        yield current.Value
                    
                    while current.Next[level] <> null && current.Next[level].Value.CompareTo(toValue) <= 0 do
                        current <- current.Next[level]
                        yield current.Value
        }
        
    member this.Remove(value) =
        let mutable removed = false
        let mutable current = _head
        
        for level = _maxLevel downto 0 do
            
            // move right until value < next.Value
            while current.Next[level] <> null && current.Next[level].Value.CompareTo(value) < 0 do
                current <- current.Next[level]
                
            // if the next node at this level contains the value, remove the current node and connect left node to right node.
            let result = current.Next[level] <> null && current.Next[level].Value.CompareTo(value) = 0
            if result then
                removed <- true
                current.Next[level] <- current.Next[level].Next[level]                
                
        removed                