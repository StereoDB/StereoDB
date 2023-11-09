module Tests.Sql.Update

open FsToolkit.ErrorHandling
open StereoDB.FSharp
open Swensen.Unquote
open Tests.TestHelper
open Xunit

[<Fact>]
let ``Update using other field`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecSql "UPDATE Books SET Quantity = Id"

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
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecSql "UPDATE Books SET Quantity = 222 WHERE Id = 8"

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
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.MutableBooks.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1; ISBN = "ISBN" }
            books.Set book
    )

    db.ExecSql "UPDATE MutableBooks SET Quantity = Id"

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
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecSql "UPDATE Books SET Quantity = 2"

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

