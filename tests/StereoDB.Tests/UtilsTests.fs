module Tests.UtilsTests

open Xunit
open Swensen.Unquote
open StereoDB.Infra.Utils

[<Fact>]
let ``strToHash should produce consistent hash results`` () =    
    
    let hash1 = "abc" |> DeterministicHash.strToHash
    let hash2 = "acb" |> DeterministicHash.strToHash
    let hash3 = "acb" |> DeterministicHash.strToHash
    let hash4 = "test str" |> DeterministicHash.strToHash
    let hash5 = "" |> DeterministicHash.strToHash
    
    test <@ hash1 <> hash2 @>
    test <@ hash2 = hash3 @>
    test <@ hash4 = 1386437358 @>
    test <@ hash5 = 23 @>