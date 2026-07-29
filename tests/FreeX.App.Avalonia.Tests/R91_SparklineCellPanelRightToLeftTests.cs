using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R91-meta-2: round 89 made <see cref="FreeX.App.Presentation.Sparklines.SparklineLayoutEngine"/>
/// RTL-aware and round 90 wired the sparkline group's "Plot Data Right-to-Left" flag into the WPF
/// GridView renderer, but <see cref="SparklineCellPanel"/> -- the separate Avalonia (Linux/macOS)
/// in-cell renderer -- kept calling the RTL-less overloads and so silently ignored
/// <see cref="SparklineModel.RightToLeft"/>. These tests go through the real product entry point
/// (constructing a <see cref="SparklineCellPanel"/> and arranging it, exactly as the Avalonia grid
/// does for every rendered cell) rather than calling <c>SparklineLayoutEngine</c> directly, so a
/// regression in the panel's own call sites -- not just the engine -- would be caught.
/// Uses the Avalonia headless platform (via the shared <see cref="RibbonHeadlessApp"/> session)
/// because Line/Rectangle/Ellipse shapes need <c>IPlatformRenderInterface</c> when arranged.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R91_SparklineCellPanelRightToLeftTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task LineSparkline_RightToLeftTrue_MirrorsSegmentXPositions()
    {
        await Session.Dispatch(() =>
        {
            var values = new double[] { 0, 10, 0, 10 };

            var ltr = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Line, RightToLeft = false });
            ltr.Measure(new Size(80, 24));
            ltr.Arrange(new Rect(0, 0, 80, 24));

            var rtl = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Line, RightToLeft = true });
            rtl.Measure(new Size(80, 24));
            rtl.Arrange(new Rect(0, 0, 80, 24));

            var ltrFirstSegment = ltr.Children.OfType<Line>().First();
            var rtlFirstSegment = rtl.Children.OfType<Line>().First();

            // Before the fix, RightToLeft=true produced byte-identical geometry to false because
            // SparklineCellPanel never passed the flag into the layout engine.
            rtlFirstSegment.StartPoint.X.Should().NotBe(ltrFirstSegment.StartPoint.X,
                "RightToLeft=true must mirror the sparkline's geometry, not match the left-to-right layout");

            // The RTL panel's first segment should start near the right edge (mirrored), matching
            // the LTR panel's LAST segment start X (segments themselves are still emitted in the
            // same left-to-right list order; only each point's X position is mirrored).
            var ltrLastSegment = ltr.Children.OfType<Line>().Last();
            rtlFirstSegment.StartPoint.X.Should().BeApproximately(ltrLastSegment.EndPoint.X, 0.5);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LineMarkers_RightToLeftTrue_MirrorsMarkerXPositions()
    {
        await Session.Dispatch(() =>
        {
            var values = new double[] { 1, 2, 3 };

            var ltr = new SparklineCellPanel(values, new SparklineModel
            {
                Kind = SparklineKind.Line,
                RightToLeft = false,
                ShowFirstPoint = true,
            });
            ltr.Measure(new Size(90, 24));
            ltr.Arrange(new Rect(0, 0, 90, 24));

            var rtl = new SparklineCellPanel(values, new SparklineModel
            {
                Kind = SparklineKind.Line,
                RightToLeft = true,
                ShowFirstPoint = true,
            });
            rtl.Measure(new Size(90, 24));
            rtl.Arrange(new Rect(0, 0, 90, 24));

            var ltrMarker = ltr.Children.OfType<Ellipse>().Single();
            var rtlMarker = rtl.Children.OfType<Ellipse>().Single();

            var ltrCenterX = ltrMarker.Margin.Left + 2.0; // MarkerRadius offset baked into Margin.Left.
            var rtlCenterX = rtlMarker.Margin.Left + 2.0;

            // Before the fix, BuildLineMarkers called the RTL-less GetLinePoints overload, so the
            // "first point" marker always sat at the left edge regardless of RightToLeft.
            rtlCenterX.Should().NotBeApproximately(ltrCenterX, 0.5,
                "the first-point marker must move to the mirrored (right-hand) position when RightToLeft is set");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColumnSparkline_RightToLeftTrue_MirrorsBarOrder()
    {
        await Session.Dispatch(() =>
        {
            var values = new double[] { 1, 2, 3 };

            var ltr = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Column, RightToLeft = false });
            ltr.Measure(new Size(60, 24));
            ltr.Arrange(new Rect(0, 0, 60, 24));

            var rtl = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Column, RightToLeft = true });
            rtl.Measure(new Size(60, 24));
            rtl.Arrange(new Rect(0, 0, 60, 24));

            var ltrBars = ltr.Children.OfType<Rectangle>().ToList();
            var rtlBars = rtl.Children.OfType<Rectangle>().ToList();

            ltrBars.Should().HaveCount(3);
            rtlBars.Should().HaveCount(3);

            // Before the fix, RightToLeft=true produced the identical bar order/positions as false
            // because BuildColumns never passed the flag into CalculateColumnLayout.
            rtlBars[0].Margin.Left.Should().BeApproximately(ltrBars[2].Margin.Left, 0.5,
                "the first value's bar must land in the rightmost slot when RightToLeft is set");
            rtlBars[0].Margin.Left.Should().NotBeApproximately(ltrBars[0].Margin.Left, 0.5);
        }, CancellationToken.None);
    }

    // ── No-regression siblings: RightToLeft=false (the default) must be unaffected ──────────────

    [Fact]
    public async Task LineSparkline_RightToLeftFalse_MatchesPreFixLayout()
    {
        await Session.Dispatch(() =>
        {
            var values = new double[] { 3, 7, 2, 9, 5 };
            var panel = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Line });

            panel.Measure(new Size(80, 24));
            panel.Arrange(new Rect(0, 0, 80, 24));

            panel.Children.OfType<Line>().Should().HaveCount(4, "5-value line -> 4 segments, unchanged by the RTL wiring");
            var first = panel.Children.OfType<Line>().First();
            // SparklineRenderPlanner.CellInset (3px) is subtracted from the cell on every side, so
            // the left-most drawable X is 3, not 0.
            first.StartPoint.X.Should().BeApproximately(3, 0.5, "left-to-right (default) still starts at the cell's left inset edge");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColumnSparkline_RightToLeftFalse_MatchesPreFixBarOrder()
    {
        await Session.Dispatch(() =>
        {
            var values = new double[] { 2, -1, 3 };
            var panel = new SparklineCellPanel(values, new SparklineModel { Kind = SparklineKind.Column });

            panel.Measure(new Size(60, 24));
            panel.Arrange(new Rect(0, 0, 60, 24));

            var bars = panel.Children.OfType<Rectangle>().ToList();
            bars.Should().HaveCount(3, "3-value column -> 3 bars, unchanged by the RTL wiring");
            // Left-to-right (default): bar 0's slot must be left of bar 2's slot.
            bars[0].Margin.Left.Should().BeLessThan(bars[2].Margin.Left);
        }, CancellationToken.None);
    }
}
