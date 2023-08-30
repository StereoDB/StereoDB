module Tests.BasicReadWriteTests

open System
open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``Get and Set operations should work correctly`` () =
    let db = Db.Create()   
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    // create order
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)
        
        for id in books.GetIds() do
            voption {
                let! book = books.Get id
                if book.Quantity > 0 then
                    let order = { Id = Guid.NewGuid(); BookId = id; Quantity = 1 }
                    let updatedBook = { book with Quantity = book.Quantity - 1 }
                    
                    books.Set updatedBook
                    orders.Set order                    
            }
            |> ignore                     
    )
    
    // query book and orders
    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        let bookIdIndex = ctx.Schema.Orders.BookIdIndex
        
        voption {
            let! book = books.Get 1
            let orders = book.Id |> bookIdIndex.Find |> Seq.toArray
            
            return struct {| Book = book; Orders = orders |}
        }
    )
    
    let book = result.Value.Book
    let orders = result.Value.Orders
    
    test <@ book.Quantity = 0 @>
    test <@ orders[0].Quantity = 1 @>
    test <@ orders[0].BookId = book.Id @>
    
[<Fact>]
let ``GetIds should be supported`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )
    
    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        let ids = books.GetIds()        
        
        if Seq.isEmpty ids then ValueNone
        else ValueSome(ids |> Seq.toArray)
    )
    
    let ids = result.Value
    
    test <@ ids.Length = 10 @>