using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotFieldPaneDragSourceTests
{
    [Fact]
    public void FieldPane_UsesMidpointInsertionTargetsForSameAndCrossBucketDrops()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Pivot.cs"));

        source.Should().Contain("private PivotDropTarget? ResolvePivotDropTarget(Point pointInPane)");
        source.Should().Contain("private int ResolvePivotDropIndex(PivotDropZone zone, Point pointInPane)");
        source.Should().Contain("var midpoint = origin.Y + zone.Items[index].Bounds.Height / 2;");
        source.Should().Contain("AdjustPivotDropIndex(target, drag)");
        source.Should().Contain("SourceBucket: drag.SourceBucket");
        source.Should().Contain("SourceItemIndex: drag.SourceItemIndex ?? -1");
        source.Should().Contain("target.TargetIndex - 1");
        source.Should().NotContain("target == drag.SourceBucket");
    }

    [Fact]
    public void FieldPane_TracksRenderedItemsWithTheirBucketOrder()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Pivot.cs"));

        source.Should().Contain("new PivotDropZone(container, bucketKind, body.Children.ToList(), fields)");
        source.Should().Contain("target?.Zone == zone");
        source.Should().Contain("target.Zone.Kind == PivotFieldBucket.Available");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
