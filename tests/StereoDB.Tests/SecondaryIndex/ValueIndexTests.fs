module Tests.ValueIndexTests

open System
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``Find should work correctly`` () =    
    let db = StereoDb.create(Schema())
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order2 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order3 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 1 }
      
        orders.Set order1
        orders.Set order2
        orders.Set order3
    )
    
    db.ReadTransaction(fun ctx ->
        
        let book1 = ctx.Schema.Orders.BookIdIndex.Find(1)  |> Seq.toArray
        let zeroBooks = ctx.Schema.Orders.BookIdIndex.Find(2) |> Seq.toArray
        let book3 = ctx.Schema.Orders.BookIdIndex.Find(3)   |> Seq.toArray
        
        test <@ book1.Length = 2 @>
        test <@ book1[0].BookId = book1[1].BookId @>
        
        test <@ zeroBooks.Length = 0 @>
        test <@ book3.Length = 1 @>
        
        ValueNone
    )
    |> ignore
    
[<Fact>]
let ``ValueIndex should handle deletion`` () =    
    let db = StereoDb.create(Schema())
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order2 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order3 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 1 }
      
        orders.Set order1
        orders.Set order2
        orders.Set order3
        
        let book1 = ctx.Schema.Orders.BookIdIndex.Find(1) |> Seq.toArray
        let book3 = ctx.Schema.Orders.BookIdIndex.Find(3) |> Seq.toArray
        
        test <@ book1.Length = 2 @>
        test <@ book3.Length = 1 @>
        
        orders.Delete(order1.Id) |> ignore
        
        let book1 = ctx.Schema.Orders.BookIdIndex.Find(1) |> Seq.toArray
        let book3 = ctx.Schema.Orders.BookIdIndex.Find(3) |> Seq.toArray
        
        test <@ book1.Length = 1 @>
        test <@ book3.Length = 1 @>
    )
    
[<Fact>]
let ``ValueIndex should handle reindexing`` () =    
    let db = StereoDb.create(Schema())
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order2 = { Id = Guid.NewGuid(); BookId = 1; Quantity = 1 }
        let order3 = { Id = Guid.NewGuid(); BookId = 3; Quantity = 1 }
      
        orders.Set order1
        orders.Set order2
        orders.Set order3
        
        let book1 = ctx.Schema.Orders.BookIdIndex.Find(1) |> Seq.toArray
        let book3 = ctx.Schema.Orders.BookIdIndex.Find(3) |> Seq.toArray
        
        test <@ book1.Length = 2 @>
        test <@ book3.Length = 1 @>
        
        let order2 = { order2 with BookId = 50 } 
        orders.Set order2
        
        let book1 = ctx.Schema.Orders.BookIdIndex.Find(1) |> Seq.toArray
        let book3 = ctx.Schema.Orders.BookIdIndex.Find(3) |> Seq.toArray
        let book50 = ctx.Schema.Orders.BookIdIndex.Find(50) |> Seq.toArray
        
        test <@ book1.Length = 1 @>
        test <@ book3.Length = 1 @>
        test <@ book50.Length = 1 @>
    )