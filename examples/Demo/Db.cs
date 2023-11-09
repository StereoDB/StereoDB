namespace Demo;

using StereoDB;
using StereoDB.CSharp;

// defines a Book type that implements IEntity<TId>
public record Book : IEntity<int>
{
    public int Id { get; init; }
    public string Title { get; init; }
    public int Quantity { get; init; }
}

// defines an Order type that implements IEntity<TId>
public record Order : IEntity<Guid>
{
    public Guid Id { get; init; }
    public int BookId { get; init; }
    public int Quantity { get; init; }
}

public class BooksSchema
{
    public ITable<int, Book> Table { get; init; }
}

public class OrdersSchema
{
    public ITable<Guid, Order> Table { get; init; }
    public IValueIndex<int, Order> BookIdIndex { get; init; }
    public IRangeScanIndex<int, Order> QuantityRangeIndex { get; init; }
}

// defines a DB schema that includes Orders and Books tables
// and a secondary index: 'BookIdIndex' for the Orders table
public class Schema
{
    public BooksSchema Books { get; }
    public OrdersSchema Orders { get; }
    
    public Schema()
    {
        Books = new BooksSchema
        {
            Table = StereoDb.CreateTable<int, Book>()
        };

        var ordersTable = StereoDb.CreateTable<Guid, Order>();

        Orders = new OrdersSchema
        {
            Table = ordersTable,
            BookIdIndex = ordersTable.AddValueIndex(order => order.BookId),
            QuantityRangeIndex = ordersTable.AddRangeScanIndex(order => order.Quantity)
        };
    }
}