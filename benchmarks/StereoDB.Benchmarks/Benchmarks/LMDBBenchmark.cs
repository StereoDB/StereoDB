using BenchmarkDotNet.Attributes;
using LightningDB;

namespace StereoDB.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class LMDBBenchmark
{
    private LightningEnvironment ENV;
    private LightningDatabase DB;
    
    private Random _random = new();
    private List<User> _allData;
    private byte[] _userRecord = Array.Empty<byte>();

    private Int64 CurrentDbWriteCount1 = 0;
    private Int64 CurrentDbWriteCount2 = 0;
    private Int64 CurrentDbReadCount = 0;
    
    [Params(30)] public int ReadThreadCount = 0;
    [Params(30)] public int WriteThreadCount = 0;
    
    [Params(1)] public int RecordSizeBytes;
    [Params(1000)] public int UsersCount;
    [Params(1000)] public int DbReadCount;
    [Params(100)] public int DbWriteCount;
    
    [GlobalSetup]
    public void GlobalSetup()
    {
        ENV = new LightningEnvironment("lmdb_data");
        ENV.Open();

        _allData = DataGen.GenerateUsers(UsersCount);
        _userRecord = DataGen.GenerateRandomBytes(RecordSizeBytes);
        
        using var tx = ENV.BeginTransaction();
        using var db = tx.OpenDatabase(configuration: new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }, closeOnDispose: true);
        
        foreach (var item in _allData)
        {
            tx.Put(db, item.Id.ToByteArray(), _userRecord);
        }
        
        tx.Commit();
    }
    
    [GlobalCleanup]
    public void GlobalCleanup() 
    {
        Console.WriteLine("Global Cleanup Begin");

        try
        {
            ENV.Dispose();
        }
        catch(Exception ex) 
        {
            Console.WriteLine(ex.ToString());
        }
        
        Console.WriteLine("Global Cleanup End");
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
                    using var tx = ENV.BeginTransaction();
                    var db = tx.OpenDatabase();
                    
                    var index = _random.Next(0, _allData.Count - 1);
                    var randomUser = _allData[index];

                    tx.Put(db, randomUser.Id.ToByteArray(), _userRecord);
                    tx.Commit();

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
                    using var tx = ENV.BeginTransaction(TransactionBeginFlags.ReadOnly);
                    var db = tx.OpenDatabase();
                    
                    var index = _random.Next(0, _allData.Count - 1);
                    var randomUser = _allData[index];
                    
                    var user = tx.Get(db, randomUser.Id.ToByteArray());
                    
                    if (user.value.AsSpan().Length == 0) 
                        throw new Exception("data is empty");

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
                    using var tx = ENV.BeginTransaction();
                    var db = tx.OpenDatabase();
                    
                    var index = _random.Next(0, _allData.Count - 1);
                    var randomUser = _allData[index];

                    tx.Put(db, randomUser.Id.ToByteArray(), _userRecord);
                    tx.Commit();

                    Interlocked.Increment(ref CurrentDbWriteCount2);
                }

                writeOps.TrySetResult();
            });
        }

        Task.WaitAll(writeOps.Task, readOps.Task);
    }
}