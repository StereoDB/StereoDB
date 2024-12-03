using StereoDB;
using StereoDB.CSharp;
using StorageDemo;

await using var db = await StereoDb.Init(new Schema(), StereoDbSettings.FileStorageMsgPack(dbFolderPath: "my_db"));

db.WriteTransaction(ctx =>
{
    var books = ctx.UseTable(ctx.Schema.Books.Table);

    foreach (var id in Enumerable.Range(0, 10))
    {
        var book = new Book { Id = id, Title = $"book_{id}", Quantity = 1 };
        books.Set(book.Id, book);
    }
});

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
            
            books.Set(updatedBook.Id, updatedBook);
            orders.Set(order.Id, order);
        }
    }
});

// Closing database - it triggers a commit on the disk
// StereoDB also executes periodic auto-commit
await db.DisposeAsync();

// Opens a database and restore data from the disk
await using var db2 = await StereoDb.Init(new Schema(), StereoDbSettings.FileStorageMsgPack());

var result = db2.ReadTransaction(ctx =>
{
    var books = ctx.UseTable(ctx.Schema.Books.Table);
    var bookIdIndex = ctx.Schema.Orders.BookIdIndex;
    
    // example of ValueIndex
    if (books.TryGet(1, out var book))
    {
        var orders = bookIdIndex.Find(book.Id).ToArray();
        return (book, orders);
    }
    
    return (null, null);
});