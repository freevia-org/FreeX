using System.Threading;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r513: the watermark decode cache held a SINGLE slot keyed on the last byte[] seen and abandoned
/// its previous bitmap whenever a different image arrived. Avalonia's Bitmap declares no finalizer
/// -- verified by reflection, Finalize is declared on System.Object alone -- so every abandoned
/// bitmap leaked its native memory for the life of the process, and alternating between two
/// watermarks leaked one more on every switch. A ConditionalWeakTable never evicts, so each image
/// keeps one bitmap for exactly as long as its bytes live. Nothing is disposed deliberately: eager
/// disposal would risk a recorded draw operation replaying a freed bitmap on the render thread.
///
/// <para>Instrument note: this lane's headless platform does not really decode -- a stream of
/// garbage constructs a Bitmap successfully, and distinct buffers can yield one shared stub. That
/// makes reference identity useless as evidence here, so allocation is the instrument instead: a
/// cache hit is a dictionary probe, while a miss allocates a stream and a bitmap. The neuter check
/// recorded with this round confirms the assertion is not vacuous.</para>
/// </summary>
public sealed class R513_WatermarkBitmapCacheTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static long MeasureRepeatCost(DocumentView view, byte[] bytes)
    {
        view.DecodeWatermarkBitmap(bytes);

        // Min-over-attempts, so an unrelated collection during one window cannot inflate the result.
        // The budget is ZERO: a ConditionalWeakTable hit allocates nothing at all, while any decode
        // allocates a stream and a bitmap. A looser budget went vacuous -- this lane's stub decode
        // is cheap enough to slip under 512 bytes.
        var best = long.MaxValue;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            view.DecodeWatermarkBitmap(bytes);
            var cost = System.GC.GetAllocatedBytesForCurrentThread() - before;
            if (cost < best)
                best = cost;
        }

        return best;
    }

    [Fact]
    public async Task Redecoding_the_same_watermark_costs_nothing()
    {
        var cost = long.MaxValue;

        await Session.Dispatch(() =>
        {
            cost = MeasureRepeatCost(new DocumentView(), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }, CancellationToken.None);

        Assert.True(cost == 0, "a repeated watermark decode allocated " + cost + " bytes");
    }

    [Fact]
    public async Task Alternating_between_two_watermarks_still_costs_nothing()
    {
        // The single-slot cache decoded afresh on every switch, stranding the bitmap it evicted.
        // Both images have to stay resident for going back and forth to be free.
        var costA = long.MaxValue;
        var costB = long.MaxValue;

        await Session.Dispatch(() =>
        {
            var view = new DocumentView();
            var a = new byte[] { 1, 2, 3, 4 };
            var b = new byte[] { 5, 6, 7, 8 };

            view.DecodeWatermarkBitmap(a);
            view.DecodeWatermarkBitmap(b);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var beforeA = System.GC.GetAllocatedBytesForCurrentThread();
                view.DecodeWatermarkBitmap(a);
                var afterA = System.GC.GetAllocatedBytesForCurrentThread();
                view.DecodeWatermarkBitmap(b);
                var afterB = System.GC.GetAllocatedBytesForCurrentThread();

                costA = System.Math.Min(costA, afterA - beforeA);
                costB = System.Math.Min(costB, afterB - afterA);
            }
        }, CancellationToken.None);

        Assert.True(costA == 0, "re-selecting the first watermark allocated " + costA + " bytes");
        Assert.True(costB == 0, "re-selecting the second watermark allocated " + costB + " bytes");
    }
}
