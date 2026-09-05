using Xunit;

/// <summary>
/// Measures how many managed bytes a delegate allocates on the calling thread, repeating the
/// measurement so that a one-off allocation charged to the runner thread cannot fail the test.
/// </summary>
/// <remarks>
/// <para>
/// A single <see cref="GC.GetAllocatedBytesForCurrentThread"/> window is not a safe basis for an
/// exact-zero assertion. The counter itself is exact - a collection triggered by another thread
/// corrects this thread's allocation context rather than perturbing it, which was verified over
/// hundreds of trials under continuous GC pressure - but the window still charges the measuring
/// thread for anything the runtime happens to do on it while the loop runs (tiered re-compilation
/// and on-stack replacement of the measured loop, coverage or profiling instrumentation on a CI
/// worker, and so on). That produced a spurious 2,776-byte reading on the provably
/// allocation-free <c>ConditionalFormat.Contains</c> hot path on GitHub CI, against code that
/// would have to allocate at least 24 bytes per iteration to be a real regression.
/// </para>
/// <para>
/// Such disturbances are one-off; a real regression allocates on every pass. So the body is run
/// several times and the <em>lowest</em> reading is the one asserted against, which removes the
/// noise without weakening the bound: the default budget of one byte per measured operation is
/// still more than an order of magnitude below the cost of a single allocation per operation.
/// </para>
/// </remarks>
public static class AllocationProbe
{
    private const int DefaultAttempts = 5;

    /// <summary>
    /// Smallest object the runtime can allocate on a 64-bit heap. A regression that allocates once
    /// per measured operation therefore costs at least this many bytes per operation, which is what
    /// the per-operation budget below is calibrated against.
    /// </summary>
    public const long MinimumObjectSizeBytes = 24;

    /// <summary>
    /// Runs <paramref name="body"/> several times and returns the lowest number of bytes it
    /// allocated on the calling thread.
    /// </summary>
    /// <param name="body">
    /// The code to measure. It is run <paramref name="warmupIterations"/> times before measuring and
    /// once per attempt, so it must be repeatable and free of observable side effects.
    /// </param>
    /// <param name="attempts">How many measurements to take. The lowest one is reported.</param>
    /// <param name="warmupIterations">
    /// How many times to run <paramref name="body"/> first, so tiered JIT compilation of the measured
    /// path is not charged to the measurement on a cold worker.
    /// </param>
    public static AllocationReading Measure(Action body, int attempts = DefaultAttempts, int warmupIterations = 1)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupIterations);

        for (var warmup = 0; warmup < warmupIterations; warmup++)
            body();

        var readings = new long[attempts];
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            body();
            readings[attempt] = GC.GetAllocatedBytesForCurrentThread() - before;

            // Nothing can beat a clean reading, so stop as soon as one shows up.
            if (readings[attempt] == 0)
                return new AllocationReading(0, readings[..(attempt + 1)]);
        }

        return new AllocationReading(readings.Min(), readings);
    }

    /// <summary>
    /// Asserts that <paramref name="body"/> does not allocate, allowing at most one byte for each of
    /// the <paramref name="operations"/> it performs so that a single disturbed window cannot fail
    /// the run. That budget is <see cref="MinimumObjectSizeBytes"/> times smaller than the cost of a
    /// regression that allocates once per operation.
    /// </summary>
    /// <param name="operations">
    /// How many operations <paramref name="body"/> performs - the iteration count of the loop it
    /// runs, multiplied by the number of measured calls per iteration.
    /// </param>
    public static AllocationReading ShouldNotAllocate(
        Action body,
        int operations,
        string because,
        int attempts = DefaultAttempts,
        int warmupIterations = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(operations, 1);

        var reading = Measure(body, attempts, warmupIterations);
        Assert.True(
            reading.Bytes <= operations,
            $"Expected {operations:N0} allocation-free operations, because {because}. " +
            $"The lowest of {reading.Readings.Count} measurements allocated {reading.Bytes:N0} bytes " +
            $"({(double)reading.Bytes / operations:F3} per operation; one allocation per operation " +
            $"would cost at least {MinimumObjectSizeBytes}). " +
            $"Measurements: {string.Join(", ", reading.Readings)}.");
        return reading;
    }

    /// <summary>
    /// Asserts that <paramref name="body"/> allocates no more than <paramref name="maxBytes"/> on the
    /// calling thread.
    /// </summary>
    public static AllocationReading ShouldAllocateAtMost(
        Action body,
        long maxBytes,
        string because,
        int attempts = DefaultAttempts,
        int warmupIterations = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var reading = Measure(body, attempts, warmupIterations);
        Assert.True(
            reading.Bytes <= maxBytes,
            $"Expected at most {maxBytes:N0} allocated bytes, because {because}. " +
            $"The lowest of {reading.Readings.Count} measurements allocated {reading.Bytes:N0} bytes. " +
            $"Measurements: {string.Join(", ", reading.Readings)}.");
        return reading;
    }
}

/// <summary>The outcome of an <see cref="AllocationProbe"/> measurement.</summary>
/// <param name="Bytes">The lowest number of bytes any single measurement attributed to the body.</param>
/// <param name="Readings">Every measurement taken, in order, for reporting.</param>
public readonly record struct AllocationReading(long Bytes, IReadOnlyList<long> Readings);
