<p align="center">
  <img src="https://github.com/StereoDB/StereoDB/blob/dev/assets/stereo_db_logo.png" alt="StereoDB logo" width="600px">
</p>

[![build](https://github.com/StereoDB/StereoDB/actions/workflows/build.yml/badge.svg)](https://github.com/StereoDB/StereoDB/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/nbomber.svg)](https://www.nuget.org/packages/nbomber/)

#### StereoDB
Ultrafast and lightweight in-process memory database written in F# that supports: transactions, secondary indexes, persistence, and data size larger than RAM. The primary use case for this database is building Stateful Services (API or ETL Worker) that keep all data in memory and can provide millions of RPS from a single node. 

Supported features:
- [x] C# and F# API
- [x] Transactions (read-only, read-write)
- [x] Secondary Indexes
  - [x] Value Index (hash-based index)
  - [ ] Range Index
- [ ] Data size larger than RAM
- [ ] Data persistence

#### Benchmarks
TBD

#### Intro to Stateful Services
<p align="center">
  <img src="https://github.com/StereoDB/StereoDB/blob/dev/assets/architecture.png" alt="StereoDB logo" width="600px">
</p>

#### C# API
TBD

#### F# API
F# API has some benefits over C# API: 

- [Anonymous Records (struct)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/anonymous-records)
- [ValueOption<T> (struct)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/value-options)
- [Computation Expression (struct)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions)

With these constructions, you get **zero cost** abstractions that help reduce allocations and keep code expressive and readable.

```fsharp
open System
open FsToolkit.ErrorHandling
open StereoDB
open StereoDB.FSharp

// defines a Book type that implements IEntity<TId>
type Book = {
    Id: int
    Title: string
    Quantity: int    
}
with
    interface IEntity<int> with
        member this.Id = this.Id

// defines an Order type that implements IEntity<TId>
type Order = {
    Id: Guid
    BookId: int
    Quantity: int    
}
with
    interface IEntity<Guid> with
        member this.Id = this.Id

// defines a db schema that includes Orders and Books tables
// and a secondary index: 'BookIdIndex' for the Orders table
type Schema() =
    let _books = {| Table = StereoDbEngine.CreateTable<int, Book>() |}
    
    let _ordersTable = StereoDbEngine.CreateTable<Guid, Order>()
    let _orders = {|
        Table = _ordersTable
        BookIdIndex = _ordersTable.AddValueIndex(fun order -> order.BookId)
    |}
    
    member this.Books = _books
    member this.Orders = _orders

// defines a Db that implements IStereoDb<Schema>
type Db() =    
    let _engine = StereoDbEngine(Schema())
    
    interface IStereoDb<Schema> with
        member this.ReadTransaction(transaction) = _engine.ReadTransaction transaction
        member this.WriteTransaction(transaction) = _engine.WriteTransaction transaction
        member this.WriteTransaction<'T>(transaction) = _engine.WriteTransaction<'T>(transaction)
        
    static member Create() = Db() :> IStereoDb<Schema>

let test () =
    let db = Db.Create()

    // 1) adds book
    // WriteTransaction: it's a read-write transaction: we can query and mutate data

    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)

        let bookId = 1
        let book = { Id = bookId; Title = "book_1"; Quantity = 1 }
        books.Set book
    )

    // 2) creates an order
    // WriteTransaction: it's a read-write transaction: we can query and mutate data

    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        let orders = ctx.UseTable(ctx.Schema.Orders.Table)        
        
        voption {
            let bookId = 1
            let! book = books.Get bookId

            if book.Quantity > 0 then
                let order = { Id = Guid.NewGuid(); BookId = bookId; Quantity = 1 }
                let updatedBook = { book with Quantity = book.Quantity - 1 }
                
                books.Set updatedBook
                orders.Set order                    
        }
        |> ignore                     
    )

    // 3) query book and orders
    // ReadTransaction: it's a read-only transaction: we can query multiple tables at once

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        let bookIdIndex = ctx.Schema.Orders.BookIdIndex
        
        voption {
            let bookId = 1
            let! book = books.Get 1
            let orders = book.Id |> bookIdIndex.Find |> Seq.toArray
            
            return struct {| Book = book; Orders = orders |}
        }
    )    
```

#### Best practices
TBD
<!-- - closures (allocation)
- use immutable types
- secondary index IEnumerable convert to array for response
- GC settings
- Serializations, HTTP 2 -->
