using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class ChartSceneRendererSourceGuardTests
{
    [Fact]
    public void WpfChartRenderer_ConsumesSharedSceneWithoutGeometryCalculators()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartPlan(chart)");
        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartScene(chart, settings, widthPx, heightPx)");
        source.Should().Contain("BuildChartSceneCanvas(scene)");
        source.Should().Contain("RenderChartScene(canvas, scene)");
        source.Should().NotContain("DrawBarChart(");
        source.Should().NotContain("DrawLineChart(");
        source.Should().NotContain("DrawScatterChart(");
        source.Should().NotContain("DrawPieChart(");
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
