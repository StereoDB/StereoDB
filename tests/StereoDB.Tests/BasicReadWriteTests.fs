module Tests.BasicReadWriteTests

open System
open NUnit.Framework
open Swensen.Unquote
open FsToolkit.ErrorHandling
open StereoDB
open StereoDB.FSharp

type Book = {
    Id: int
    Title: string
    Quantity: int    
}
with
    interface IEntity<int> with
        member this.Id = this.Id
        
type Order = {
    Id: Guid
    BookId: int
    Quantity: int    
}
with
    interface IEntity<Guid> with
        member this.Id = this.Id

type Schema() =
    let _books = {| Table = StereoDbEngine.CreateTable<int, Book>() |}
    
    let _ordersTable = StereoDbEngine.CreateTable<Guid, Order>()
    let _orders = {| Table = _ordersTable; BookIdIndex = _ordersTable.AddValueIndex(fun order -> order.BookId) |}
    
    member this.Books = _books
    member this.Orders = _orders

type Db() =    
    let _engine = StereoDbEngine(Schema())
    
    interface IStereoDb<Schema> with
        member this.ReadTransaction(transaction) = _engine.ReadTransaction transaction
        member this.WriteTransaction(transaction) = _engine.WriteTransaction transaction
        member this.WriteTransaction<'T>(transaction) = _engine.WriteTransaction<'T>(transaction)
        
    static member Create() = Db() :> IStereoDb<Schema>
             
// [<SetUp>]
// let Setup () =    
//     ()

[<Test>]
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
        
        for id in [1..10] do
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