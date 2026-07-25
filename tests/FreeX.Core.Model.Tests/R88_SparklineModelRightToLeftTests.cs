using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R88-render-sparkline-5-3: <see cref="SparklineModel.RightToLeft"/> ("Plot Data Right-to-Left")
/// is parsed/round-tripped by <c>XlsxSparklineMapper</c> but was never consumed by anything -- every
/// rendering path always laid out points/bars left-to-right by increasing index. This adds
/// <see cref="SparklineModel.ApplyPlotOrder"/> as the single model-level contract point a layout/
/// render consumer routes a sparkline's series through, so the option actually changes plot order.
/// </summary>
public sealed class R88_SparklineModelRightToLeftTests
{
    [Fact]
    public void ApplyPlotOrder_ReversesSeries_WhenRightToLeftIsSet()
    {
        var sparkline = new SparklineModel { RightToLeft = true };
        var values = new List<double> { 1, 2, 3, 4 };

        var ordered = sparkline.ApplyPlotOrder(values);

        ordered.Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public void ApplyPlotOrder_LeavesSeriesUnchanged_WhenRightToLeftIsNotSet()
    {
        var sparkline = new SparklineModel { RightToLeft = false };
        var values = new List<double> { 1, 2, 3, 4 };

        var ordered = sparkline.ApplyPlotOrder(values);

        ordered.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ApplyPlotOrder_ReturnsSameInstance_ForSingleOrEmptyValues_EvenWhenRightToLeft()
    {
        var sparkline = new SparklineModel { RightToLeft = true };
        var single = new List<double> { 7 };
        var empty = new List<double>();

        sparkline.ApplyPlotOrder(single).Should().Equal(7);
        sparkline.ApplyPlotOrder(empty).Should().BeEmpty();
    }
}
