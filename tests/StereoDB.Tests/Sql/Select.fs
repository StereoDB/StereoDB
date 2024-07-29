module Tests.Sql.Select

open StereoDB.FSharp
open Xunit
open Tests.TestHelper
open Swensen.Unquote

type SubBook =
    {
        Id: int
        Quantity: int
    }

type SuperBook =
    {
        SuperId: int
        Quantity: int
    }

[<Fact>]
let ``Select all rows`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

[<Fact>]
let ``Select filtered rows`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id <= 3"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 3 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

    let booksCount2 = (db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id >= 3").Value.Count
    test <@ booksCount2 = 8 @>

    let booksCount3 = (db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id = 3").Value.Count
    test <@ booksCount3 = 1 @>

    let booksCount4 = (db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id <> 3").Value.Count
    test <@ booksCount4 = 9 @>

    let booksCount5 = (db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id < 3").Value.Count
    test <@ booksCount5 = 2 @>

    let booksCount6 = (db.ExecSql<SubBook> "SELECT Id, Quantity FROM Books WHERE Id > 3").Value.Count
    test <@ booksCount6 = 7 @>

[<Fact>]
let ``Select star`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecSql<Book> "SELECT * FROM Books"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

[<Fact>]
let ``Select star into sub-type`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
            books.Set book
    )

    let result = db.ExecSql<SubBook> "SELECT * FROM Books"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 2 @>

// [<Fact>]
// let ``Select within transactions`` () =
//     let db = StereoDb.create(Schema())
//     
//     // add books
//     db.WriteTransaction(fun ctx ->
//         let books = ctx.UseTable(ctx.Schema.Books.Table)
//         
//         for i in [1..10] do
//             let book = { Id = i; Title = $"book_{i}"; Quantity = 1 }
//             books.Set book
//     )
//     db.ReadTransaction(fun ctx ->
//         let result = db.ExecuteSql<SubBook> ((ctx, "SELECT * FROM Books"))
//     
//         let booksCount = result.Value.Count
//         test <@ booksCount = 10 @>
//         let book1 = result.Value[0]
//         test <@ book1.Id = 1 @>
//         let book2 = result.Value[1]
//         test <@ book2.Id = 2 @>
//
//         result
//     )

[<Fact>]
let ``Order by`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let result = db.ExecSql<Book> "SELECT * FROM Books ORDER BY Quantity, Title"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 6 @>
    test <@ book1.Quantity = 0 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 5 @>
    test <@ book2.Quantity = 1 @>
    let book2 = result.Value[2]
    test <@ book2.Id = 7 @>
    test <@ book2.Quantity = 1 @>

[<Fact>]
let ``Order by ASC`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let result = db.ExecSql<Book> "SELECT * FROM Books ORDER BY Quantity ASC, Title"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 6 @>
    test <@ book1.Quantity = 0 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 5 @>
    test <@ book2.Quantity = 1 @>
    let book2 = result.Value[2]
    test <@ book2.Id = 7 @>
    test <@ book2.Quantity = 1 @>

[<Fact>]
let ``Order by DESC`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let result = db.ExecSql<Book> "SELECT * FROM Books ORDER BY Quantity DESC, Title"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    test <@ book1.Quantity = 5 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 10 @>
    test <@ book2.Quantity = 4 @>
    let book2 = result.Value[2]
    test <@ book2.Id = 2 @>
    test <@ book2.Quantity = 4 @>

[<Fact>]
let ``TOP`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let result = db.ExecSql<Book> "SELECT TOP 3 * FROM Books ORDER BY Quantity DESC, Title"
    
    let booksCount = result.Value.Count
    test <@ booksCount = 3 @>
    let book1 = result.Value[0]
    test <@ book1.Id = 1 @>
    test <@ book1.Quantity = 5 @>
    let book2 = result.Value[1]
    test <@ book2.Id = 10 @>
    test <@ book2.Quantity = 4 @>
    let book2 = result.Value[2]
    test <@ book2.Id = 2 @>
    test <@ book2.Quantity = 4 @>
    

[<Theory>]
[<InlineData("SELECT b.Id as SuperId, b.Quantity FROM Books AS b ORDER BY Quantity, Title")>]
[<InlineData("SELECT b.Id as SuperId, b.Quantity FROM Books b ORDER BY Quantity, Title")>]
let ``Alias for table`` (sql) =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let result = db.ExecSql<SuperBook> sql
    
    let booksCount = result.Value.Count
    test <@ booksCount = 10 @>
    let book1 = result.Value[0]
    test <@ book1.SuperId = 6 @>
    test <@ book1.Quantity = 0 @>
    let book2 = result.Value[1]
    test <@ book2.SuperId = 5 @>
    test <@ book2.Quantity = 1 @>
    let book2 = result.Value[2]
    test <@ book2.SuperId = 7 @>
    test <@ book2.Quantity = 1 @>

[<Fact>]
let ``Select filtered rows with between`` () =
    let db = StereoDb.create(Schema())
    
    // add books
    db.WriteTransaction(fun ctx ->
        let books = ctx.UseTable(ctx.Schema.Books.Table)
        
        for i in [1..10] do
            let book = { Id = i; Title = $"book_{i}"; Quantity = abs(6 - i) }
            books.Set book
    )

    let booksCount = (db.ExecSql<Book> "SELECT * FROM Books WHERE Id BETWEEN 4 AND 6").Value.Count
    test <@ booksCount = 3 @>