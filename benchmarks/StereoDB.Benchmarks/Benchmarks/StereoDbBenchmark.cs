using BenchmarkDotNet.Attributes;
using Serilog;
using StereoDB.CSharp;
using StereoDB.Infra;

namespace StereoDB.Benchmarks.Benchmarks;

class UsersSchema
{
    public ITable<Guid, User> Table { get; init; }
    //public IValueIndex<string, User> EmailIndex { get; init; }

    public UsersSchema()
    {
        var table = StereoDb.CreateTable<Guid, User>("users");
        //var emailIndex = table.AddValueIndex(x => x.Email);

        Table = table;
        //EmailIndex = emailIndex;
    }
}

class Schema : IDbSchema
{
    public UsersSchema Users { get; init; } = new();
    public IEnumerable<ITable> AllTables => [Users.Table];
}

// class Db : IStereoDb<Schema>
// {
//     private readonly StereoDbEngine<Schema> _engine = StereoDbEngine.Create(new Schema());
//
//     public T ReadTransaction<T>(Func<ReadOnlyTsContext<Schema>, T> transaction) => _engine.ReadTransaction(transaction);
//     public T WriteTransaction<T>(Func<ReadWriteTsContext<Schema>, T> transaction) => _engine.WriteTransaction(transaction);
//     public void WriteTransaction(Action<ReadWriteTsContext<Schema>> transaction) => _engine.WriteTransaction(transaction);
// }

public enum Concurrency
{
    RwLockSlimLock,
    CasSpinLock,
    CasRwSpinLock
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 5, iterationCount: 5)]
public class StereoDbBenchmark
{
    private List<User> _allData;
    private IStereoDb<Schema> _dbRwLockSlimLock = null;
    private IStereoDb<Schema> _dbCasSpinLock = null;
    private IStereoDb<Schema> _dbCasRwSpinLock = null;
    
    private Random _random = new();

    private Int64 CurrentDbWriteCount1 = 0;
    private Int64 CurrentDbWriteCount2 = 0;
    private Int64 CurrentDbReadCount = 0;
    
    [Params(Concurrency.CasRwSpinLock, Concurrency.CasSpinLock)] // Concurrency.CasRwSpinLock  
    public Concurrency Concurrency = Concurrency.CasSpinLock;
    
    [Params(30)] public int ReadThreadCount = 0;
    [Params(30)] public int WriteThreadCount = 0;
    
    [Params(1_000_000)] public int UsersCount;
    // [Params(1_000_000)] public int DbReadCount;
    // [Params(500_000)] public int DbWriteCount;
    [Params(4_000_000)] public int DbReadCount;
    [Params(100_000,1_000_000)] public int DbWriteCount;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _allData = DataGen.GenerateUsers(UsersCount);
        
        _dbRwLockSlimLock = InitStereoDb(new Schema(), StereoDbSettings.OnlyInMemory, Concurrency.RwLockSlimLock);
        _dbRwLockSlimLock.WriteTransaction(ctx =>
        {
            var table = ctx.UseTable(ctx.Schema.Users.Table);

            foreach (var item in _allData)
            {
                table.Set(item.Id, item);
            }
        });
        
        _dbCasSpinLock = InitStereoDb(new Schema(), StereoDbSettings.OnlyInMemory, Concurrency.CasSpinLock);
        _dbCasSpinLock.WriteTransaction(ctx =>
        {
            var table = ctx.UseTable(ctx.Schema.Users.Table);

            foreach (var item in _allData)
            {
                table.Set(item.Id, item);
            }
        });
        
        _dbCasRwSpinLock = InitStereoDb(new Schema(), StereoDbSettings.OnlyInMemory, Concurrency.CasRwSpinLock);
        _dbCasRwSpinLock.WriteTransaction(ctx =>
        {
            var table = ctx.UseTable(ctx.Schema.Users.Table);

            foreach (var item in _allData)
            {
                table.Set(item.Id, item);
            }
        });
    }

    [Benchmark]
    public void Basic_Read_Modify_Write_Tx()
    {
        var db = Concurrency switch
        {
            Concurrency.RwLockSlimLock => _dbRwLockSlimLock,
            Concurrency.CasSpinLock => _dbCasSpinLock,
            Concurrency.CasRwSpinLock => _dbCasRwSpinLock,
            _ => null
        };
        
        var writeOps1 = new TaskCompletionSource();
        var writeOps2 = new TaskCompletionSource();
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
                    db.WriteTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.Set(randomUser.Id, randomUser);
                    });

                    Interlocked.Increment(ref CurrentDbWriteCount1);
                }

                writeOps1.TrySetResult();
            });
        }

        for (int i = 0; i < ReadThreadCount; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (Interlocked.Read(ref CurrentDbReadCount) <= DbReadCount)
                {
                    var user = db.ReadTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.TryGet(randomUser.Id, out var user);

                        return user;
                    });

                    if (user == null) 
                        throw new NullReferenceException();
                    
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
                    db.WriteTransaction(ctx =>
                    {
                        var index = _random.Next(0, _allData.Count - 1);
                        var randomUser = _allData[index];

                        var table = ctx.UseTable(ctx.Schema.Users.Table);
                        table.Set(randomUser.Id, randomUser);
                    });

                    Interlocked.Increment(ref CurrentDbWriteCount2);
                }

                writeOps2.TrySetResult();
            });
        }

        Task.WaitAll(writeOps1.Task, writeOps2.Task, readOps.Task);
    }

    IStereoDb<Schema> InitStereoDb(Schema schema, StereoDbSettings settings, Concurrency concurrency)
    {
        ConcurrencyControl.IThreadLock threadLock = concurrency switch
        {
            Concurrency.RwLockSlimLock => new ConcurrencyControl.RwLockSlimLock(),
            Concurrency.CasSpinLock => new ConcurrencyControl.CasSpinLock(),
            Concurrency.CasRwSpinLock => new ConcurrencyControl.CasRwSpinLock(),
            _ => throw new ArgumentOutOfRangeException(nameof(concurrency), concurrency, null)
        };
        
        var logger = new LoggerConfiguration().CreateLogger();
        var db = new StereoDb<Schema>(logger, threadLock, schema, settings);
        db.InitDb().GetAwaiter().GetResult();
        return db;
    }
}