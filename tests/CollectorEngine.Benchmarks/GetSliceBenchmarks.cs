using BenchmarkDotNet.Attributes;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Benchmarks <c>GetSlice</c> via reflection (the parser's private method).
/// Helps estimate the contribution of <c>GetSlice</c> to the total parse cost.
/// </summary>
[MemoryDiagnoser]
public class GetSliceBenchmarks
{
    // TODO: if GetSlice is private, InternalsVisibleTo is required, or the
    // benchmark must go through the full Parse path. For now this file is a
    // stub. Use ParserPhasesBenchmarks for the actual estimate.

    [Benchmark]
    public int Noop()
    {
        return 0;
    }
}
