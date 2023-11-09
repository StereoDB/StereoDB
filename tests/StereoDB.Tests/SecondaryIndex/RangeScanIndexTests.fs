module Tests.RangeScanIndexTests

open System
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``RangeScanIndex SelectRange should work correctly`` () =    
    let db = StereoDb.create(Schema())
    
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
    let db = StereoDb.create(Schema())
    
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
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(0, 5) |> Seq.toArray
        
        test <@ data.Length = 2 @>
        test <@ data[0].Quantity = 1 @>
        test <@ data[1].Quantity = 3 @>
        
        // add item again 
        orders.Set order2
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        
        test <@ data.Length = 2 @>
        test <@ data[0].Quantity = 3 @>
        test <@ data[1].Quantity = 5 @>
        
        // add new item
        let order4 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 10 } 
        orders.Set order4
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 11) |> Seq.toArray
        
        test <@ data.Length = 3 @>
        test <@ data[0].Quantity = 3 @>
        test <@ data[1].Quantity = 5 @>
        test <@ data[2].Quantity = 10 @>
    )
    
[<Fact>]
let ``RangeScanIndex should support reindex`` () =    
    let db = StereoDb.create(Schema())
    
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
        
        // change quantity and update item
        let updatedOrder2 = { order2 with Quantity = 100 }
        orders.Set updatedOrder2
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 5) |> Seq.toArray
        test <@ data.Length = 1 @>
        
        let data = ctx.Schema.Orders.QuantityIndex.SelectRange(2, 100) |> Seq.toArray
        test <@ data.Length = 2 @>
        test <@ data[1].Quantity = 100 @>            
    )