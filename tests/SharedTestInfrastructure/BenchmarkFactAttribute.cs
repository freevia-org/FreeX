using Xunit;

[AttributeUsage(AttributeTargets.Method)]
public sealed class BenchmarkFactAttribute : FactAttribute
{
    private const string EnabledEnvironmentVariable = "FREEX_RUN_BENCHMARK_TESTS";
    private const string SkipReason =
        "Benchmark test skipped by default. Set FREEX_RUN_BENCHMARK_TESTS=1 to run performance measurements.";

    public BenchmarkFactAttribute()
    {
        if (!IsEnabled())
            Skip = SkipReason;
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
