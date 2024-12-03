module internal StereoDB.Infra.Utils

open System
open System.Collections.Generic
open MessagePack
open MessagePack.Resolvers

let inline disposeAsync instance =
    (instance :> IAsyncDisposable).DisposeAsync()

module DeterministicHash =
    
    /// Calculates deterministic hash
    let strToHash (input: string) =
        let mutable hash = 23 // Arbitrary prime number seed        
        for c in input do            
            hash <- (hash * 31) ^^^ (int c) // Multiply hash and XOR with character code
                    
        hash &&& 0x7FFFFFFF // Ensure a positive 32-bit integer by masking the result        
        
    let inline mapToByte (hash: int) =
        byte (hash % 256)
            
    let inline castToNotReservedBytes (value: byte) =
        if value >= 0uy && value <= 5uy then 
            value + 6uy // Shift reserved values to 6 or above
        else 
            value     
        
module Array =
    
    let copyDictToArray (dict: Dictionary<_,'T>) (array: 'T[]) =
        let mutable index = 0
        for ch in dict do
            array[index] <- ch.Value
            index <- index + 1
            
module Seq =
    
    let tryHeadV (source: seq<_>) =        
        use e = source.GetEnumerator()

        if e.MoveNext() then
            ValueSome e.Current
        else
            ValueNone
            
module MessagePack =
    
    let defaultOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)