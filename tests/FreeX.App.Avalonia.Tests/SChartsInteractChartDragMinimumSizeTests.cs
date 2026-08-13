using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class SChartsInteractChartDragMinimumSizeTests
{
    [Fact]
    public void ChartDragPreview_UsesSharedMinimumAndResizeClamp()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs"));
        var start = source.IndexOf("private void WireChartDragMoveRelease(", StringComparison.Ordinal);
        var end = source.IndexOf("private void CommitChartDrag(", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);

        var body = source[start..end];
        body.Should().Contain("ObjectDragPlanner.CalculateDragTransform(");
        body.Should().Contain("Math.Min(minimumChartWidth, minimumChartHeight)");
        body.Should().Contain("ObjectDragPlanner.ClampResizeToMinimums(");
        body.Should().Contain("container.Width = rect.Width");
        body.Should().Contain("container.Height = rect.Height");
    }

    [Fact]
    public void CommitChartDrag_UsesSharedWpfMinimumBounds()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs"));
        var start = source.IndexOf("private void CommitChartDrag(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);

        var body = source[start..];
        body.Should().Contain(
            "DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Chart)");
        body.Should().Contain(
            "DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Chart)");
        body.Should().NotContain("Math.Max(ObjectDragPlanner.MinimumObjectSize, container.Width / zoomFactor)");
        body.Should().NotContain("Math.Max(ObjectDragPlanner.MinimumObjectSize, container.Height / zoomFactor)");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
