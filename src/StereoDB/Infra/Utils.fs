module internal StereoDB.Infra.Utils

open System.Collections.Generic

module DeterministicHash =
    
    /// Calculates deterministic hash
    let strToHash (input: string) =
        let mutable hash = 23 // Arbitrary prime number seed        
        for c in input do            
            hash <- (hash * 31) ^^^ (int c) // Multiply hash and XOR with character code
        hash &&& 0x7FFFFFFF // Return a positive 32-bit integer by masking the result
        
    let inline mapToByte (hash: int) =
        byte (hash % 256)
        
module Array =
    
    let copyDictToArray (dict: Dictionary<_,'T>) (array: 'T[]) =
        let mutable index = 0
        for ch in dict do
            array[index] <- ch.Value
            index <- index + 1