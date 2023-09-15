using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace StereoDB.Benchmarks.Benchmarks;

//[MemoryDiagnoser]
[AllStatisticsColumn] // RankColumn, MinColumn, MaxColumn, Q1Column, Q3Column, AllStatisticsColumn 
public class DotnetBenchmark
{
    private const int Iterations = 1000;
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int Add(int a, int b) => a + b;
    
    [Benchmark(OperationsPerInvoke = Iterations)]
    public void SimpleMethod()
    {
        var k = 0;
        
        for (int i = 0; i < Iterations; i++)
        {
            k = Add(i, i);
        }
    }

    private ValueTask<int> AddValueAsync(int a, int b) => ValueTask.FromResult(a + b);
    
    [Benchmark(OperationsPerInvoke = Iterations)]
    public async ValueTask SimpleMethodValueAsync()
    {
        var k = 1;
        for (int i = 0; i < Iterations; i++)
        {
            k = await AddValueAsync(i, i);
        }
    }
    
    private Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
    
    [Benchmark(OperationsPerInvoke = Iterations)]
    public async Task SimpleMethodAsync()
    {
        var k = 0;
        
        for (int i = 0; i < Iterations; i++)
        {
            k = await AddAsync(i, i);
        }
    }
    
    [Benchmark(OperationsPerInvoke = Iterations)]
    public async Task SimpleMethodAsyncYield()
    {
        var k = 0;
        
        for (int i = 0; i < Iterations; i++)
        {
            k = await AddAsyncYield(i, i);
        }
    }
    
    private async Task<int> AddAsyncYield(int a, int b)
    {
        await Task.Yield();
        return a + b;
    }
    
    [Benchmark(OperationsPerInvoke = Iterations)]
    public async Task SimpleMethodAsync2()
    {
        var k = 0;
        
        for (int i = 0; i < Iterations; i++)
        {
            k = await AddAsync2(i, i);
        }
    }
    
    private async Task<int> AddAsync2(int a, int b)
    {
        await Task.Yield();
        return await Task.Run(() => a + b);
    }
}