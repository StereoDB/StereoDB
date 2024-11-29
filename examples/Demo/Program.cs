using Demo;
using StereoDB;
using StereoDB.CSharp;

await using var db = await StereoDb.Init(new Schema(), new StereoDbSettings(localPersistenceEnabled: true));

// 1) adds book
// WriteTransaction: it's a read-write transaction: we can query and mutate data

db.WriteTransaction(ctx =>
{
    var books = ctx.UseTable(ctx.Schema.Books.Table);

    foreach (var id in Enumerable.Range(0, 10_000))
    {
        var book = new Book { Id = id, Title = $"book_{id}", Quantity = 1, Categories = [ 1, 2 ] };
        books.Set(book.Id, book);
        
        // if (books.TryGet(book.Id, out var newBook))
        // {
        //     
        // }
    }
});

// await db.CommitAsync();

// var result1 = db.ReadTransaction(ctx =>
// {
//     var books = ctx.UseTable(ctx.Schema.Books.Table);
//     
//     return books.TryGet(1, out var newBook) ? newBook : null;
// });
       
// 2) creates an order
// WriteTransaction: it's a read-write transaction: we can query and mutate data

// db.WriteTransaction(ctx =>
// {
//     var books = ctx.UseTable(ctx.Schema.Books.Table);
//     var orders = ctx.UseTable(ctx.Schema.Orders.Table);
//     
//     foreach (var id in books.GetIds())
//     {
//         if (books.TryGet(id, out var book) && book.Quantity > 0)
//         {
//             var order = new Order {Id = Guid.NewGuid(), BookId = id, Quantity = 1};
//             var updatedBook = book with { Quantity = book.Quantity - 1 };
//             
//             books.Set(updatedBook.Id, updatedBook);
//             orders.Set(order.Id, order);
//         }
//     }
// });
        
// 3) query book and orders
// ReadTransaction: it's a read-only transaction: we can query multiple tables at once

// var result = db.ReadTransaction(ctx =>
// {
//     var books = ctx.UseTable(ctx.Schema.Books.Table);
//     var categoryIndex = ctx.Schema.Books.CategoryIndex;
//     var bookIdIndex = ctx.Schema.Orders.BookIdIndex;
//     var quantityIndex = ctx.Schema.Orders.QuantityRangeIndex;
//     
//     // example of RangeScanIndex
//     var booksRange = quantityIndex.SelectRange(0, 5).ToArray();
//     
//     // example of MultiValueIndex
//     var booksWithCategory1 = categoryIndex.Find(1).ToArray();
//     
//     // example of ValueIndex
//     if (books.TryGet(1, out var book))
//     {
//         var orders = bookIdIndex.Find(book.Id).ToArray();
//         return (book, orders);
//     }
//     
//     return (null, null);
// });