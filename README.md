<p align="center">
  <img src="https://github.com/StereoDB/StereoDB/blob/dev/assets/stereo_db_logo.png" alt="StereoDB logo" width="600px">
</p>

[![build](https://github.com/StereoDB/StereoDB/actions/workflows/build.yml/badge.svg)](https://github.com/StereoDB/StereoDB/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/stereodb.svg)](https://www.nuget.org/packages/nbomber/)

#### StereoDB
Ultrafast and lightweight in-process memory database written in F# that supports: transactions, secondary indexes, persistence, and data size larger than RAM. The primary use case for this database is building Stateful Services (API or ETL Worker) that keep all data in memory and can provide millions of RPS from a single node. 

Supported features:
- [x] C# and F# API
- [x] Transactions (read-only, read-write)
- [x] Secondary Indexes
  - [x] Value Index (hash-based index)
  - [x] Range Scan Index
- [ ] Data size larger than RAM
- [ ] Data persistence
- [ ] Distributed mode
  - [ ] Server and client discovery
  - [ ] Range-based sharding

#### Benchmarks
TBD

#### Intro to Stateful Services
<p align="center">
  <img src="https://github.com/StereoDB/StereoDB/blob/dev/assets/architecture.png" alt="StereoDB logo" width="600px">
</p>

#### C# API
```csharp
using System;
using StereoDB;
using StereoDB.CSharp;

// defines a Book type that implements IEntity<TId>
public record Book : IEntity<int>
{
    public int Id { get; init; }
    public string Title { get; init; }
    public int Quantity { get; init; }
}

// defines an Order type that implements IEntity<TId>
public record Order : IEntity<Guid>
{
    public Guid Id { get; init; }
    public int BookId { get; init; }
    public int Quantity { get; init; }
}

public record BooksSchema
{
    public ITable<int, Book> Table { get; init; }
}

public record OrdersSchema
{
    public ITable<Guid, Order> Table { get; init; }
    public IValueIndex<int, Order> BookIdIndex { get; init; }
}

// defines a DB schema that includes Orders and Books tables
// and a secondary index: 'BookIdIndex' for the Orders table
public record Schema
{
    public BooksSchema Books { get; }
    public OrdersSchema Orders { get; }
    
    public Schema()
    {
        Books = new BooksSchema
        {
            Table = StereoDbEngine.CreateTable<int, Book>()
        };

        var ordersTable = StereoDbEngine.CreateTable<Guid, Order>();


        Orders = new OrdersSchema()
        {
            Table = ordersTable,
            BookIdIndex = ordersTable.AddValueIndex(order => order.BookId)
        };
    }
}

// defines a DB that implements IStereoDb<Schema>
public class Db : IStereoDb<Schema>
{
    private readonly StereoDbEngine<Schema> _engine = StereoDbEngine.Create(new Schema());

    public T ReadTransaction<T>(Func<ReadOnlyTsContext<Schema>, T> transaction) => 
        _engine.ReadTransaction(transaction);
    
    public T WriteTransaction<T>(Func<ReadWriteTsContext<Schema>, T> transaction) => 
        _engine.WriteTransaction(transaction);

    public void WriteTransaction(Action<ReadWriteTsContext<Schema>> transaction) =>
        _engine.WriteTransaction(transaction);
}

public static class Demo
{
    public static void Run()
    {
        var db = new Db();

        // 1) adds book
        // WriteTransaction: it's a read-write transaction: we can query and mutate data
        
        db.WriteTransaction(ctx =>
        {
            var books = ctx.UseTable(ctx.Schema.Books.Table);
        
            foreach (var id in Enumerable.Range(0, 10))
            {
                var book = new Book {Id = id, Title = $"book_{id}", Quantity = 1};
                books.Set(book);
            }
        });
               
        // 2) creates an order
        // WriteTransaction: it's a read-write transaction: we can query and mutate data
        
        db.WriteTransaction(ctx =>
        {
            var books = ctx.UseTable(ctx.Schema.Books.Table);
            var orders = ctx.UseTable(ctx.Schema.Orders.Table);
        
            foreach (var id in books.GetIds())
            {
                if (books.TryGet(id, out var book) && book.Quantity > 0)
                {
                    var order = new Order {Id = Guid.NewGuid(), BookId = id, Quantity = 1};
                    var updatedBook = book with { Quantity = book.Quantity - 1 };
                    
                    books.Set(updatedBook);
                    orders.Set(order);
                }
            }
        });
                
        // 3) query book and orders
        // ReadTransaction: it's a read-only transaction: we can query multiple tables at once
        
        var result = db.ReadTransaction(ctx =>
        {
            var books = ctx.UseTable(ctx.Schema.Books.Table);
            var bookIdIndex = ctx.Schema.Orders.BookIdIndex;
        
            if (books.TryGet(1, out var book))
            {
                var orders = bookIdIndex.Find(book.Id).ToArray();
                return (book, orders);
            }
            
            return (null, null);
        });    
    }    
}
```

#### F# API
F# API has some benefits over C# API, mainly in expressiveness and type safety: 

- [Anonymous Records](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/anonymous-records) - It provides in place schema definition. You don't need to define extra types for schema as you do with C#. Also, it helps you model efficient **(zero-cost, since it supports [structs](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/structs))** and expressive - return result type. 
- [ValueOption<'T>](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/value-options) - It's used for StereoDB API to model emptiness in a type safe manner. Also, it's a **zero-cost abstraction** since it's struct.
- [Computation Expression](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) - It helps to express multiple if & else checks on emptiness/null for ValueOption<'T>, into a single **voption { }** expression. To use **voption { }**, [FsToolkit.ErrorHandling](https://github.com/demystifyfp/FsToolkit.ErrorHandling) should be installed. In the case of **voption {}**, it's also a **zero-cost abstraction**, the compiler generates optimized code without allocations.

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

// defines a DB schema that includes Orders and Books tables
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

// defines a DB that implements IStereoDb<Schema>
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
