using System.Diagnostics;
using Avalonia.Headless;

#if AVALONIA_CAPTURE_PER_TEST
[assembly: AvaloniaTestApplication(typeof(FreeX.App.Avalonia.Tests.Batch6HeadlessApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
#else
[assembly: AvaloniaTestApplication(typeof(FreeX.App.Avalonia.Tests.RibbonHeadlessApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
#endif
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FreeX.App.Avalonia.Tests;

[CollectionDefinition(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName, DisableParallelization = true)]
public sealed class AvaloniaParityCaptureCollection : ICollectionFixture<AvaloniaCaptureProcessLease>;

[CollectionDefinition(AvaloniaHeadlessCollectionOrderer.PostCaptureCollectionName, DisableParallelization = true)]
public sealed class AvaloniaPostCaptureCollection;

internal static class AvaloniaParityCaptureSession
{
    internal static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
}

public static class AvaloniaHeadlessCollectionOrderer
{
    internal const string ParityCaptureCollectionName = "AvaloniaParityCapture";
    internal const string PostCaptureCollectionName = "AvaloniaPostCapture";

}

public sealed class AvaloniaCaptureProcessLease : IDisposable
{
    private const int MaximumConcurrentProcesses = 3;
    // A lease is held for a whole assembly run, not a single test, and a capture assembly can take
    // well over a minute. The gate starts every FreeX.App.Avalonia.CaptureTests.Batch* assembly at
    // once, so with three slots the later ones legitimately queue behind two full runs. At 75s they
    // gave up and reported "Could not acquire one of 3 Avalonia capture process slots", which read
    // as a pile of unrelated capture and dialog-contract failures -- every one of them passing when
    // its project was run on its own. Wait long enough to outlast the queue instead.
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly FileStream _lease;

    public AvaloniaCaptureProcessLease()
    {
        var leaseDirectory = Path.Combine(Path.GetTempPath(), "freex-avalonia-capture-test-leases");
        Directory.CreateDirectory(leaseDirectory);

        var stopwatch = Stopwatch.StartNew();
        var firstSlot = Environment.ProcessId % MaximumConcurrentProcesses;
        while (stopwatch.Elapsed < AcquisitionTimeout)
        {
            for (var offset = 0; offset < MaximumConcurrentProcesses; offset++)
            {
                var slot = (firstSlot + offset) % MaximumConcurrentProcesses;
                try
                {
                    _lease = new FileStream(
                        Path.Combine(leaseDirectory, $"slot-{slot}.lock"),
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return;
                }
                catch (IOException)
                {
                    // Another capture testhost owns this slot.
                }
            }

            Thread.Sleep(RetryDelay);
        }

        throw new TimeoutException(
            $"Could not acquire one of {MaximumConcurrentProcesses} Avalonia capture process slots " +
            $"within {AcquisitionTimeout.TotalSeconds:0} seconds.");
    }

    public void Dispose() => _lease.Dispose();
}
