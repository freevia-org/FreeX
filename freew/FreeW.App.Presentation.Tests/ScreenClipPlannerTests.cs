using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ScreenClipPlannerTests
{
    [Fact]
    public void SelectionSession_NormalizesReverseDragAndCompletesLifecycle()
    {
        var session = new ScreenClipSelectionSession();

        session.Update(1, 2).Should().BeNull();
        session.Begin(12, 18).Bounds.Should().Be(new ScreenClipSelectionBounds(12, 18, 0, 0));
        session.IsDragging.Should().BeTrue();
        session.Update(5, 7)!.Value.Bounds.Should().Be(new ScreenClipSelectionBounds(5, 7, 7, 11));
        session.Complete(4, 6)!.Value.Bounds.Should().Be(new ScreenClipSelectionBounds(4, 6, 8, 12));
        session.IsDragging.Should().BeFalse();
        session.Complete(0, 0).Should().BeNull();
    }

    [Fact]
    public void SelectionSession_CancelClearsDragAndRejectsNonFinitePoints()
    {
        var session = new ScreenClipSelectionSession();
        session.Begin(1, 2);

        session.Cancel();

        session.IsDragging.Should().BeFalse();
        session.Update(3, 4).Should().BeNull();
        var act = () => session.Begin(double.NaN, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OverlayScaleMappingRetainsOriginIndependentMidpointRounding()
    {
        ScreenClipPlanner.BuildPhysicalSelection(
                0.5,
                0.5,
                2.5,
                2.5,
                overlayOriginX: 1,
                overlayOriginY: 1,
                renderScale: 1)
            .Should().Be(new ScreenPixelRect(1, 1, 2, 2));
    }

    [Fact]
    public void MappedEndpointsAreNormalizedAndRoundedInPhysicalPixels()
    {
        ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
                310.6,
                420.4,
                100.4,
                200.6)
            .Should().Be(new ScreenPixelRect(100, 201, 210, 220));
    }

    [Fact]
    public void RoundedEmptyMappedSelectionIsRejected()
    {
        ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
                10.1,
                20,
                10.4,
                30)
            .Should().BeNull();
    }

    [Fact]
    public void NonFiniteMappedEndpointIsRejected()
    {
        var act = () => ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
            double.NaN,
            0,
            10,
            10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(96, 48, 72, 36)]
    [InlineData(1200, 600, 400, 200)]
    [InlineData(1600, 900, 400, 225)]
    public void ImageInsertionPlanOwnsPngFormatAndAspectPreservingDisplaySize(
        int pixelWidth,
        int pixelHeight,
        double expectedWidthPt,
        double expectedHeightPt)
    {
        var plan = ScreenClipPlanner.BuildImageInsertionPlan(pixelWidth, pixelHeight);

        plan.Format.Should().Be(ImageFormat.Png);
        plan.WidthPt.Should().BeApproximately(expectedWidthPt, 0.001);
        plan.HeightPt.Should().BeApproximately(expectedHeightPt, 0.001);
        plan.OriginalPixelWidth.Should().Be(pixelWidth);
        plan.OriginalPixelHeight.Should().Be(pixelHeight);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void ImageInsertionPlanRejectsEmptyPixelDimensions(int width, int height)
    {
        var act = () => ScreenClipPlanner.BuildImageInsertionPlan(width, height);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
