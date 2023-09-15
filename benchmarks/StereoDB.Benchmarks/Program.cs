using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using StereoDB.Benchmarks.Benchmarks;

//BenchmarkRunner.Run<StereoDbBenchmark>(new DebugInProcessConfig());
BenchmarkRunner.Run<StereoDbBenchmark>();

//BenchmarkRunner.Run<LMDBBenchmark>();
//BenchmarkRunner.Run<LMDBBenchmark>(new DebugInProcessConfig());

//BenchmarkRunner.Run<DotnetBenchmark>(args: args);