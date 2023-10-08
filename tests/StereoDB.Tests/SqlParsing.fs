module Tests.SqlParsing

open Xunit
open Tests.TestHelper
open Swensen.Unquote
open FsToolkit.ErrorHandling
open StereoDB.FSharp
open System.Linq

let sqlCompilationFailure (db: IStereoDb<Schema>) sql expectedError = 
    try
        db.ExecuteSql sql
        Assert.True(false, "Should not happens")
    with ex ->
        Assert.Equal (expectedError, ex.Message)

[<Fact>]
let ``Fails on not existing table`` () =
    let db = Db.Create()
    
    sqlCompilationFailure db "UPDATE NonExisting SET Quantity = 2" "Table NonExisting is not defined"

[<Fact>]
let ``Fails on not existing column`` () =
    let db = Db.Create()
    
    sqlCompilationFailure db "UPDATE Books SET NonExisting = 2" "Column NonExisting does not exist in table Books"

[<Fact>]
let ``Update using other field`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteSql "UPDATE Books SET Quantity = Id"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! book1 = books.Get 1
            let! book2 = books.Get 3
            
            return struct {| Book1 = book1; Book2 = book2; |}
        }
    )
    
    let book1 = result.Value.Book1
    test <@ book1.Quantity = 1 @>
    let book2 = result.Value.Book2
    test <@ book2.Quantity = 3 @>

[<Fact>]
let ``Update with WHERE`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteSql "UPDATE Books SET Quantity = 222 WHERE Id = 8"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! book1 = books.Get 8
            let! book2 = books.Get 3
            
            return struct {| Book1 = book1; Book2 = book2; |}
        }
    )
    
    let book1 = result.Value.Book1
    test <@ book1.Quantity = 222 @>
    let book2 = result.Value.Book2
    test <@ book2.Quantity = 1 @>

[<Fact>]
let ``Update using other field for mutable record`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.MutableBooks.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1; ISBN = "ISBN" }
            books.Set book
    )

    db.ExecuteSql "UPDATE MutableBooks SET Quantity = Id"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.MutableBooks.Table)
        
        voption {
            let! book1 = books.Get 1
            let! book2 = books.Get 3
            
            return struct {| Book1 = book1; Book2 = book2; |}
        }
    )
    
    let book1 = result.Value.Book1
    test <@ book1.Quantity = 1 @>
    let book2 = result.Value.Book2
    test <@ book2.Quantity = 3 @>

[<Fact>]
let ``Update all rows in table`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteSql "UPDATE Books SET Quantity = 2"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! book1 = books.Get 1
            let! book2 = books.Get 3
            
            return struct {| Book1 = book1; Book2 = book2; |}
        }
    )
    
    let book1 = result.Value.Book1
    test <@ book1.Quantity = 2 @>
    let book2 = result.Value.Book2
    test <@ book2.Quantity = 2 @>

[<Fact>]
let ``Delete all rows in table`` () =
    let db = Db.Create()

    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteSql "DELETE FROM Books"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! allIds = books.GetIds()
            
            return allIds.Count()
        }
    )
    
    let allIdsCount = result.Value
    test <@ allIdsCount = 0 @>

[<Fact>]
let ``Delete rows from table by condition`` () =
    let db = Db.Create()

    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteSql "DELETE FROM Books WHERE Id = 7"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! allIds = books.GetIds()
            let book = books.Get 7
            match book with
            | ValueSome _ -> failwith "Record with id 7 was not deleted"
            | _ -> ()
            
            return allIds.Count()
        }
    )
    
    let allIdsCount = result.Value
    test <@ allIdsCount = 9 @>

type SubBook =
    {
        Id: int
        Quantity: int
    }

[<Fact>]
let ``Select all rows`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

[<Fact>]
let ``Select filtered rows`` () =
    let db = Db.Create()
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id <= 3"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 3 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

    let booksCount2 = (db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id >= 3").Value.Count
    test <@ booksCount2 = 8 @>

    let booksCount3 = (db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id = 3").Value.Count
    test <@ booksCount3 = 1 @>

    let booksCount4 = (db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id <> 3").Value.Count
    test <@ booksCount4 = 9 @>

    let booksCount5 = (db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id < 3").Value.Count
    test <@ booksCount5 = 2 @>

    let booksCount6 = (db.ExecuteSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id > 3").Value.Count
    test <@ booksCount6 = 7 @>