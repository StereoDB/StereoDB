using Bogus;

namespace StereoDB.Benchmarks;

public enum Gender
{
    Male,
    Female
}

public class Order
{
    public Guid OrderId { get; set; }
    public string Item { get; set; }
    public int Quantity { get; set; }
    public int? LotNumber { get; set; }
}

public class User : IEntity<Guid>
{
    //public int Id { get; set; }
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Avatar { get; set; }
    public Guid CartId { get; set; }
    public string SSN { get; set; }
    public Gender Gender { get; set; }
    public List<Order> Orders { get; set; }
}

public class DataGen
{
    public static List<User> GenerateUsers(int count = 10)
    {
        Randomizer.Seed = new Random(3897234);
        
        var fruit = new[] {"apple", "banana", "orange", "strawberry", "kiwi"};
        
        var testOrders = new Faker<Order>()
            //Ensure all properties have rules. By default, StrictMode is false
            //Set a global policy by using Faker.DefaultStrictMode if you prefer.
            .StrictMode(true)
            .RuleFor(o => o.OrderId, f => Guid.NewGuid())
            .RuleFor(o => o.Item, f => f.PickRandom(fruit))
            .RuleFor(o => o.Quantity, f => f.Random.Number(1, 10))
            //A nullable int? with 80% probability of being null.
            //The .OrNull extension is in the Bogus.Extensions namespace.
            .RuleFor(o => o.LotNumber, f => f.Random.Int(0, 100).OrNull(f, 0.8f));

        var testUsers = new Faker<User>()
            .RuleFor(u => u.Id, f => Guid.NewGuid())
            //Basic rules using built-in generators
            .RuleFor(u => u.FirstName, f => f.Name.FirstName())
            .RuleFor(u => u.LastName, f => f.Name.LastName())
            .RuleFor(u => u.Avatar, f => f.Internet.Avatar())
            .RuleFor(u => u.Email, (f, u) => f.Internet.Email(u.FirstName, u.LastName))
            .RuleFor(u => u.Gender, f => f.PickRandom<Gender>())
            .RuleFor(u => u.CartId, f => Guid.NewGuid())
            .RuleFor(u => u.SSN, f => f.Random.Replace("###-##-####"))
            //And composability of a complex collection.
            .RuleFor(u => u.Orders, f => testOrders.Generate(5));
        
        return testUsers.Generate(count);
    }
}