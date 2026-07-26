using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// R90-print-twin-two-tier-sweep-2: round 89 threaded the sparkline group's "Plot Data
/// Right-to-Left" option through <see cref="SparklineLayoutEngine"/> only via new
/// <c>rightToLeft</c>-parameter overloads, which a caller can silently omit (as the WPF host's
/// <c>SparklineLayoutPlanner</c> adapter did) and so miss the option entirely. This closes the
/// design gap at the engine choke point: a caller that hands the engine the
/// <see cref="SparklineModel"/> itself -- via the new <c>SparklineModel</c>-taking overloads of
/// <see cref="SparklineLayoutEngine.CalculateLineLayout"/> and
/// <see cref="SparklineLayoutEngine.CalculateColumnLayout"/> -- cannot skip "Plot Data
/// Right-to-Left" the way the old rightToLeft-parameter overloads allowed.
/// </summary>
public sealed class R90_SparklineModelDrivenRightToLeftTests
{
    private static readonly LayoutRect Cell = new(10, 20, 100, 40);

    [Fact]
    public void CalculateLineLayout_FromModel_HonorsRightToLeftWithoutAnExtraArgument()
    {
        var sparkline = new SparklineModel { Kind = SparklineKind.Line, RightToLeft = true };
        var values = new double[] { 0, 10, 0, 10 };

        // The caller passes only the model + values + rect -- no separate rightToLeft argument to
        // forget -- and still gets the mirrored geometry the explicit rightToLeft:true overload produces.
        var fromModel = SparklineLayoutEngine.CalculateLineLayout(sparkline, values, Cell);
        var explicitRtl = SparklineLayoutEngine.CalculateLineLayout(
            values, Cell, overrideMin: null, overrideMax: null, datePositions: null, rightToLeft: true);

        fromModel.Segments.Should().Equal(explicitRtl.Segments);
        fromModel.SinglePoint.Should().Be(explicitRtl.SinglePoint);

        // And it must actually differ from the plain (non-mirrored) layout -- proving RightToLeft
        // was truly consumed, not silently ignored.
        var plain = SparklineLayoutEngine.CalculateLineLayout(values, Cell);
        fromModel.Segments[0].Start.X.Should().NotBe(plain.Segments[0].Start.X);
    }

    [Fact]
    public void CalculateColumnLayout_FromModel_HonorsKindAndRightToLeftWithoutExtraArguments()
    {
        var sparkline = new SparklineModel { Kind = SparklineKind.Column, RightToLeft = true };
        var values = new double[] { 1, 2, 3 };

        var fromModel = SparklineLayoutEngine.CalculateColumnLayout(sparkline, values, Cell);
        var explicitRtl = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false, overrideMaxAbs: null, rightToLeft: true);

        fromModel.Bars.Should().Equal(explicitRtl.Bars);

        var plain = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false);
        fromModel.Bars[0].Rect.X.Should().Be(plain.Bars[2].Rect.X);
    }

    [Fact]
    public void CalculateColumnLayout_FromModel_DerivesWinLossFromKind()
    {
        var sparkline = new SparklineModel { Kind = SparklineKind.WinLoss, RightToLeft = false };
        var values = new double[] { 5, -2, 3 };

        var fromModel = SparklineLayoutEngine.CalculateColumnLayout(sparkline, values, Cell);
        var explicitWinLoss = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: true);

        fromModel.Bars.Should().Equal(explicitWinLoss.Bars);
    }

    // No-regression sibling: a model with RightToLeft=false (the default) must produce byte-identical
    // output to the plain, non-mirrored layout -- the model-driven overload must not change behavior
    // for the common (left-to-right) case.
    [Fact]
    public void CalculateLineAndColumnLayout_FromModel_RightToLeftFalse_MatchesPlainLayout()
    {
        var lineValues = new double[] { 3, 7, 2, 9, 5, 1, 8 };
        var columnValues = new double[] { 5, -2, 0, 3 };

        var lineSparkline = new SparklineModel { Kind = SparklineKind.Line, RightToLeft = false };
        var fromModelLine = SparklineLayoutEngine.CalculateLineLayout(lineSparkline, lineValues, Cell);
        var plainLine = SparklineLayoutEngine.CalculateLineLayout(lineValues, Cell);
        fromModelLine.Segments.Should().Equal(plainLine.Segments);
        fromModelLine.SinglePoint.Should().Be(plainLine.SinglePoint);

        var columnSparkline = new SparklineModel { Kind = SparklineKind.WinLoss, RightToLeft = false };
        var fromModelColumn = SparklineLayoutEngine.CalculateColumnLayout(columnSparkline, columnValues, Cell);
        var plainColumn = SparklineLayoutEngine.CalculateColumnLayout(columnValues, Cell, winLoss: true);
        fromModelColumn.Bars.Should().Equal(plainColumn.Bars);
    }
}
