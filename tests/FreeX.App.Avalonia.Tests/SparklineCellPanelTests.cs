using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the Avalonia sparkline panel port.
/// Tests are split into two classes:
/// <list type="bullet">
///   <item><see cref="SparklineLayoutEngineOverloadTests"/> — pure math / no Avalonia platform needed.</item>
///   <item><see cref="SparklinePanelRenderTests"/> — panel Arrange tests that need the headless platform
///     because Avalonia Shape children (Line, Rectangle, Ellipse) require IPlatformRenderInterface.</item>
/// </list>
/// </summary>

// ── Pure logic tests: no Avalonia platform required ──────────────────────────────────────────────

/// <summary>
/// Tests for the new <see cref="SparklineLayoutEngine"/> overloads that accept axis-bound overrides
/// (group / custom scaling), and the marker-priority logic mirroring the WPF renderer.
/// </summary>
public sealed class SparklineLayoutEngineOverloadTests
{
    // ── CalculateLineLayout with overrides ────────────────────────────────────────────────────────

    [Fact]
    public void CalculateLineLayout_IndividualScaling_UsesLocalMinMax()
    {
        // Values 0..10; no override → min=0, max=10.
        // value=0 → bottom; value=10 → top.
        var values = new double[] { 0, 10 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var layout = SparklineLayoutEngine.CalculateLineLayout(values, rect, null, null);

        layout.Segments.Should().HaveCount(1);
        layout.Segments[0].Start.Y.Should().BeApproximately(rect.Bottom, 0.01); // value=0 → bottom
        layout.Segments[0].End.Y.Should().BeApproximately(rect.Top,    0.01); // value=10 → top
    }

    [Fact]
    public void CalculateLineLayout_CustomMinOverride_ExpandsScaleDown()
    {
        // Data: [5, 10].  Without override min=5, span=5: value=5 → bottom.
        // With override min=0, span=10: value=5 → mid-height.
        var values = new double[] { 5, 10 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var noOverride   = SparklineLayoutEngine.CalculateLineLayout(values, rect, null, null);
        var withOverride = SparklineLayoutEngine.CalculateLineLayout(values, rect, overrideMin: 0, overrideMax: null);

        noOverride.Segments[0].Start.Y.Should().BeApproximately(rect.Bottom, 0.01);
        withOverride.Segments[0].Start.Y.Should().BeApproximately(rect.Top + rect.Height * 0.5, 0.01);
    }

    [Fact]
    public void CalculateLineLayout_CustomMaxOverride_ExpandsScaleUp()
    {
        // Data: [0, 5].  Without override max=5: value=5 → top.
        // With override max=10: value=5 → mid-height.
        var values = new double[] { 0, 5 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var noOverride   = SparklineLayoutEngine.CalculateLineLayout(values, rect, null, null);
        var withOverride = SparklineLayoutEngine.CalculateLineLayout(values, rect, overrideMin: null, overrideMax: 10);

        noOverride.Segments[0].End.Y.Should().BeApproximately(rect.Top, 0.01);
        withOverride.Segments[0].End.Y.Should().BeApproximately(rect.Top + rect.Height * 0.5, 0.01);
    }

    [Fact]
    public void CalculateLineLayout_BothOverrides_GroupScaling()
    {
        // Both bounds overridden: local data [2,8] placed inside [0,10] group range.
        var values = new double[] { 2, 8 };
        var rect   = new LayoutRect(0, 0, 40, 100);

        var layout = SparklineLayoutEngine.CalculateLineLayout(values, rect, overrideMin: 0, overrideMax: 10);

        // value=2 at 20% → y = bottom - 0.2*height = 100 - 20 = 80
        // value=8 at 80% → y = bottom - 0.8*height = 100 - 80 = 20
        layout.Segments[0].Start.Y.Should().BeApproximately(80, 0.5);
        layout.Segments[0].End.Y.Should().BeApproximately(20, 0.5);
    }

    [Fact]
    public void CalculateLineLayout_NullOverrides_MatchesNoOverloadVersion()
    {
        var values = new double[] { 1, 5, 3 };
        var rect   = new LayoutRect(0, 0, 60, 30);

        var a = SparklineLayoutEngine.CalculateLineLayout(values, rect);
        var b = SparklineLayoutEngine.CalculateLineLayout(values, rect, null, null);

        a.Segments.Should().HaveSameCount(b.Segments);
        for (var i = 0; i < a.Segments.Count; i++)
        {
            a.Segments[i].Start.X.Should().BeApproximately(b.Segments[i].Start.X, 0.001);
            a.Segments[i].Start.Y.Should().BeApproximately(b.Segments[i].Start.Y, 0.001);
            a.Segments[i].End.X.Should().BeApproximately(b.Segments[i].End.X, 0.001);
            a.Segments[i].End.Y.Should().BeApproximately(b.Segments[i].End.Y, 0.001);
        }
    }

    // ── CalculateColumnLayout with overrideMaxAbs ─────────────────────────────────────────────────

    [Fact]
    public void CalculateColumnLayout_WithoutOverrideMaxAbs_ScalesBarsByLocalMax()
    {
        // Single all-positive value: the zero baseline sits at the cell bottom (R14 fix), so the
        // local max fills the full cell height (20), not half.
        var values = new double[] { 5 };
        var rect   = new LayoutRect(0, 0, 20, 20);

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, rect, winLoss: false, overrideMaxAbs: null);

        layout.Bars.Should().HaveCount(1);
        layout.Bars[0].Rect.Height.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void CalculateColumnLayout_WithOverrideMaxAbs_ScalesBarsByGroupMax()
    {
        // Local maxAbs=5 but group maxAbs=10 → bar height = 5/10 * full cell height (20) = 10.
        var values = new double[] { 5 };
        var rect   = new LayoutRect(0, 0, 20, 20);

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, rect, winLoss: false, overrideMaxAbs: 10.0);

        layout.Bars.Should().HaveCount(1);
        layout.Bars[0].Rect.Height.Should().BeApproximately(10, 0.01);
    }

    [Fact]
    public void CalculateColumnLayout_WinLoss_NullOverride_FixedHalfHeight()
    {
        // Win-loss: override has no effect (bar height is always half the cell).
        var values = new double[] { 3, -2 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, rect, winLoss: true, overrideMaxAbs: 100.0);

        layout.Bars.Should().HaveCount(2);
        layout.Bars.Should().AllSatisfy(b => b.Rect.Height.Should().BeApproximately(10, 0.01));
    }

    [Fact]
    public void CalculateColumnLayout_NullOverride_MatchesNoOverloadVersion()
    {
        var values = new double[] { 2, -1, 3 };
        var rect   = new LayoutRect(0, 0, 60, 20);

        var a = SparklineLayoutEngine.CalculateColumnLayout(values, rect, winLoss: false);
        var b = SparklineLayoutEngine.CalculateColumnLayout(values, rect, winLoss: false, overrideMaxAbs: null);

        a.Bars.Should().HaveSameCount(b.Bars);
        for (var i = 0; i < a.Bars.Count; i++)
        {
            a.Bars[i].Rect.X.Should().BeApproximately(b.Bars[i].Rect.X, 0.001);
            a.Bars[i].Rect.Y.Should().BeApproximately(b.Bars[i].Rect.Y, 0.001);
            a.Bars[i].Rect.Width.Should().BeApproximately(b.Bars[i].Rect.Width, 0.001);
            a.Bars[i].Rect.Height.Should().BeApproximately(b.Bars[i].Rect.Height, 0.001);
        }
    }

    // ── GetLinePoints: group-scaling override ─────────────────────────────────────────────────────

    [Fact]
    public void GetLinePoints_IndividualScaling_TwoValues_ReturnsTopAndBottomY()
    {
        var values = new double[] { 0, 10 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var points = SparklineLayoutEngine.GetLinePoints(values, rect, null, null);

        points.Should().HaveCount(2);
        points[0].Point.Y.Should().BeApproximately(rect.Bottom, 0.01); // min=0 → bottom
        points[1].Point.Y.Should().BeApproximately(rect.Top,    0.01); // max=10 → top
    }

    [Fact]
    public void GetLinePoints_GroupScalingOverride_ScalesRelativeToGroupMax()
    {
        // Local max=5 but group max=10: value=5 maps to mid-height.
        var values = new double[] { 0, 5 };
        var rect   = new LayoutRect(0, 0, 40, 20);

        var points = SparklineLayoutEngine.GetLinePoints(values, rect, overrideMin: null, overrideMax: 10);

        points.Should().HaveCount(2);
        // value=5 at 50% of [0..10] → y = bottom - 0.5*height = 20 - 10 = 10
        points[1].Point.Y.Should().BeApproximately(rect.Top + rect.Height * 0.5, 0.01);
    }

    // ── Marker-point priority logic ────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkerPriority_NoFlagsSet_ReturnsNull()
    {
        var sparkline = new SparklineModel(); // no show flags
        ComputeMarkerColor(new double[] { 5 }, sparkline, 0, minVal: 5, maxVal: 5, firstIndex: 0, lastIndex: 0)
            .Should().BeNull();
    }

    [Fact]
    public void MarkerPriority_ShowMarkersOnly_AssignsMarkersColor()
    {
        var custom = new CellColor(10, 20, 30);
        var sparkline = new SparklineModel { ShowMarkers = true, MarkersColor = custom };
        ComputeMarkerColor(new double[] { 5 }, sparkline, 0, minVal: 5, maxVal: 5, firstIndex: 0, lastIndex: 0)
            .Should().Be(custom);
    }

    [Fact]
    public void MarkerPriority_NegativeOverridesBaseMarkers()
    {
        var markers = new CellColor(33,  115, 70);
        var negative = new CellColor(192,  0,   0);
        var sparkline = new SparklineModel
        {
            ShowMarkers       = true,
            ShowNegativePoints = true,
            MarkersColor      = markers,
            NegativeColor     = negative,
        };
        // index 0 value = -3 (negative)
        ComputeMarkerColor(new double[] { -3, 4 }, sparkline, 0, minVal: -3, maxVal: 4, firstIndex: 0, lastIndex: 1)
            .Should().Be(negative);
    }

    [Fact]
    public void MarkerPriority_FirstOverridesNegative()
    {
        var negative  = new CellColor(192,  0,  0);
        var firstColor = new CellColor(0,  200,  0);
        var sparkline = new SparklineModel
        {
            ShowNegativePoints = true,
            ShowFirstPoint     = true,
            NegativeColor  = negative,
            FirstPointColor = firstColor,
        };
        // index 0 is both negative and first
        ComputeMarkerColor(new double[] { -1, 2 }, sparkline, 0, minVal: -1, maxVal: 2, firstIndex: 0, lastIndex: 1)
            .Should().Be(firstColor);
    }

    [Fact]
    public void MarkerPriority_HighOverridesLow_WhenSamePoint()
    {
        // When both high and low point at same index, high wins (assigned after low).
        var highColor = new CellColor(216, 0, 0);
        var lowColor  = new CellColor(0,   0, 255);
        var sparkline = new SparklineModel
        {
            ShowHighPoint = true,
            ShowLowPoint  = true,
            HighPointColor = highColor,
            LowPointColor  = lowColor,
        };
        ComputeMarkerColor(new double[] { 5 }, sparkline, 0, minVal: 5, maxVal: 5, firstIndex: 0, lastIndex: 0)
            .Should().Be(highColor);
    }

    [Fact]
    public void MarkerPriority_LastOverridesNegative()
    {
        var negative  = new CellColor(192,   0, 0);
        var lastColor = new CellColor(  0, 150, 0);
        var sparkline = new SparklineModel
        {
            ShowNegativePoints = true,
            ShowLastPoint      = true,
            NegativeColor = negative,
            LastPointColor = lastColor,
        };
        // index 1 is both negative and last
        ComputeMarkerColor(new double[] { 1, -2 }, sparkline, 1, minVal: -2, maxVal: 1, firstIndex: 0, lastIndex: 1)
            .Should().Be(lastColor);
    }

    [Fact]
    public void MarkerPriority_HighestPriority_IsHighOverFirst()
    {
        // High point > first point when both apply at the same index.
        var firstColor = new CellColor(10, 20, 30);
        var highColor  = new CellColor(50, 60, 70);
        var sparkline = new SparklineModel
        {
            ShowFirstPoint = true,
            ShowHighPoint  = true,
            FirstPointColor = firstColor,
            HighPointColor  = highColor,
        };
        ComputeMarkerColor(new double[] { 10 }, sparkline, 0, minVal: 10, maxVal: 10, firstIndex: 0, lastIndex: 0)
            .Should().Be(highColor);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    // Mirrors the marker-priority logic in SparklineCellPanel.BuildLineMarkers for unit-test access
    // without needing Avalonia shapes or a platform.
    private static readonly CellColor DefaultMarkersColor  = new(33,  115, 70);
    private static readonly CellColor DefaultHighColor     = new(216,   0,  0);
    private static readonly CellColor DefaultLowColor      = new(216,   0,  0);
    private static readonly CellColor DefaultFirstColor    = new(33,  115, 70);
    private static readonly CellColor DefaultLastColor     = new(33,  115, 70);
    private static readonly CellColor DefaultNegativeColor = new(192,   0,  0);

    private static CellColor? ComputeMarkerColor(
        IReadOnlyList<double> values,
        SparklineModel sparkline,
        int index,
        double minVal,
        double maxVal,
        int firstIndex,
        int lastIndex)
    {
        var markersColor = sparkline.MarkersColor   ?? DefaultMarkersColor;
        var highColor    = sparkline.HighPointColor  ?? DefaultHighColor;
        var lowColor     = sparkline.LowPointColor   ?? DefaultLowColor;
        var firstColor   = sparkline.FirstPointColor ?? DefaultFirstColor;
        var lastColor    = sparkline.LastPointColor  ?? DefaultLastColor;
        var negColor     = sparkline.NegativeColor   ?? DefaultNegativeColor;

        CellColor? markerColor = null;

        if (sparkline.ShowMarkers)
            markerColor = markersColor;

        if (sparkline.ShowNegativePoints && double.IsFinite(values[index]) && values[index] < 0)
            markerColor = negColor;

        if (sparkline.ShowFirstPoint && index == firstIndex)
            markerColor = firstColor;

        if (sparkline.ShowLastPoint && index == lastIndex)
            markerColor = lastColor;

        if (sparkline.ShowLowPoint && double.IsFinite(values[index]) &&
            Math.Abs(values[index] - minVal) < 1e-10)
            markerColor = lowColor;

        if (sparkline.ShowHighPoint && double.IsFinite(values[index]) &&
            Math.Abs(values[index] - maxVal) < 1e-10)
            markerColor = highColor;

        return markerColor;
    }
}

// ── Platform-dependent tests: need headless Avalonia ─────────────────────────────────────────────

/// <summary>
/// Integration tests for <see cref="SparklineCellPanel"/> that require the Avalonia headless platform
/// because Avalonia <c>Line</c>, <c>Rectangle</c>, and <c>Ellipse</c> shapes need
/// <c>IPlatformRenderInterface</c> when arranged (they compute geometry bounds via the platform).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class SparklinePanelRenderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SparklineCellPanel_MeasureReturnsZero_DoesNotInfluenceParentLayout()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 1, 2, 3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(100, 30));

            panel.DesiredSize.Should().Be(new Size(0, 0),
                "MeasureOverride returns (0,0) so the panel does not influence the parent grid's layout");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_LineSeries_ProducesLineChildrenWhenArranged()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 1, 3, 2, 4, 3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(80, 24));
            panel.Arrange(new Rect(0, 0, 80, 24));

            panel.Children.Should().NotBeEmpty("multi-value line sparkline must produce children");
            panel.Children.OfType<Line>().Should().HaveCount(4,
                "5-value line → 4 segments");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ColumnSeries_ProducesRectangleChildren()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 2, -1, 3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Column };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(60, 24));
            panel.Arrange(new Rect(0, 0, 60, 24));

            panel.Children.OfType<Rectangle>().Should().HaveCount(3,
                "3-value column → 3 bars");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ZeroArrangeSize_ProducesNoChildren()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 1, 2 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(0, 0));
            panel.Arrange(new Rect(0, 0, 0, 0));

            panel.Children.Should().BeEmpty("zero-size arrange must produce no children");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ShowAxis_AddsAxisLineFirst()
    {
        await Session.Dispatch(() =>
        {
            // The axis marks zero, so it only appears when the plotted range actually contains zero.
            // "Share FreeX sparkline axis geometry" replaced this panel's private always-draw-at-the-
            // midpoint line with the shared SparklineAxisLinePlanner, which is what the WPF grid had
            // been using; see SparklineAxisLinePlannerTests.ResolveY_LineRangeOutsideZero_ReturnsNull.
            var values    = new double[] { -2, 4, -3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line, ShowAxis = true };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(80, 24));
            panel.Arrange(new Rect(0, 0, 80, 24));

            // Axis line (Line) + 2 sparkline segments (Line) = 3 total.
            panel.Children.OfType<Line>().Should().HaveCount(3,
                "axis=1 + 2 segments for 3-value line = 3 Lines");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ShowAxis_OmitsAxisWhenRangeExcludesZero()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 2, 4, 3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line, ShowAxis = true };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(80, 24));
            panel.Arrange(new Rect(0, 0, 80, 24));

            // All-positive data never crosses zero, so ShowAxis has nothing to draw and only the
            // 2 sparkline segments remain. Before the shared planner this panel drew a meaningless
            // line across the vertical midpoint here.
            panel.Children.OfType<Line>().Should().HaveCount(2,
                "an all-positive range has no zero crossing for the axis to mark");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ShowAllMarkers_AddsEllipseForEachValue()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 1, 2, 3 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line, ShowMarkers = true };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(90, 24));
            panel.Arrange(new Rect(0, 0, 90, 24));

            // 3 values → 3 marker ellipses, plus 2 line segments.
            panel.Children.OfType<Ellipse>().Should().HaveCount(3, "one ellipse per data point");
            panel.Children.OfType<Line>().Should().HaveCount(2,   "two segments for 3 points");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_ShowHighPoint_AddsOneEllipseMarker()
    {
        await Session.Dispatch(() =>
        {
            var values    = new double[] { 1, 5, 2 };  // high at index 1
            var sparkline = new SparklineModel { Kind = SparklineKind.Line, ShowHighPoint = true };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(80, 24));
            panel.Arrange(new Rect(0, 0, 80, 24));

            panel.Children.OfType<Ellipse>().Should().HaveCount(1, "exactly one high-point marker");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_CustomSeriesColor_AppliedToLineStrokes()
    {
        await Session.Dispatch(() =>
        {
            var customColor = new CellColor(200, 0, 0);
            var values      = new double[] { 1, 2 };
            var sparkline   = new SparklineModel { Kind = SparklineKind.Line, SeriesColor = customColor };
            var panel       = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var line = panel.Children.OfType<Line>().First();
            line.Stroke.Should().BeOfType<SolidColorBrush>();
            var brush = (SolidColorBrush)line.Stroke!;
            brush.Color.R.Should().Be(customColor.R);
            brush.Color.G.Should().Be(customColor.G);
            brush.Color.B.Should().Be(customColor.B);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_WinLoss_NegativeBarUsesNegativeColor()
    {
        await Session.Dispatch(() =>
        {
            var customNeg = new CellColor(128, 0, 64);
            var values    = new double[] { 1, -1, 1 };
            var sparkline = new SparklineModel { Kind = SparklineKind.WinLoss, NegativeColor = customNeg, ShowNegativePoints = true };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var bars = panel.Children.OfType<Rectangle>().ToList();
            bars.Should().HaveCount(3);
            var negBar = bars[1]; // index 1 is the -1 value
            negBar.Fill.Should().BeOfType<SolidColorBrush>();
            ((SolidColorBrush)negBar.Fill!).Color.R.Should().Be(customNeg.R);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_CustomLineWeight_AppliedToStrokeThickness()
    {
        await Session.Dispatch(() =>
        {
            // LineWeight = 2 pt → 2 * 96 / 72 ≈ 2.667 DIP.
            var values    = new double[] { 1, 2 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Line, LineWeight = 2.0 };
            var panel     = new SparklineCellPanel(values, sparkline);

            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var line = panel.Children.OfType<Line>().First();
            line.StrokeThickness.Should().BeApproximately(2.0 * 96.0 / 72.0, 0.01);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SparklineCellPanel_GroupScaledColumn_UsesOverrideMaxAbs()
    {
        await Session.Dispatch(() =>
        {
            // Single all-positive value: the zero baseline sits at the cell bottom, so the bar can
            // reach the full available height rather than only half.
            // value=5 with local maxAbs=5 → full-height bar.
            // With groupMaxAbs=10 → half-height bar.
            var values    = new double[] { 5 };
            var sparkline = new SparklineModel { Kind = SparklineKind.Column };
            var rect      = new Rect(0, 0, 20, 20);

            var panelLocal = new SparklineCellPanel(values, sparkline, overrideMaxAbs: null);
            panelLocal.Measure(rect.Size);
            panelLocal.Arrange(rect);

            var panelGroup = new SparklineCellPanel(values, sparkline, overrideMaxAbs: 10.0);
            panelGroup.Measure(rect.Size);
            panelGroup.Arrange(rect);

            var barLocal = panelLocal.Children.OfType<Rectangle>().Single();
            var barGroup = panelGroup.Children.OfType<Rectangle>().Single();

            // Local max=5: bar height = 5/5 * (20-6) ≈ 14 (inset applied).
            // Group max=10: bar height = 5/10 * (20-6) ≈ 7.
            barGroup.Height.Should().BeLessThan(barLocal.Height,
                "group-scaled bar is shorter because the common maxAbs is larger");
        }, CancellationToken.None);
    }
}
