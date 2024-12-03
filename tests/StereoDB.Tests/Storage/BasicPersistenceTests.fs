module Tests.BasicPersistenceTests

open Swensen.Unquote
open FsToolkit.ErrorHandling
open Xunit

open StereoDB
open StereoDB.FSharp
open Tests.TestHelper

[<Fact>]
let ``Get and Set operations should work correctly`` () = task {
    let dbSettings = StereoDbSettings.FileStorageMsgPack()
    StereoDb.remove dbSettings
    let! db = StereoDb.init(Schema3(), dbSettings)   
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }            
            books.Set(book)
    )    
    
    // commit data
    do! db.DisposeAsync()
    use! db = StereoDb.init(Schema3(), dbSettings)
    
    // query book
    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! book1 = books.Get 1
            let idsCount = books.GetIds() |> Seq.length        
            return {| Book = book1; IdsCount = idsCount |}
        }
    )
    
    let book = result.Value.Book
    let idsCount = result.Value.IdsCount
    
    test <@ book.Id = 1 && book.Quantity = 1 @>
    test <@ idsCount = 10 @>    
}

[<Fact>]
let ``Delete operations should work correctly`` () = task {
    let dbSettings = StereoDbSettings.FileStorageMsgPack()
    StereoDb.remove dbSettings
    let! db = StereoDb.init(Schema3(), dbSettings)   
        
    // add books        
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }            
            books.Set(book)
    )    
    
    // commit data
    do! db.DisposeAsync()
    use! db = StereoDb.init(Schema3(), dbSettings)
    
    // delete books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)        
        
        books.GetIds()
        |> Seq.iter(books.Delete >> ignore)        
    )
    
    // commit data
    do! db.DisposeAsync()
    use! db = StereoDb.init(Schema3(), dbSettings)
        
    let result = db.ReadTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        voption {
            let! book1 = books.Get 1
            let idsCount = books.GetIds() |> Seq.length        
            return {| Book = book1; IdsCount = idsCount |}
        }
    )
    
    let dbIsEmpty = result.IsNone
    
    test <@ dbIsEmpty @>    
}