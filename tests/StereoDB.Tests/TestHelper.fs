module Tests.TestHelper

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
    Id: int
    BookId: int
    Quantity: int
    Categories: int[]
}
with
    interface IEntity<int> with
        member this.Id = this.Id

type Schema() =
    let _books = {| Table = StereoDb.createTable<int, Book>() |}
    
    let _ordersTable = StereoDb.createTable<int, Order>()
    let _orders = {|
        Table = _ordersTable
        BookIdIndex = _ordersTable.AddValueIndex(fun order -> order.BookId)
        QuantityIndex = _ordersTable.AddRangeScanIndex(fun order -> order.Quantity)
        CategoryIndex = _ordersTable.AddMultiValueIndex(fun order -> order.Categories)
    |}
    
    member this.Books = _books
    member this.Orders = _orders
    
type Order2 = {
    Id: int    
    Categories: Set<int>
}
with
    interface IEntity<int> with
        member this.Id = this.Id    
    
type Schema2() =    
    
    let _ordersTable = StereoDb.createTable<int, Order2>()
    let _orders = {|
        Table = _ordersTable        
        CategoryIndex = _ordersTable.AddMultiValueIndex((fun order -> order.Categories), unsafeReindexByObjRefCompare = true)
    |}    
    
    member this.Orders = _orders    

