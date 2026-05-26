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

        method.IndexOf("MergedRegions is not { Count: > 0 }", StringComparison.Ordinal)
            .Should().BeLessThan(method.IndexOf("new HashSet<uint>", StringComparison.Ordinal));
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
