using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for round-164 meta-F4: Home &gt; Fill &gt; Series is the fifth destination-sized
/// tiling path found materializing one edit per destination cell with no ceiling, after the
/// internal-clipboard paste, external-clipboard paste, Paste Link, and the fill handle (Autofill).
/// FillSeriesPlanner.BuildSeriesEdits (and every one of the four series-type builders it dispatches
/// to) must reject a destination range larger than the shared
/// <see cref="PasteCommandFactory.MaxTiledPasteCellCount"/> cap -- the same constant the other four
/// paths already enforce -- instead of building millions/billions of edits on the synchronous UI
/// thread. Mirrors the shape of R163_PasteLinkTiledCellCapTests: a 2,001 x 2,001 destination
/// (4,004,001 cells) is one cell over the cap, small enough to safely exercise the pre-fix
/// unbounded code path without the OOM/hang risk of an actual whole-column/whole-sheet-scale range.
/// </summary>
public sealed class R164_FillSeriesTiledCellCapTests
{
    private static GridRange OverCapRange(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2001, 2001)); // 2,001 x 2,001 = 4,004,001 cells, 1 over the cap.

    [Fact]
    public void BuildSeriesEdits_RangeOverSharedCap_ReturnsNoEditsInsteadOfMillions()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1)); // seed: every later cell in
                                                                            // column 1 would chain off it,
                                                                            // and (per BuildLinearSeriesEdits'
                                                                            // per-line-seed carry-over) so
                                                                            // would every subsequent column
                                                                            // with no seed of its own -- this
                                                                            // is exactly the shape the finding
                                                                            // measured 4,001,999 edits from.
        var range = OverCapRange(sheet.Id);
        var options = FillSeriesPlanner.CreateDefaultOptions(step: 1);

        var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);

        edits.Should().BeEmpty("a destination over the shared MaxTiledPasteCellCount cap must be " +
            "rejected up front rather than materialized cell-by-cell");
    }

    [Fact]
    public void IsRangeTooLargeToFill_MatchesTheSharedPasteCommandFactoryCap()
    {
        var sheetId = SheetId.New();
        var overCapRange = OverCapRange(sheetId);

        FillSeriesPlanner.IsRangeTooLargeToFill(overCapRange).Should().BeTrue();
        FillSeriesPlanner.MaxFillSeriesCellCount.Should().Be(PasteCommandFactory.MaxTiledPasteCellCount,
            "Fill ▸ Series must reuse the exact same constant as the internal paste, external " +
            "paste, Paste Link, and Autofill caps rather than declaring a sixth limit that could drift");
    }

    /// <summary>
    /// Sibling no-regression case: an ordinary, well-under-the-cap Fill &gt; Series still produces
    /// its normal per-cell edits -- the cap must reject only oversized destinations, not shrink or
    /// otherwise change a normal fill.
    /// </summary>
    [Fact]
    public void BuildSeriesEdits_RangeUnderCap_StillFillsNormally()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var options = FillSeriesPlanner.CreateDefaultOptions(step: 1);

        FillSeriesPlanner.IsRangeTooLargeToFill(range).Should().BeFalse();

        var edits = FillSeriesPlanner.BuildSeriesEdits(sheet, range, options);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(2, 3, 4, 5);
    }
}
