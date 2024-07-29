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

type MutableBook = {
    Id: int
    Title: string
    ISBN: string
    mutable Quantity: int
}
with
    interface IEntity<int> with
        member this.Id = this.Id

type NullableBook = {
    Id: int
    Title: string
    OptionalValue: option<int>
    NullableValueType: Nullable<int>
    NullableRefType: string
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
    let _books = {| Table = StereoDb.createTable<int, Book>() |}
    let _mutableBooks = {| Table = StereoDb.createTable<int, MutableBook>() |}
    let _nullableBooks = {| Table = StereoDb.createTable<int, NullableBook>() |}
    
    let _ordersTable = StereoDb.createTable<Guid, Order>()
    let _orders = {|
        Table = _ordersTable
        BookIdIndex = _ordersTable.AddValueIndex(fun order -> order.BookId)
        QuantityIndex = _ordersTable.AddRangeScanIndex(fun order -> order.Quantity)
    |}
    
    member this.Books = _books
    member this.MutableBooks = _mutableBooks
    member this.NullableBooks = _nullableBooks
    member this.Orders = _orders

