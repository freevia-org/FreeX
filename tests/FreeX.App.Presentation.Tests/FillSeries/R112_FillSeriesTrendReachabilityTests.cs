using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Tests for R112-app-host-fillseries-trend-unreachable (MED): <see cref="FillSeriesPlanner"/> fully
/// implements Excel's Fill ▸ Series "Trend" checkbox (see R81_FillSeriesTrendTests -- BuildLinearSeriesEdits
/// / BuildGrowthSeriesEdits / BuildSeriesEdits all already dispatch correctly on <see
/// cref="FillSeriesOptions.Trend"/>), but neither of the two <see cref="FillSeriesPlanner.TryCreateOptions"/>
/// overloads that the WPF and Avalonia Fill ▸ Series dialogs actually call ever had a way to accept a
/// Trend flag at all -- so no caller anywhere could ever produce a <see cref="FillSeriesOptions"/> with
/// Trend set to true from parsed dialog input, making the fully-implemented backend permanently
/// unreachable. These tests exercise the new <c>trend</c> parameter added to both TryCreateOptions
/// overloads (and the new <see cref="FillSeriesPlanner.IsTrendEnabled"/> helper the dialogs use to
/// enable/disable the checkbox) to close that gap.
/// </summary>
public sealed class R112_FillSeriesTrendReachabilityTests
{
    // Family-completeness: FillSeriesPlanner.BuildSeriesEdits' authoritative type dispatch has exactly
    // four branches (Growth, Date, AutoFill, Linear-default); Excel only offers Trend for Linear and
    // Growth, so IsTrendEnabled must cover all four branches and agree with that dispatch.
    [Theory]
    [InlineData(FillSeriesType.Linear, true)]
    [InlineData(FillSeriesType.Growth, true)]
    [InlineData(FillSeriesType.Date, false)]
    [InlineData(FillSeriesType.AutoFill, false)]
    public void IsTrendEnabled_OnlyEnablesLinearAndGrowth_MatchingExcelsSeriesDialog(FillSeriesType type, bool expected)
    {
        FillSeriesPlanner.IsTrendEnabled(type).Should().Be(expected);
    }

    [Fact]
    public void TryCreateOptions_TrendTrue_ThreadsTrendFlagIntoResultingOptions()
    {
        // This is the exact reachability gap: before this fix there was no overload of
        // TryCreateOptions that could accept a Trend flag from dialog input at all.
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "1", stopText: null, trend: true, out var options, out var error);

        ok.Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);
        options.Trend.Should().BeTrue();
    }

    [Fact]
    public void TryCreateOptions_WithExplicitCulture_TrendTrue_ThreadsTrendFlagIntoResultingOptions()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Rows, FillSeriesType.Growth, FillSeriesDateUnit.Day,
            stepText: "2", stopText: null, trend: true, culture, out var options, out var error);

        ok.Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);
        options.Trend.Should().BeTrue();
    }

    // No-regression sibling: the pre-existing no-trend overloads (still called positionally by any
    // other/older caller) must keep defaulting Trend to false exactly like before this change.
    [Fact]
    public void TryCreateOptions_WithoutTrendArgument_StillDefaultsTrendFalse()
    {
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "1", stopText: null, out var options, out var error);

        ok.Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);
        options.Trend.Should().BeFalse();
    }

    [Fact]
    public void TryCreateOptions_WithExplicitCulture_WithoutTrendArgument_StillDefaultsTrendFalse()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "1,5", stopText: null, culture, out var options, out var error);

        ok.Should().BeTrue();
        options.Step.Should().Be(1.5);
        options.Trend.Should().BeFalse();
    }

    [Fact]
    public void TryCreateOptions_TrendTrue_RejectsInvalidStepJustLikeNonTrendOverload()
    {
        // Validation must still run identically with Trend set: an invalid step is still rejected
        // (Trend only changes what BuildSeriesEdits does with a *valid* parse, not the parsing itself).
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "not-a-number", stopText: null, trend: true, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(FillSeriesInputError.InvalidStep);
    }

    // End-to-end: parse dialog-shaped input all the way through the REAL BuildSeriesEdits dispatch
    // entry point (the same call both shells make), proving Trend is reachable from parsed text input,
    // not merely settable by hand-constructing a FillSeriesOptions record directly.
    [Fact]
    public void ParsedTrendOptions_RouteThroughBuildSeriesEdits_LinearFitsLeastSquaresLine()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        FillSeriesPlanner.TryCreateOptions(
                FillSeriesDirection.Rows, FillSeriesType.Linear, FillSeriesDateUnit.Day,
                stepText: "1", stopText: null, trend: true, out var options, out var error)
            .Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);

        var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 1, 5));
        // A perfectly linear seed run (10, 20, 30) extrapolates exactly to 40, 50 -- a completely
        // different result than the fixed Step=1 chain (31, 32) the non-Trend engine would produce.
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(40, 50);
    }

    // Sibling for the other Trend-eligible type: Growth must be equally reachable from parsed input.
    [Fact]
    public void ParsedTrendOptions_RouteThroughBuildSeriesEdits_GrowthFitsLeastSquaresCurve()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(8));

        FillSeriesPlanner.TryCreateOptions(
                FillSeriesDirection.Rows, FillSeriesType.Growth, FillSeriesDateUnit.Day,
                stepText: "1", stopText: null, trend: true, out var options, out var error)
            .Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);

        var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);

        edits.Select(e => e.Address).Should().Equal(new CellAddress(sheet.Id, 1, 4));
        // A perfectly geometric seed run (2, 4, 8; ratio 2) extrapolates to 16 -- a completely
        // different result than the fixed Step=1 (as a ratio) chain would produce. Compared with a
        // tight tolerance since the log/exp round-trip can differ from the exact literal by a few ULPs.
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(
            [16.0], (actual, expected) => Math.Abs(actual - expected) < 1e-9);
    }
}
