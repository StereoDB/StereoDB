module Tests.SqlParsing

open Xunit
open Tests.TestHelper
open Swensen.Unquote
open FsToolkit.ErrorHandling
open StereoDB.FSharp

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