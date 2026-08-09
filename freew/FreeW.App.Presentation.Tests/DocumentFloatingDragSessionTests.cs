using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentFloatingDragSessionTests
{
    [Fact]
    public void MoveLifecycleProjectsPreviewCommitThresholdAndReset()
    {
        var session = new DocumentFloatingDragSession();
        var baseRect = new DocumentFloatRect(10, 20, 80, 40);

        session.Begin(
            new DocumentFloatPoint(30, 40),
            baseRect,
            DocumentFloatingHandle.Body).Should().BeTrue();
        session.Update(new DocumentFloatPoint(34, 46), false, 12)!.Rect
            .Should().Be(new DocumentFloatRect(14, 26, 80, 40));

        var commit = session.Complete(
            new DocumentFloatPoint(30.5, 40.5),
            false,
            minimumSizeDip: 12,
            minimumMoveDip: 1,
            minimumResizeChangeDip: 1)!;

        commit.HasModelChange.Should().BeFalse();
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ResizeLifecycleCarriesTransformAndReturnsModelDelta()
    {
        var session = new DocumentFloatingDragSession();
        var baseRect = new DocumentFloatRect(10, 20, 80, 40);
        session.Begin(
            new DocumentFloatPoint(90, 60),
            baseRect,
            DocumentFloatingHandle.BottomRight,
            rotationAngle: 0,
            flipH: true);

        var commit = session.Complete(
            new DocumentFloatPoint(0, 80),
            preserveAspect: false,
            minimumSizeDip: 12,
            minimumMoveDip: 1,
            minimumResizeChangeDip: 1)!;

        commit.HasModelChange.Should().BeTrue();
        commit.Rect.Should().Be(DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            baseRect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(0, 80),
            preserveAspect: false,
            minimumSizeDip: 12,
            rotationAngle: 0,
            flipH: true));
    }

    [Fact]
    public void GroupChildMoveUsesCapturedParentTransformChain()
    {
        var session = new DocumentFloatingDragSession();
        var baseRect = new DocumentFloatRect(20, 25, 40, 30);
        DocumentFloatTransform[] parents =
        [
            new(new DocumentFloatRect(0, 0, 200, 150), RotationAngle: 90),
        ];
        var pointerDown = new DocumentFloatPoint(40, 40);
        var pointer = new DocumentFloatPoint(50, 60);
        session.Begin(
            pointerDown,
            baseRect,
            DocumentFloatingHandle.Body,
            parentTransforms: parents);

        var update = session.Update(pointer, false, 12)!;

        update.IsGroupChild.Should().BeTrue();
        update.Rect.Should().Be(
            DocumentViewLayoutPlanner.BuildFloatingGroupChildMoveRectThroughGroupChain(
                baseRect,
                pointerDown,
                pointer,
                parents));
    }

    [Fact]
    public void CancelReturnsBaseRectAndClearsState()
    {
        var session = new DocumentFloatingDragSession();
        var baseRect = new DocumentFloatRect(10, 20, 80, 40);
        session.Begin(
            new DocumentFloatPoint(10, 20),
            baseRect,
            DocumentFloatingHandle.Left);

        session.Cancel(out var cancelledRect).Should().BeTrue();

        cancelledRect.Should().Be(baseRect);
        session.IsActive.Should().BeFalse();
        session.Cancel(out _).Should().BeFalse();
    }
}
