using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CommentNavigationPlannerTests
{
    [BenchmarkFact]
    public void Benchmark_FormatCellCommentPreview_NoCommentCells_ReportsTiming()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>();
        const int iterations = 200_000;

        for (var index = 0; index < 1_000; index++)
        {
            var address = new CellAddress(sheetId, (uint)(index + 1), 1);
            CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address)
                .Should()
                .BeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        string? preview = null;
        for (var index = 0; index < iterations; index++)
        {
            var address = new CellAddress(sheetId, (uint)((index % 10_000) + 1), 1);
            preview = CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address);
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Console.WriteLine(
            $"PERF COMMENT_PREVIEW_EMPTY steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");

        preview.Should().BeNull();
    }
}
