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

public record BooksSchema
{
    public ITable<int, Book> Table { get; init; }
}

public record OrdersSchema
{
    public ITable<Guid, Order> Table { get; init; }
    public IValueIndex<int, Order> BookIdIndex { get; init; }
}

// defines a DB schema that includes Orders and Books tables
// and a secondary index: 'BookIdIndex' for the Orders table
public record Schema
{
    public BooksSchema Books { get; }
    public OrdersSchema Orders { get; }
    
    public Schema()
    {
        Books = new BooksSchema
        {
            Table = StereoDbEngine.CreateTable<int, Book>()
        };

        var ordersTable = StereoDbEngine.CreateTable<Guid, Order>();


        Orders = new OrdersSchema()
        {
            Table = ordersTable,
            BookIdIndex = ordersTable.AddValueIndex(order => order.BookId)
        };
    }
}

// defines a DB that implements IStereoDb<Schema>
public class Db : IStereoDb<Schema>
{
    private readonly StereoDbEngine<Schema> _engine = StereoDbEngine.Create(new Schema());

    public T ReadTransaction<T>(Func<ReadOnlyTsContext<Schema>, T> transaction) => 
        _engine.ReadTransaction(transaction);
    
    public T WriteTransaction<T>(Func<ReadWriteTsContext<Schema>, T> transaction) => 
        _engine.WriteTransaction(transaction);

    public void WriteTransaction(Action<ReadWriteTsContext<Schema>> transaction) =>
        _engine.WriteTransaction(transaction);
}