using BenchmarkDotNet.Attributes;
using StereoDB.CSharp;

namespace StereoDB.Benchmarks.Benchmarks;

class UsersSchema
{
    public ITable<Guid, User> Table { get; init; }
    //public IValueIndex<string, User> EmailIndex { get; init; }

    public UsersSchema()
    {
        var table = StereoDbEngine.CreateTable<Guid, User>();
        //var emailIndex = table.AddValueIndex(x => x.Email);

        Table = table;
        //EmailIndex = emailIndex;
    }
}

class Schema
{
    public UsersSchema Users { get; init; } = new();
}

class Db : IStereoDb<Schema>
{
    private readonly StereoDbEngine<Schema> _engine = StereoDbEngine.Create(new Schema());

    public T ReadTransaction<T>(Func<ReadOnlyTsContext<Schema>, T> transaction) => _engine.ReadTransaction(transaction);
    public T WriteTransaction<T>(Func<ReadWriteTsContext<Schema>, T> transaction) => _engine.WriteTransaction(transaction);
    public void WriteTransaction(Action<ReadWriteTsContext<Schema>> transaction) => _engine.WriteTransaction(transaction);
}

[MemoryDiagnoser]
public class StereoDbBenchmark
{
    private List<User> _allData;
    private Db _db = new();
    private Random _random = new();

    private Int64 CurrentDbWriteCount1 = 0;
    private Int64 CurrentDbWriteCount2 = 0;
    private Int64 CurrentDbReadCount = 0;
    
    [Params(30)] public int ReadThreadCount = 0;
    [Params(30)] public int WriteThreadCount = 0;
    
    [Params(4_000_000)] public int UsersCount;
    [Params(3_000_000)] public int DbReadCount;
    [Params(100_000)] public int DbWriteCount;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _allData = DataGen.GenerateUsers(UsersCount);

        _db.WriteTransaction(ctx =>
        {
            var table = ctx.UseTable(ctx.Schema.Users.Table);

            foreach (var item in _allData)
            {
                table.Set(item);
            }
        });
    }

    [Benchmark]
    public void ReadWrite()
    {
        var writeOps = new TaskCompletionSource();
        var readOps = new TaskCompletionSource();
        CurrentDbWriteCount1 = 0;
        CurrentDbWriteCount2 = 0;
        CurrentDbReadCount = 0;
        var writeCount = DbWriteCount / 2;
        var writeThreads = WriteThreadCount / 2;
        
        for (int i = 0; i < writeThreads; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (Interlocked.Read(ref CurrentDbWriteCount1) <= writeCount)
                {
                    _db.WriteTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.Set(randomUser);
                    });

                    Interlocked.Increment(ref CurrentDbWriteCount1);
                }

                writeOps.TrySetResult();
            });
        }

        for (int i = 0; i < ReadThreadCount; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (Interlocked.Read(ref CurrentDbReadCount) <= DbReadCount)
                {
                    var user = _db.ReadTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.TryGet(randomUser.Id, out var user);

                        return user;
                    });

                    Interlocked.Increment(ref CurrentDbReadCount);
                }

                readOps.TrySetResult();
            });
        }
        
        for (int i = 0; i < writeThreads; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (Interlocked.Read(ref CurrentDbWriteCount2) <= writeCount)
                {
                    _db.WriteTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.Set(randomUser);
                    });

                    Interlocked.Increment(ref CurrentDbWriteCount2);
                }

                writeOps.TrySetResult();
            });
        }

        Task.WaitAll(writeOps.Task, readOps.Task);
    }
}