using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using StereoDB.Benchmarks.Benchmarks;

// var b = new StereoDbBenchmark
// {
//     Concurrency = Concurrency.CasRwSpinLock
// };
// b.GlobalSetup();
// b.Basic_Read_Modify_Write_Tx();

// BenchmarkRunner.Run<StereoDbBenchmark>(new DebugInProcessConfig());
BenchmarkRunner.Run<StereoDbBenchmark>();

//BenchmarkRunner.Run<LMDBBenchmark>();
//BenchmarkRunner.Run<LMDBBenchmark>(new DebugInProcessConfig());

//BenchmarkRunner.Run<DotnetBenchmark>(args: args);