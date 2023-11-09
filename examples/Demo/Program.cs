using Demo;
using StereoDB.CSharp;

var db = StereoDb.Create(new Schema());

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
    var quantityIndex = ctx.Schema.Orders.QuantityRangeIndex;
    
    // example of RangeScanIndex
    var booksRange = quantityIndex.SelectRange(0, 5).ToArray();
    
    // example of ValueIndex
    if (books.TryGet(1, out var book))
    {
        var orders = bookIdIndex.Find(book.Id).ToArray();
        return (book, orders);
    }
    
    return (null, null);
});