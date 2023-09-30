module Tests.SqlParsing

open Xunit
open Tests.TestHelper
open Swensen.Unquote
open FsToolkit.ErrorHandling

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