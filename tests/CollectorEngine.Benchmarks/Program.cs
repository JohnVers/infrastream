using BenchmarkDotNet.Running;

// Run all benchmarks in the assembly.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
