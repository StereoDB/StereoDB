module Tests.TestHelper

open System
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
