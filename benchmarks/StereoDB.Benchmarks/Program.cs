using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using StereoDB.Benchmarks.Benchmarks;

//BenchmarkRunner.Run<StereoDbBenchmark>(new DebugInProcessConfig());
BenchmarkRunner.Run<StereoDbBenchmark>();