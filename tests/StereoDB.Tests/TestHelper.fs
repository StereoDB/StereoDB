module Tests.TestHelper

open StereoDB
open StereoDB.FSharp

type Book = {
    Id: int
    Title: string
    Quantity: int
}
        
type Order = {
    Id: int
    BookId: int
    Quantity: int
    Categories: int[]
}

type Schema() =
    let _books = {| Table = StereoDb.createTable<int, Book>("books") |}
    
    let _ordersTable = StereoDb.createTable<int, Order>("orders")
    let _orders = {|
        Table = _ordersTable
        BookIdIndex = _ordersTable.AddValueIndex(fun order -> order.BookId)
        QuantityIndex = _ordersTable.AddRangeScanIndex(fun order -> order.Quantity)
        CategoryIndex = _ordersTable.AddMultiValueIndex(fun order -> order.Categories)
    |}
    
    member this.Books = _books
    member this.Orders = _orders
    
    interface IDbSchema with
        member this.AllTables = [_books.Table; _orders.Table]
    
type Order2 = {
    Id: int    
    Categories: Set<int>
}    
    
type Schema2() =    
    
    let _ordersTable = StereoDb.createTable<int, Order2>("orders")
    let _orders = {|
        Table = _ordersTable        
        CategoryIndex = _ordersTable.AddMultiValueIndex((fun order -> order.Categories), unsafeReindexByObjRefCompare = true)
    |}    
    
    member this.Orders = _orders
    
    interface IDbSchema with
        member this.AllTables = [_ordersTable]

