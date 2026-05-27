using FluentAssertions;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class SelectionMarqueeLayoutPlannerPerformanceTests
{
    [Fact]
    public void CalculateVisibleRangeRect_AccumulatesBoundsWithoutMaterializedMetricLists()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "SelectionMarqueeLayoutPlanner.cs"));

        source.Should().Contain("foreach (var row in viewport.RowMetrics)");
        source.Should().Contain("foreach (var column in viewport.ColMetrics)");
        source.Should().NotContain(".Where(");
        source.Should().NotContain(".ToList()");
        source.Should().NotContain(".Min(");
        source.Should().NotContain(".Max(");
        source.Should().NotContain("using System.Linq;");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
