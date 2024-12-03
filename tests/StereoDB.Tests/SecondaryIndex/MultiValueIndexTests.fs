module Tests.MultiValueIndexTests

open System
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``Find should work correctly`` () = task {    
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = 1; BookId = 1; Quantity = 1; Categories = [| 2; 4; 5 |] }
        let order2 = { Id = 2; BookId = 1; Quantity = 1; Categories = [| 2; 1; 10 |] }
        let order3 = { Id = 3; BookId = 3; Quantity = 1; Categories = [| 3 |] }
      
        orders.Set(order1.Id, order1)
        orders.Set(order2.Id, order2)
        orders.Set(order3.Id, order3)
    )
    
    db.ReadTransaction(fun ctx ->
        
        let category1 = ctx.Schema.Orders.CategoryIndex.Find(1) |> Seq.toArray
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category3 = ctx.Schema.Orders.CategoryIndex.Find(3) |> Seq.toArray
        let category10 = ctx.Schema.Orders.CategoryIndex.Find(10) |> Seq.toArray
        
        test <@ category1.Length = 1 @>
        test <@ category2.Length = 2 @>
        test <@ category3.Length = 1 @>
        test <@ category10.Length = 1 @>
        test <@ category1[0] = category10[0] @>
        
        ValueNone
    )
    |> ignore
}
    
[<Fact>]
let ``MultiValueIndex should handle deletion`` () = task {   
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = 1; BookId = 1; Quantity = 1; Categories = [| 2 |] }
        let order2 = { Id = 2; BookId = 1; Quantity = 1; Categories = [| 2 |] }
        let order3 = { Id = 3; BookId = 3; Quantity = 1; Categories = [| 3 |] }
      
        orders.Set(order1.Id, order1)
        orders.Set(order2.Id, order2)
        orders.Set(order3.Id, order3)
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category3 = ctx.Schema.Orders.CategoryIndex.Find(3) |> Seq.toArray
        
        test <@ category2.Length = 2 @>
        test <@ category3.Length = 1 @>
        
        orders.Delete(order3.Id) |> ignore
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category3 = ctx.Schema.Orders.CategoryIndex.Find(3) |> Seq.toArray
        
        test <@ category2.Length = 2 @>
        test <@ category3.Length = 0 @>
    )
}
    
[<Fact>]
let ``MultiValueIndex should handle reindexing`` () = task {    
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let order1 = { Id = 1; BookId = 1; Quantity = 1; Categories = [| 2 |] }
        let order2 = { Id = 2; BookId = 1; Quantity = 1; Categories = [| 2 |] }
        let order3 = { Id = 3; BookId = 3; Quantity = 1; Categories = [| 3 |] }
      
        orders.Set(order1.Id, order1)
        orders.Set(order2.Id, order2)
        orders.Set(order3.Id, order3)
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category3 = ctx.Schema.Orders.CategoryIndex.Find(3) |> Seq.toArray
        
        test <@ category2.Length = 2 @>
        test <@ category3.Length = 1 @>
        
        let order3 = { order3 with Categories = [| 2; 4 |] } 
        orders.Set(order3.Id, order3)
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category3 = ctx.Schema.Orders.CategoryIndex.Find(3) |> Seq.toArray
        let category4 = ctx.Schema.Orders.CategoryIndex.Find(4) |> Seq.toArray
        
        test <@ category2.Length = 3 @>
        test <@ category3.Length = 0 @>
        test <@ category4.Length = 1 @>
    )
}
    
[<Fact>]
let ``MultiValueIndex should support reindexing by hash comparison`` () = task {    
    use! db = StereoDb.init(Schema2(), StereoDbSettings.OnlyInMemory)
    
    db.WriteTransaction(fun ctx ->
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
                
        let categories = [| 2 |] |> Set.ofArray                
        let order1 = { Id = 1; Categories = categories }
        let order2 = { Id = 2; Categories = categories }
        let order3 = { Id = 3; Categories = categories }
      
        orders.Set(order1.Id, order1)
        orders.Set(order2.Id, order2)
        orders.Set(order3.Id, order3)
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category5 = ctx.Schema.Orders.CategoryIndex.Find(5) |> Seq.toArray
        
        test <@ category2.Length = 3 @>
        test <@ category5.Length = 0 @>
        
        let order3 = { order3 with Categories = order3.Categories |> Set.add 5 |> Set.remove 2 } 
        orders.Set(order3.Id, order3)
        
        let category2 = ctx.Schema.Orders.CategoryIndex.Find(2) |> Seq.toArray
        let category5 = ctx.Schema.Orders.CategoryIndex.Find(5) |> Seq.toArray        
        
        test <@ category2.Length = 2 @>
        test <@ category5.Length = 1 @>        
    )
}             