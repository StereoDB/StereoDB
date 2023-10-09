module Tests.Sql.Delete

open System.Linq
open FsToolkit.ErrorHandling
open Swensen.Unquote
open Tests.TestHelper
open Xunit

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

