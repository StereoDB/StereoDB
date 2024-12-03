module Tests.Sql.Delete

open System.Linq
open FsToolkit.ErrorHandling
open Swensen.Unquote
open Xunit
open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``Delete all rows in table`` () = task {
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)

    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteNonQuery "DELETE FROM Books"

    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! allIds = books.GetIds()
            
            return allIds.Count()
        }
    )
    
    let allIdsCount = result.Value
    test <@ allIdsCount = 0 @>
}

[<Theory>]
[<InlineData("DELETE FROM Books WHERE Id = 7")>]
[<InlineData("DELETE Books WHERE Id = 7")>]
[<InlineData("DELETE b FROM Books b WHERE Id = 7")>]
[<InlineData("DELETE Books FROM Books WHERE Id = 7")>]
let ``Delete rows from table by condition`` (sql) = task {
    use! db = StereoDb.init(Schema(), StereoDbSettings.OnlyInMemory)

    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    db.ExecuteNonQuery sql

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
}

