using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklineColumnColorPlannerTests
{
    private static readonly CellColor Series = new(33, 115, 70);
    private static readonly CellColor Negative = new(255, 0, 0);
    private static readonly CellColor High = new(0, 176, 80);
    private static readonly CellColor Low = new(255, 192, 0);
    private static readonly CellColor First = new(0, 0, 255);
    private static readonly CellColor Last = new(255, 165, 0);

    [Fact]
    public void ResolveBarColors_HighAndLowOverrideNegative_AndMatchVisibleBarOrder()
    {
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Column,
            ShowNegativePoints = true,
            ShowHighPoint = true,
            ShowLowPoint = true,
        };

        var colors = SparklineColumnColorPlanner.ResolveBarColors(
            sparkline, [5, -3, 8, -1, 4, -6, 2], Series, Negative, High, Low, First, Last);

        colors.Should().Equal([Series, Negative, High, Negative, Series, Low, Series]);
    }

    [Fact]
    public void ResolveBarColors_FirstLastThenHighPrecedence_AppliesToWinLoss()
    {
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.WinLoss,
            ShowNegativePoints = true,
            ShowFirstPoint = true,
            ShowLastPoint = true,
            ShowHighPoint = true,
        };

        var colors = SparklineColumnColorPlanner.ResolveBarColors(
            sparkline, [-1, 1, -1], Series, Negative, High, Low, First, Last);

        colors.Should().Equal([First, High, Last]);
    }

    [Fact]
    public void ResolveBarColors_LineSparkline_IsRejectedToPreserveMarkerPath()
    {
        var sparkline = new SparklineModel { Kind = SparklineKind.Line, ShowMarkers = true };

        var act = () => SparklineColumnColorPlanner.ResolveBarColors(
            sparkline, [1, 2], Series, Negative, High, Low, First, Last);

        act.Should().Throw<ArgumentException>();
    }
}
