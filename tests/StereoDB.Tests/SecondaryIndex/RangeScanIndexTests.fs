module Tests.RangeScanIndexTests

open System
open Maybe.SkipList
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper
open StereoDB.Infra.SkipList

[<Fact>]
let ``RangeScanIndex SelectRange should work correctly`` () =    
    let db = Db.Create()
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order2 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 5 }
        let order3 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 3 }
      
        orders.Set order1
        orders.Set order2
        orders.Set order3
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        
        test <@ data.Length = 2 @>
        test <@ data[0].Quantity = 3 @>
        test <@ data[1].Quantity = 5 @>
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(5, 5) |> Seq.toArray
        test <@ data.Length = 1 @>
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(6, 7) |> Seq.toArray
        test <@ data.Length = 0 @>
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(1, 10) |> Seq.toArray
        test <@ data.Length = 3 @>
    )
    
[<Fact>]
let ``RangeScanIndex remove from index should work correctly`` () =
    
    let db = Db.Create()
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order2 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 5 }
        let order3 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 3 }
      
        orders.Set order1
        orders.Set order2
        orders.Set order3
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        
        test <@ data.Length = 2 @>
        test <@ data[0].Quantity = 3 @>
        test <@ data[1].Quantity = 5 @>
        
        // remove item
        orders.Delete(order2.Id) |> ignore        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        
        test <@ data.Length = 1 @>
        test <@ data[0].Quantity = 3 @>
        
        // add item again 
        orders.Set order2
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        
        test <@ data.Length = 2 @>
        test <@ data[0].Quantity = 3 @>
        test <@ data[1].Quantity = 5 @>        
    )
