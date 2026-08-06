using FluentAssertions;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class ObjectDragCommitPlannerTests
{
    private static readonly SheetId TestSheetId = SheetId.New();
    private static readonly CellAddress StartAnchor = new(TestSheetId, 2, 3);
    private static readonly LayoutRect StartRect = new(100, 80, 200, 120);

    [Fact]
    public void PlanCommit_MoveRequiresResolvedChangedAnchor()
    {
        Plan(ObjectDragKind.Move, StartRect, null).Kind.Should().Be(ObjectDragCommitKind.Unavailable);
        Plan(ObjectDragKind.Move, StartRect, StartAnchor).Kind.Should().Be(ObjectDragCommitKind.None);

        var destination = new CellAddress(TestSheetId, 5, 7);
        var plan = Plan(ObjectDragKind.Move, StartRect, destination);

        plan.Kind.Should().Be(ObjectDragCommitKind.Move);
        plan.Anchor.Should().Be(destination);
    }

    [Fact]
    public void PlanCommit_ResizeChoosesAnchoredOrSizeOnlyAction()
    {
        var destination = new CellAddress(TestSheetId, 4, 6);
        var movedAndResized = new LayoutRect(120, 95, 180, 105);

        Plan(ObjectDragKind.ResizeNW, movedAndResized, destination)
            .Kind.Should().Be(ObjectDragCommitKind.ResizeWithAnchor);
        Plan(ObjectDragKind.ResizeNW, movedAndResized, null)
            .Kind.Should().Be(ObjectDragCommitKind.Resize);
        Plan(ObjectDragKind.ResizeSE, new LayoutRect(100, 80, 225, 140), destination)
            .Kind.Should().Be(ObjectDragCommitKind.Resize);
    }

    [Fact]
    public void PlanCommit_ResizeNoOpsBelowThresholdButCommitsFlipOnlyChange()
    {
        Plan(
            ObjectDragKind.ResizeSE,
            new LayoutRect(100.5, 80.5, 200.5, 120.5),
            StartAnchor).Kind.Should().Be(ObjectDragCommitKind.None);

        Plan(
            ObjectDragKind.ResizeSE,
            StartRect,
            StartAnchor,
            currentFlipHorizontal: true).Kind.Should().Be(ObjectDragCommitKind.Resize);
    }

    [Fact]
    public void PlanCommit_RotateCarriesCurrentAngle()
    {
        var plan = Plan(ObjectDragKind.Rotate, StartRect, null, rotationDegrees: 137.5);

        plan.Kind.Should().Be(ObjectDragCommitKind.Rotate);
        plan.RotationDegrees.Should().Be(137.5);
    }

    [Theory]
    [InlineData(ObjectDragCommitKind.Move, typeof(RepositionShapeCommand))]
    [InlineData(ObjectDragCommitKind.Resize, typeof(ResizeDrawingShapeCommand))]
    [InlineData(ObjectDragCommitKind.ResizeWithAnchor, typeof(CompositeWorkbookCommand))]
    [InlineData(ObjectDragCommitKind.Rotate, typeof(SetDrawingObjectRotationCommand))]
    public void BuildDragCommitCommand_MapsPortableActionToCoreCommand(
        ObjectDragCommitKind kind,
        Type expectedType)
    {
        var plan = new ObjectDragCommitPlan(
            kind,
            new CellAddress(TestSheetId, 7, 8),
            240,
            150,
            45,
            true,
            false);

        DrawingObjectCommandPlanner.BuildDragCommitCommand(
                TestSheetId,
                DrawingObjectTargetKind.Shape,
                Guid.NewGuid(),
                plan)
            .Should().BeOfType(expectedType);
    }

    [Theory]
    [InlineData(ObjectDragCommitKind.None)]
    [InlineData(ObjectDragCommitKind.Unavailable)]
    public void BuildDragCommitCommand_SkipsNonActions(ObjectDragCommitKind kind)
    {
        var plan = ObjectDragCommitPlan.None with { Kind = kind };

        DrawingObjectCommandPlanner.BuildDragCommitCommand(
                TestSheetId,
                DrawingObjectTargetKind.TextBox,
                Guid.NewGuid(),
                plan)
            .Should().BeNull();
    }

    [Fact]
    public void WpfAndAvaloniaRenderers_DelegateDragReleasePolicy()
    {
        var sourceRoot = RepositoryFileLocator.FindDirectory("src");
        var wpf = File.ReadAllText(Path.Combine(sourceRoot, "FreeX.App.UI", "GridView.Input.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            sourceRoot,
            "FreeX.App.Avalonia",
            "MainWindow.DrawingObjectInteraction.cs"));

        wpf.Should().Contain("GridObjectDragPlanner.PlanCommit(");
        avalonia.Should().Contain("ObjectDragPlanner.PlanCommit(");
        avalonia.Should().Contain("DrawingObjectCommandPlanner.BuildDragCommitCommand(");
        avalonia.Should().NotContain("ObjectDragPlanner.ShouldCommitMove(");
        avalonia.Should().NotContain("ObjectDragPlanner.ShouldCommitResize(");
        avalonia.Should().NotContain("DrawingObjectCommandPlanner.BuildResizeWithAnchorCommand(");
    }

    private static ObjectDragCommitPlan Plan(
        ObjectDragKind kind,
        LayoutRect currentRect,
        CellAddress? currentAnchor,
        double rotationDegrees = 0,
        bool currentFlipHorizontal = false) =>
        ObjectDragPlanner.PlanCommit(
            kind,
            StartRect,
            currentRect,
            StartAnchor,
            currentAnchor,
            width: 240,
            height: 150,
            rotationDegrees,
            startFlipHorizontal: false,
            startFlipVertical: false,
            currentFlipHorizontal,
            currentFlipVertical: false);
}
