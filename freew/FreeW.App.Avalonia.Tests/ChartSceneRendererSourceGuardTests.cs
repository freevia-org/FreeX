using System.IO;
using System.Linq;

namespace FreeW.App.Avalonia.Tests;

public sealed class ChartSceneRendererSourceGuardTests
{
    [Fact]
    public void AvaloniaChartRenderer_ConsumesSharedSceneWithoutGeometryCalculators()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartPlan(chart)");
        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartScene(chart, settings, rect.Width, rect.Height)");
        source.Should().Contain("RenderChartScene(context, cd.Scene)");
        source.Should().NotContain("DrawChartBars(");
        source.Should().NotContain("DrawChartLines(");
        source.Should().NotContain("DrawChartScatterMarkers(");
        source.Should().NotContain("DrawChartPie(");
        source.Should().NotContain("ChartValueAxisPlan.FromSeries");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
