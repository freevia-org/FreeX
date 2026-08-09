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

    private static string RepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreeW.slnx")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
