using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for two Fill ▸ Series ▸ AutoFill copy-default gaps:
///
///   R86-commands-autofill-series-5-1: a lone plain-number seed incremented by +1 instead of
///   copying, contradicting AutofillCommand.WantsSingleCellSeriesDefault's own documented default
///   (a number defaults to a copy; only Ctrl -- which AutoFill has no equivalent toggle for --
///   forces the incrementing series).
///
///   R86-commands-autofill-series-5-2: a repeating non-list text pattern (or any other
///   non-series text run) silently no-opped instead of replaying cyclically, contradicting
///   AutofillCommand's own ResolvePatternSourceAddress cyclic-replay fallback for the identical
///   case.
/// </summary>
public sealed class R86_FillSeriesPlannerAutoFillCopyDefaultsTests
{
    // ── R86-commands-autofill-series-5-1 ──────────────────────────────────────────────────────

    [Fact]
    public void BuildAutoFillSeriesEdits_LoneNumberSeed_CopiesInsteadOfIncrementing()
    {
        // A1 = 5, select A1:A4, AutoFill -> A2:A4 must all copy 5 (Excel's fill-handle default
        // for a single numeric cell), not increment to 6, 7, 8.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(5, 5, 5);
    }

    /// <summary>
    /// Sibling no-regression: a lone DATE seed keeps its own default (a +1-day incrementing
    /// series, per WantsSingleCellSeriesDefault's type-dependent split) -- only the NUMBER
    /// lone-seed default changed.
    /// </summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_LoneDateSeed_StillContinuesByDayUnchanged()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var seedDate = new DateTime(2026, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(seedDate));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((DateTimeValue)e.NewCell.Value).ToDateTime().Date).Should().Equal(
            seedDate.AddDays(1), seedDate.AddDays(2));
    }

    /// <summary>
    /// Sibling no-regression: a 2+ cell numeric seed run still continues its fitted arithmetic
    /// trend -- only the SINGLE-cell numeric default changed.
    /// </summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_TwoNumberSeeds_StillContinuesTheArithmeticStep()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(3, 4, 5);
    }

    // ── R86-commands-autofill-series-5-2 ──────────────────────────────────────────────────────

    [Fact]
    public void BuildAutoFillSeriesEdits_AlternatingTextPattern_ReplaysCyclicallyInsteadOfNoOp()
    {
        // A1="Red", A2="Blue" (arbitrary 2-cell alternating text, not a trailing-number or
        // built-in/custom-list series). Select A1:A6, AutoFill -> A3:A6 must replay the pattern
        // cyclically (Red, Blue, Red, Blue), matching a fill-handle drag over the same seed cells.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Red"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Blue"));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1));
        edits.Select(e => ((TextValue)e.NewCell.Value).Value).Should().Equal("Red", "Blue", "Red", "Blue");
    }

    /// <summary>
    /// Sibling no-regression: a text run that DOES match a detectable series (trailing number)
    /// still continues that series instead of falling into the cyclic-replay fallback.
    /// </summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_TrailingNumberTextSeed_StillContinuesTheSeries()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(range.Start, new TextValue("Item 1"));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((TextValue)e.NewCell.Value).Value).Should().Equal("Item 2", "Item 3", "Item 4");
    }
}
