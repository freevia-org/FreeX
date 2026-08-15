using System.IO;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridViewValidationCircleSourceTests
{
    [Fact]
    public void GridView_ExposesValidationCircleCellsAndRendersInvalidDataCircles()
    {
        var properties = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderDispatch = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var overlays = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var gridSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.cs"));

        properties.Should().Contain("public static readonly DependencyProperty ValidationCircleCellsProperty");
        renderDispatch.Should().Contain("RenderValidationCircles(dc);");
        renderDispatch.Should().Contain("ValidationCircleCells is { Count: > 0 }");
        overlays.Should().Contain("private void RenderValidationCircles(DrawingContext dc)");
        overlays.Should().Contain("ValidationCircleLayoutPlanner.CalculateEllipseBounds(");
        overlays.Should().Contain("new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height)");
        overlays.Should().Contain("dc.DrawEllipse(");
        overlays.Should().Contain("ValidationCirclePen,");
        gridSource.Should().Contain("private static readonly Pen ValidationCirclePen");
    }
}
