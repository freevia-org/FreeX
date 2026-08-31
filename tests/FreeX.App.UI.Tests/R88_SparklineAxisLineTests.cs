using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R88-render-sparkline-5-2: the sparkline "Show Axis" line must sit at the data's actual
/// zero-value position implied by the min/max/scale -- matching
/// <see cref="FreeX.App.Presentation.Sparklines.SparklineLayoutEngine.VisitColumnLayout{TConsumer}"/>'s
/// own baseline computation for the bars themselves -- instead of always being drawn through the
/// cell's fixed vertical midpoint. Column data that is entirely one sign puts the baseline at the
/// matching cell edge; a line sparkline whose plotted min/max range does not include zero has no
/// axis line to draw at all (real zero sits outside the visible plot).
/// </summary>
public sealed class R88_SparklineAxisLineTests
{
    private static double InvokeResolveColumnAxisY(IReadOnlyList<double> values, Rect rect, bool winLoss)
    {
        return (double)GridView.ResolveColumnAxisY(values, rect, winLoss)!;
    }

    private static double? InvokeResolveLineAxisY(IReadOnlyList<double> values, Rect rect, double? overrideMin, double? overrideMax)
    {
        return (double?)GridView.ResolveLineAxisY(values, rect, overrideMin, overrideMax);
    }

    [Fact]
    public void ResolveColumnAxisY_AllPositiveColumnData_PlacesAxisAtCellBottomNotMidpoint()
    {
        // Failure scenario from the finding: [5, 10, 3, 8, 2] is entirely positive, so Excel's
        // column baseline (and FreeX's own SparklineLayoutEngine bar baseline) sits at rect.Bottom,
        // not the fixed vertical midpoint.
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveColumnAxisY([5, 10, 3, 8, 2], rect, winLoss: false);

        axisY.Should().Be(rect.Bottom,
            "an all-positive column sparkline's zero baseline sits at the cell bottom, coinciding with the bar bases, " +
            "not at the fixed cell midpoint");
    }

    [Fact]
    public void ResolveColumnAxisY_AllNegativeColumnData_PlacesAxisAtCellTop()
    {
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveColumnAxisY([-5, -10, -3], rect, winLoss: false);

        axisY.Should().Be(rect.Top, "an all-negative column sparkline's zero baseline sits at the cell top");
    }

    [Fact]
    public void ResolveColumnAxisY_MixedSignColumnData_KeepsCenteredMidline()
    {
        // Sibling/no-regression: mixed-sign column data must keep the traditional centered axis
        // (matching the bars' own centered baseline for this case).
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveColumnAxisY([5, -3], rect, winLoss: false);

        axisY.Should().Be(rect.Top + (rect.Height / 2),
            "mixed-sign column data keeps the centered axis, unchanged from before the fix");
    }

    [Fact]
    public void ResolveColumnAxisY_WinLoss_AlwaysKeepsCenteredMidlineRegardlessOfDataShape()
    {
        // Sibling/no-regression: win/loss bars are always fixed half-height keyed on sign alone, so
        // their axis stays centered even for all-positive data (unlike plain column sparklines).
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveColumnAxisY([1, 1, 1], rect, winLoss: true);

        axisY.Should().Be(rect.Top + (rect.Height / 2));
    }

    [Fact]
    public void ResolveLineAxisY_DataDoesNotSpanZero_ReturnsNullSoNoAxisLineIsDrawn()
    {
        // Failure scenario from the finding: values 9-15 never cross zero, so real zero sits
        // outside the plotted range and Excel does not draw an axis line through the chart.
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveLineAxisY([9, 12, 15, 11], rect, overrideMin: null, overrideMax: null);

        axisY.Should().BeNull("zero falls outside the plotted min/max range, so no axis line should be drawn");
    }

    [Fact]
    public void ResolveLineAxisY_DataSpansZeroSymmetrically_ReturnsMidline()
    {
        // Sibling/no-regression: a range that DOES straddle zero must still draw the axis line, at
        // the pixel position that actually corresponds to value 0 (here the midline, since -5..5 is
        // symmetric around zero).
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveLineAxisY([-5, 5, 2], rect, overrideMin: null, overrideMax: null);

        axisY.Should().Be(rect.Top + (rect.Height / 2));
    }

    [Fact]
    public void ResolveLineAxisY_DataSpansZeroAsymmetrically_ReturnsRealZeroPositionNotMidline()
    {
        // A range like -2..8 crosses zero but not at its center: real zero sits 20% of the way up
        // from the bottom (2 / (2+8)), not at the visual midpoint.
        var rect = new Rect(0, 0, 100, 40);

        var axisY = InvokeResolveLineAxisY([-2, 8, 3], rect, overrideMin: null, overrideMax: null);

        axisY.Should().BeApproximately(rect.Bottom - (0.2 * rect.Height), 0.001);
    }
}
