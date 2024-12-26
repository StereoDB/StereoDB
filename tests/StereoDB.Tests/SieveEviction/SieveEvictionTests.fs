module Tests.SieveEvictionTests

open System
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open StereoDB.Infra.SieveEviction
open Tests.TestHelper

let internal isQueueCorrect keysCount head =
    let mutable currentHead = head
    let mutable count = 0
    
    while count <> keysCount do
        currentHead <- currentHead.Next
        count <- count + 1
        
    keysCount = count && currentHead.Key = head.Key

[<Fact>]
let ``Basic test check`` () =
    
    let cache = SieveEviction<int>(capacity = 2)
    let k1IsNone = cache.HitAndEvict(1).IsNone
    let k2IsNone = cache.HitAndEvict(2).IsNone
    
    let evk1 = cache.HitAndEvict(3).Value    
    let evk2 = cache.HitAndEvict(4).Value
    let queueIsCorrect = isQueueCorrect cache.Keys.Count cache.Head    
        
    test <@ k1IsNone @>
    test <@ k2IsNone @>
    test <@ evk1.Key = 1 @>
    test <@ evk2.Key = 2 @>
    test <@ cache.Head.Key = 4 @>
    test <@ cache.Tail.Key = 3 @>
    test <@ cache.HandPointer.Key = 4 @>
    test <@ queueIsCorrect @>
    
[<Fact>]
let ``HEAD and TAIL should compose after 2 keys in cache`` () =
    
    let cache = SieveEviction<int>(capacity = 5)
    cache.HitAndEvict(1) |> ignore
    let tailIsNone = cache.Tail |> Option.ofObj |> Option.isNone 
    
    cache.HitAndEvict(2) |> ignore
    let tailIsSome = cache.Tail |> Option.ofObj |> Option.isSome    
    
    let queueIsCorrect = isQueueCorrect cache.Keys.Count cache.Head
    
    test <@ tailIsNone @>
    test <@ tailIsSome @>
    test <@ cache.HandPointer.Key = 2 @>
    test <@ queueIsCorrect @>
    
[<Fact>]
let ``Check that eviction works correctly for Visited = true`` () =
    
    let cache = SieveEviction<int>(capacity = 2)
    cache.HitAndEvict(1) |> ignore
    cache.HitAndEvict(2) |> ignore
    cache.HitAndEvict(1) |> ignore // hit to prolong live
    let evicted = cache.HitAndEvict(3).Value    
    
    let queueIsCorrect = isQueueCorrect cache.Keys.Count cache.Head
    
    test <@ evicted.Key = 2 @>    
    test <@ cache.HandPointer.Key = 3 @>
    test <@ cache.Head.Key = 3 @>
    test <@ cache.Tail.Key = 1 @>
    test <@ queueIsCorrect @>    