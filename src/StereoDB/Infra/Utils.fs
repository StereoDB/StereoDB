module internal StereoDB.Infra.Utils

type Hash =
    
    /// Calculates deterministic hash
    static member calcDeterministicHash (input: string) =
        let mutable hash = 23 // Arbitrary prime number seed        
        for c in input do            
            hash <- (hash * 31) ^^^ (int c) // Multiply hash and XOR with character code
        hash &&& 0x7FFFFFFF // Return a positive 32-bit integer by masking the result