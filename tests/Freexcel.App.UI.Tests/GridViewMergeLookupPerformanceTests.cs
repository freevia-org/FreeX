using System;
using System.IO;
using FluentAssertions;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewMergeLookupPerformanceTests
{
    [Fact]
    public void RebuildMergeLookup_ReturnsBeforeAllocatingVisibleSetsWhenNoMergedRegions()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.State.cs"));
        var method = source[
            source.IndexOf("private void RebuildMergeLookup()", StringComparison.Ordinal)..
            source.IndexOf("private DispatcherTimer?", StringComparison.Ordinal)];

        method.Should().Contain("MergedRegions is not { Count: > 0 }");
        method.Should().NotContain("new HashSet<uint>");
    }

    [Fact]
    public void RebuildMergeLookup_BoundsLargeMergeWorkToVisibleMetrics()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.State.cs"));
        var rebuildMethod = source[
            source.IndexOf("private void RebuildMergeLookup()", StringComparison.Ordinal)..
            source.IndexOf("private Dictionary<uint, List<GridRange>> BuildVisibleRowMergeLookup", StringComparison.Ordinal)];
        var rowIndexMethod = source[
            source.IndexOf("private Dictionary<uint, List<GridRange>> BuildVisibleRowMergeLookup", StringComparison.Ordinal)..
            source.IndexOf("private DispatcherTimer?", StringComparison.Ordinal)];

        rebuildMethod.Should().Contain("var mergesByVisibleRow = BuildVisibleRowMergeLookup();");
        rebuildMethod.Should().Contain("mergesByVisibleRow.TryGetValue(row, out var rowMerges)");
        rebuildMethod.Should().Contain("foreach (var colMetric in Viewport.ColMetrics)");
        rebuildMethod.Should().Contain("foreach (var merge in rowMerges)");
        rebuildMethod.Should().NotContain("foreach (var merge in MergedRegions)");
        rowIndexMethod.Should().Contain("foreach (var rowMetric in Viewport!.RowMetrics)");
        rowIndexMethod.Should().Contain("foreach (var merge in MergedRegions!)");
        rowIndexMethod.Should().NotContain("r <= merge.End.Row");
        rowIndexMethod.Should().NotContain("c <= merge.End.Col");
    }

    [Fact]
    public void OnRender_StillRefreshesMergeLookupBeforeRenderingCells()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.RenderDispatch.cs"));

        source.IndexOf("RebuildMergeLookup();", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("RenderCells(dc);", StringComparison.Ordinal));
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
