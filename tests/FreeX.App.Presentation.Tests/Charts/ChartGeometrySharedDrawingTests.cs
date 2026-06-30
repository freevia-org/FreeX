using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartGeometrySharedDrawingTests
{
    [Fact]
    public void ChartGeometry_UsesSharedLayoutPointAndRect()
    {
        var plot = new PlotRect(10, 20, 300, 180);
        var arc = new LayoutArc(new LayoutPoint(100, 120), 80, 30, 15, 90);

        typeof(PlotRect).GetMethod(nameof(PlotRect.ToRect))!.ReturnType.Should().Be(typeof(LayoutRect));
        typeof(LayoutArc).GetProperty(nameof(LayoutArc.Center))!.PropertyType.Should().Be(typeof(LayoutPoint));
        typeof(LayoutPoint).Namespace.Should().Be("Free.Shared.Drawing");
        typeof(LayoutRect).Assembly.FullName.Should().Be(typeof(LayoutPoint).Assembly.FullName);

        plot.ToRect().Should().Be(new LayoutRect(10, 20, 300, 180));
        arc.Center.Should().Be(new LayoutPoint(100, 120));
    }

    [Fact]
    public void ChartGeometrySources_KeepOnlyChartSpecificTypesLocal()
    {
        var sharedRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("shared", "Free.Shared.Drawing");
        var chartsRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.App.Presentation", "Charts");

        File.Exists(Path.Combine(sharedRoot, "Geometry.cs"))
            .Should()
            .BeTrue("LayoutPoint and LayoutRect should remain owned by Free.Shared.Drawing");

        var source = File.ReadAllText(Path.Combine(chartsRoot, "Geometry.cs"));

        source.Should().Contain("using Free.Shared.Drawing;");
        source.Should().Contain("public readonly record struct PlotRect");
        source.Should().Contain("public readonly record struct LayoutArc");
        source.Should().NotContain("record struct LayoutPoint");
        source.Should().NotContain("record struct LayoutRect");
    }
}
