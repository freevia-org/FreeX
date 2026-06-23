using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class RulerInteractionTests
{
    [Fact]
    public void TryMetrics_MapsContentPointsToHorizontalRulerCoordinates()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72
        };

        var metrics = Ruler.TryMetrics(new Size(1000, 16), page, zoom: 1)!;

        metrics.ContentStart.Should().BeApproximately(188, 0.1);
        metrics.ContentEnd.Should().BeApproximately(812, 0.1);
        metrics.ContentPtToX(72).Should().BeApproximately(284, 0.1);
        metrics.PointToContentPt(metrics.ContentPtToX(144)).Should().BeApproximately(144, 0.1);
    }

    [Fact]
    public void MoveOrAddLeftTabStop_AddsSnappedSortedStop()
    {
        var stops = Ruler.MoveOrAddLeftTabStop(
            [new TabStop(144, TabStopAlignment.Right, TabLeader.Dots)],
            index: -1,
            positionPt: 71);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(144, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void MoveOrAddTabStop_AddsSelectedAlignment_ForNewStop()
    {
        var stops = Ruler.MoveOrAddTabStop(
            [new TabStop(144, TabStopAlignment.Right, TabLeader.Dots)],
            index: -1,
            positionPt: 109,
            TabStopAlignment.Center);

        stops.Should().Equal(
            new TabStop(108, TabStopAlignment.Center),
            new TabStop(144, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void MoveOrAddLeftTabStop_MovesExistingStop_AndPreservesAlignmentLeader()
    {
        var stops = Ruler.MoveOrAddLeftTabStop(
            [new TabStop(72), new TabStop(144, TabStopAlignment.Decimal, TabLeader.Underline)],
            index: 1,
            positionPt: 218);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(216, TabStopAlignment.Decimal, TabLeader.Underline));
    }

    [Fact]
    public void MoveOrAddTabStop_MovesExistingStop_AndIgnoresSelectedAlignment()
    {
        var stops = Ruler.MoveOrAddTabStop(
            [new TabStop(72), new TabStop(144, TabStopAlignment.Right, TabLeader.Dashes)],
            index: 1,
            positionPt: 181,
            TabStopAlignment.Decimal);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(180, TabStopAlignment.Right, TabLeader.Dashes));
    }

    [Fact]
    public void RemoveTabStop_RemovesRequestedStop()
    {
        var stops = Ruler.RemoveTabStop(
            [
                new TabStop(72),
                new TabStop(144, TabStopAlignment.Decimal, TabLeader.Underline),
                new TabStop(216)
            ],
            index: 1);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(216));
    }

    [Fact]
    public void RemoveTabStop_IgnoresInvalidIndex()
    {
        var start = new[]
        {
            new TabStop(72),
            new TabStop(144, TabStopAlignment.Right, TabLeader.Dashes)
        };

        Ruler.RemoveTabStop(start, index: -1).Should().Equal(start);
        Ruler.RemoveTabStop(start, index: 2).Should().Equal(start);
    }

    [Fact]
    public void IsTabStopRemovalDrop_RequiresClearVerticalDropOutsideRuler()
    {
        var size = new Size(1000, 16);

        Ruler.IsTabStopRemovalDrop(new Point(200, 8), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, -6), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, 22), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, -8), size).Should().BeTrue();
        Ruler.IsTabStopRemovalDrop(new Point(200, 24), size).Should().BeTrue();
    }

    [Fact]
    public void IndentsForDrag_UpdatesTheRequestedIndentOnly()
    {
        var start = ParagraphFormatting.Default with
        {
            IndentLeftPt = 36,
            IndentRightPt = 18,
            FirstLineIndentPt = -12
        };

        Ruler.IndentsForDrag(start, Ruler.DragKind.LeftIndent, 74).Should().Be(
            start with { IndentLeftPt = 72 });

        Ruler.IndentsForDrag(start, Ruler.DragKind.FirstLineIndent, 60).Should().Be(
            start with { FirstLineIndentPt = 24 });

        Ruler.IndentsForDrag(start, Ruler.DragKind.RightIndent, 30).Should().Be(
            start with { IndentRightPt = 30 });
    }

    // ── Vertical ruler metrics ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryVerticalMetrics_ComputesBoundaryYsFromRenderVerticalAnchor()
    {
        // RenderVertical anchors pageY = 0; top-margin boundary is at topDip, bottom boundary is
        // at pageHeightDip - bottomDip. PointsToDip = pt * (96/72).
        var page = new PageSettings
        {
            HeightPt    = 792,   // 11 inches
            MarginTopPt = 72,    // 1 inch  →  96 DIP at zoom 1
            MarginBottomPt = 72  // 1 inch  →  96 DIP
        };

        var vm = Ruler.TryVerticalMetrics(page, zoom: 1)!;

        const double dipPerPt = 96.0 / 72.0;
        var expectedTop    = 72  * dipPerPt;           // 96
        var expectedBottom = 792 * dipPerPt - expectedTop;  // 1056 - 96 = 960

        vm.TopBoundaryY.Should().BeApproximately(expectedTop, 0.001);
        vm.BottomBoundaryY.Should().BeApproximately(expectedBottom, 0.001);
        vm.PageHeightPt.Should().Be(792);
    }

    [Fact]
    public void TryVerticalMetrics_ScalesBoundariesByZoom()
    {
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };

        var vm = Ruler.TryVerticalMetrics(page, zoom: 1.5)!;

        const double dipPerPt = 96.0 / 72.0;
        var expectedTop = 72 * dipPerPt * 1.5;
        vm.TopBoundaryY.Should().BeApproximately(expectedTop, 0.001);
        vm.BottomBoundaryY.Should().BeApproximately(792 * dipPerPt * 1.5 - expectedTop, 0.001);
    }

    [Fact]
    public void TryVerticalMetrics_ReturnsNull_ForZeroZoom()
    {
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };
        Ruler.TryVerticalMetrics(page, zoom: 0).Should().BeNull();
        Ruler.TryVerticalMetrics(page, zoom: -1).Should().BeNull();
    }

    [Fact]
    public void DipDeltaToPointsDelta_IsExactInverseOfRenderConversion()
    {
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };
        var vm = Ruler.TryVerticalMetrics(page, zoom: 1.25)!;

        // A 40-pt change should round-trip through DIP and back to exactly 40 pt.
        const double dipPerPt = 96.0 / 72.0;
        var dipDelta = 40.0 * dipPerPt * 1.25;  // what a 40-pt drag looks like in DIP at zoom 1.25
        vm.DipDeltaToPointsDelta(dipDelta).Should().BeApproximately(40.0, 0.0001);
    }

    // ── Vertical hit-test ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerticalHitTest_ReturnsTopMargin_WhenWithinHitRadius()
    {
        var vm = new Ruler.VerticalMetrics(TopBoundaryY: 96, BottomBoundaryY: 960, PageHeightPt: 792, Zoom: 1, ScrollOffsetDip: 0);

        Ruler.VerticalHitTest(96, vm).Should().Be(Ruler.DragKind.TopMargin);
        Ruler.VerticalHitTest(96 + 7, vm).Should().Be(Ruler.DragKind.TopMargin);   // edge of radius
        Ruler.VerticalHitTest(96 - 7, vm).Should().Be(Ruler.DragKind.TopMargin);
        Ruler.VerticalHitTest(96 + 7.1, vm).Should().Be(Ruler.DragKind.None);       // just outside
    }

    [Fact]
    public void VerticalHitTest_ReturnsBottomMargin_WhenWithinHitRadius()
    {
        var vm = new Ruler.VerticalMetrics(TopBoundaryY: 96, BottomBoundaryY: 960, PageHeightPt: 792, Zoom: 1, ScrollOffsetDip: 0);

        Ruler.VerticalHitTest(960, vm).Should().Be(Ruler.DragKind.BottomMargin);
        Ruler.VerticalHitTest(960 + 7, vm).Should().Be(Ruler.DragKind.BottomMargin);
        Ruler.VerticalHitTest(960 - 7, vm).Should().Be(Ruler.DragKind.BottomMargin);
        Ruler.VerticalHitTest(960 - 7.1, vm).Should().Be(Ruler.DragKind.None);
    }

    [Fact]
    public void VerticalHitTest_ReturnsNone_WhenFarFromBothBoundaries()
    {
        var vm = new Ruler.VerticalMetrics(TopBoundaryY: 96, BottomBoundaryY: 960, PageHeightPt: 792, Zoom: 1, ScrollOffsetDip: 0);

        Ruler.VerticalHitTest(500, vm).Should().Be(Ruler.DragKind.None);
        Ruler.VerticalHitTest(0, vm).Should().Be(Ruler.DragKind.None);
    }

    // ── Vertical margin clamping ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ClampVerticalMargin_ClampsToNonNegative()
    {
        // Dragging a margin below 0 should snap to 0.
        Ruler.ClampVerticalMargin(-10, otherMarginPt: 72, pageHeightPt: 792).Should().Be(0);
        Ruler.ClampVerticalMargin(0, otherMarginPt: 72, pageHeightPt: 792).Should().Be(0);
    }

    [Fact]
    public void ClampVerticalMargin_KeepsAtLeastOnePointOfContent()
    {
        // top=750, bottom=72 → top + bottom = 822 > 792. Should clamp top to 792 - 72 - 1 = 719.
        Ruler.ClampVerticalMargin(750, otherMarginPt: 72, pageHeightPt: 792).Should().BeApproximately(719, 0.001);
    }

    [Fact]
    public void ClampVerticalMargin_AllowsNormalMargin()
    {
        // 108 pt top, 72 pt bottom on a 792 pt page: well within limits.
        Ruler.ClampVerticalMargin(108, otherMarginPt: 72, pageHeightPt: 792).Should().BeApproximately(108, 0.001);
    }

    [Fact]
    public void ClampVerticalMargin_BothMarginsZeroIsValid()
    {
        // 0 + 0 = 0, leaving the full page as content.
        Ruler.ClampVerticalMargin(0, otherMarginPt: 0, pageHeightPt: 792).Should().Be(0);
    }

    // ── Scroll-adjusted vertical metrics ─────────────────────────────────────────────────────────

    [Fact]
    public void TryVerticalMetrics_OffsetsBoundariesByScrollOffset()
    {
        // At scroll offset 0, boundaries are at topDip and pageHeightDip - bottomDip.
        // With a positive scroll offset (user scrolled down), the page top moves upward in the ruler,
        // so both boundaries shift UP by the same scroll amount.
        var page = new PageSettings
        {
            HeightPt       = 792,  // 11 in → 1056 DIP at zoom 1
            MarginTopPt    = 72,   // 1 in  →   96 DIP at zoom 1
            MarginBottomPt = 72
        };

        const double scrollDip = 200.0; // 200 DIP of scroll

        var vm = Ruler.TryVerticalMetrics(page, zoom: 1, scrollOffsetDip: scrollDip)!;

        // topDip = 72 * (96/72) = 96; expectedTop = 96 - 200 = -104
        var expectedTop    = -104.0;
        // pageHeightDip = 792*(96/72) = 1056; bottomDip=96; expectedBottom = 1056-96-200 = 760
        var expectedBottom =  760.0;

        vm.TopBoundaryY.Should().BeApproximately(expectedTop,    0.001);
        vm.BottomBoundaryY.Should().BeApproximately(expectedBottom, 0.001);
        vm.ScrollOffsetDip.Should().Be(scrollDip);
    }

    [Fact]
    public void TryVerticalMetrics_ScrollOffset_ScalesWithZoom()
    {
        // The scroll offset is in device-independent pixels (already screen-space), so it is NOT
        // multiplied by zoom — it subtracts directly from the page-top anchor in ruler coordinates.
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };

        const double zoom      = 1.5;
        const double scrollDip = 100.0;
        const double dipPerPt  = 96.0 / 72.0;

        var vm = Ruler.TryVerticalMetrics(page, zoom: zoom, scrollOffsetDip: scrollDip)!;

        // Top boundary: pageY + topDip*zoom = -scrollDip + 72*(96/72)*1.5 = -100 + 144 = 44
        var expectedTop = 72 * dipPerPt * zoom - scrollDip;
        vm.TopBoundaryY.Should().BeApproximately(expectedTop, 0.001);
    }

    [Fact]
    public void TryVerticalMetrics_ZeroScrollOffset_MatchesNoScrollOverload()
    {
        // Calling with scrollOffsetDip=0 must produce the same result as the default overload.
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };

        var vmDefault = Ruler.TryVerticalMetrics(page, zoom: 1.25)!;
        var vmExplicit = Ruler.TryVerticalMetrics(page, zoom: 1.25, scrollOffsetDip: 0)!;

        vmExplicit.TopBoundaryY.Should().BeApproximately(vmDefault.TopBoundaryY, 0.0001);
        vmExplicit.BottomBoundaryY.Should().BeApproximately(vmDefault.BottomBoundaryY, 0.0001);
    }

    [Fact]
    public void VerticalHitTest_StillLandsOnBoundary_AfterSimulatedScroll()
    {
        // Simulate 200 DIP of vertical scroll. The top boundary moves to (96 - 200) = -104 DIP,
        // which is off-screen; the bottom boundary moves to (960 - 200) = 760 DIP.
        // Hit-testing at the scroll-shifted boundary Y should still return the correct drag kind,
        // confirming that RenderVertical and TryVerticalMetrics share the same anchor.
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };

        const double scrollDip = 200.0;

        var vm = Ruler.TryVerticalMetrics(page, zoom: 1, scrollOffsetDip: scrollDip)!;

        // Bottom boundary is now at 960 - 200 = 760; hit-test should find BottomMargin there.
        Ruler.VerticalHitTest(vm.BottomBoundaryY, vm).Should().Be(Ruler.DragKind.BottomMargin);
        // Top boundary is off-screen (-104); hit-test should still report TopMargin if asked.
        Ruler.VerticalHitTest(vm.TopBoundaryY, vm).Should().Be(Ruler.DragKind.TopMargin);
        // Mid-page is not near either boundary.
        Ruler.VerticalHitTest(300, vm).Should().Be(Ruler.DragKind.None);
    }

    [Fact]
    public void TryVerticalMetrics_ScrollOffsetDip_IsReturnedOnRecord()
    {
        var page = new PageSettings { HeightPt = 792, MarginTopPt = 72, MarginBottomPt = 72 };
        var vm   = Ruler.TryVerticalMetrics(page, zoom: 1, scrollOffsetDip: 123.4)!;
        vm.ScrollOffsetDip.Should().Be(123.4);
    }
}
