using FluentAssertions;
using System.IO;

namespace FreeX.App.UI.Tests;

public sealed class SelectionMarqueeLayoutPlannerPerformanceTests
{
    [Fact]
    public void CalculateVisibleRangeRect_AccumulatesBoundsWithoutMaterializedMetricLists()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SelectionMarqueeLayoutPlanner.cs"));

        source.Should().Contain("foreach (var row in viewport.RowMetrics)");
        source.Should().Contain("foreach (var column in viewport.ColMetrics)");
        source.Should().Contain("if (row.Row > range.End.Row)");
        source.Should().Contain("if (column.Col > range.End.Col)");
        source.Should().Contain("break;");
        source.Should().NotContain(".Where(");
        source.Should().NotContain(".ToList()");
        source.Should().NotContain(".Min(");
        source.Should().NotContain(".Max(");
        source.Should().NotContain("using System.Linq;");
    }

    private static string FindWorkspaceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(relativeParts);
}
