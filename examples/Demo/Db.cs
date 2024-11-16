using MessagePack;

namespace Demo;

using StereoDB;
using StereoDB.CSharp;

[MessagePackObject]
public record Book
{
    [Key(0)] public int Id { get; init; }
    [Key(1)] public string Title { get; init; }
    [Key(2)] public int Quantity { get; init; }
    [Key(3)] public int[] Categories { get; init; }
}

[MessagePackObject]
public record Order
{
    [Key(0)] public Guid Id { get; init; }
    [Key(1)] public int BookId { get; init; }
    [Key(2)] public int Quantity { get; init; }
}

public class BooksSchema
{
    public ITable<int, Book> Table { get; init; }
    public IValueIndex<int, Book> CategoryIndex { get; init; }
}

public class OrdersSchema
{
    public ITable<Guid, Order> Table { get; init; }
    public IValueIndex<int, Order> BookIdIndex { get; init; }
    public IRangeScanIndex<int, Order> QuantityRangeIndex { get; init; }
}

// defines a DB schema that includes Orders and Books tables
// and a secondary index: 'BookIdIndex' for the Orders table
public class Schema : IDbSchema
{
    public BooksSchema Books { get; }
    public OrdersSchema Orders { get; }
    
    public Schema()
    {
        var booksTable = StereoDb.CreateTable<int, Book>("books");
        
        Books = new BooksSchema
        {
            Table = booksTable,
            CategoryIndex = booksTable.AddMultiValueIndex(order => order.Categories)
        };

        var ordersTable = StereoDb.CreateTable<Guid, Order>("orders");

        Orders = new OrdersSchema
        {
            Table = ordersTable,
            BookIdIndex = ordersTable.AddValueIndex(order => order.BookId),
            QuantityRangeIndex = ordersTable.AddRangeScanIndex(order => order.Quantity)
        };
    }

    public IEnumerable<ITable> AllTables => [Orders.Table, Books.Table];
}