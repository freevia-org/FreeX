using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for three round-27 AutofillCommand findings (shared file, so each fix is
/// verified alongside an already-working sibling case it must not disturb):
///
/// R27-cut-copy-fill-remaining-2: dragging the fill handle from a single DATE cell (no Ctrl) must
/// default to Excel's day-increment series, not a plain copy -- the reverse of a single plain
/// NUMBER cell's default (copy), which must remain unchanged. Ctrl flips both types' defaults.
///
/// R27-cut-copy-fill-remaining-3: a detected trend/list series must carry forward the WHOLE
/// source selection's per-cell style pattern (cycling), not just the single source-range edge
/// cell's style -- matching the plain pattern-copy branch, which already cycled correctly.
///
/// R27-merged-cells-deep-3: autofilling from a source range that is a single uniformly-sized
/// merged region (e.g. a "Q1" header merged across 2 columns) must tile new, identically-sized
/// merged regions across the fill range instead of unconditionally refusing -- while a merge
/// shape/overlap outside that one supported pattern must still be refused.
/// </summary>
public class R27_AutofillSeriesAndMergeTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ---- R27-cut-copy-fill-remaining-2 ---------------------------------------------------

    [Fact]
    public void FillDateSeries_SingleCell_Down_DefaultsToIncrementingSeriesNotCopy()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, DateTimeValue.FromDateTime(new DateTime(2026, 1, 1)));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1)); // A2:A4

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        ((DateTimeValue)sheet.GetValue(2, 1)).ToDateTime().Should().Be(new DateTime(2026, 1, 2));
        ((DateTimeValue)sheet.GetValue(3, 1)).ToDateTime().Should().Be(new DateTime(2026, 1, 3));
        ((DateTimeValue)sheet.GetValue(4, 1)).ToDateTime().Should().Be(new DateTime(2026, 1, 4));
    }

    [Fact]
    public void FillDateSeries_SingleCell_CtrlHeld_CopiesInstead()
    {
        // Ctrl flips the date default the OTHER way from a plain number: copies verbatim.
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var seedDate = new DateTime(2026, 1, 1);
        sheet.SetCell(source, DateTimeValue.FromDateTime(seedDate));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1)); // A2:A3

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        ((DateTimeValue)sheet.GetValue(2, 1)).ToDateTime().Should().Be(seedDate);
        ((DateTimeValue)sheet.GetValue(3, 1)).ToDateTime().Should().Be(seedDate);
    }

    [Fact]
    public void FillNumberValue_SingleCell_Down_StillCopiesByDefault()
    {
        // Sibling sanity check: a lone plain NUMBER cell must keep its existing default (copy) --
        // only a lone DATE cell's default flips per the fix above.
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(7));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1)); // A2:A3

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(7));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
    }

    // ---- R27-cut-copy-fill-remaining-3 ---------------------------------------------------

    [Fact]
    public void FillNumberSeries_Down_CyclesSourceSelectionStylePattern_NotJustEdgeCellStyle()
    {
        var (workbook, sheet, ctx) = Setup();
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var generalStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "General" });

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var cellA1 = Cell.FromValue(new NumberValue(1));
        cellA1.StyleId = currencyStyle;
        sheet.SetCell(a1, cellA1);
        var cellA2 = Cell.FromValue(new NumberValue(2));
        cellA2.StyleId = generalStyle;
        sheet.SetCell(a2, cellA2);

        var sourceRange = new GridRange(a1, a2);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 6, 1)); // A3:A6

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Values continue the detected linear series...
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(6));

        // ...and the alternating Currency/General style pattern cycles too, instead of every
        // cell collapsing to A2's (General) style.
        sheet.GetCell(3, 1)!.StyleId.Should().Be(currencyStyle);
        sheet.GetCell(4, 1)!.StyleId.Should().Be(generalStyle);
        sheet.GetCell(5, 1)!.StyleId.Should().Be(currencyStyle);
        sheet.GetCell(6, 1)!.StyleId.Should().Be(generalStyle);
    }

    [Fact]
    public void FillPlainCopy_Down_StillCyclesStylePattern_WhenNoSeriesDetected()
    {
        // Sibling sanity check: the plain pattern-copy branch (no trend/list series detected)
        // already cycled the source's per-cell style pattern before this fix and must keep doing
        // so unchanged.
        var (workbook, sheet, ctx) = Setup();
        var boldStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var italicStyle = workbook.RegisterStyle(new CellStyle { Italic = true });

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var cellA1 = Cell.FromValue(new TextValue("Alpha"));
        cellA1.StyleId = boldStyle;
        sheet.SetCell(a1, cellA1);
        var cellA2 = Cell.FromValue(new TextValue("Beta"));
        cellA2.StyleId = italicStyle;
        sheet.SetCell(a2, cellA2);

        var sourceRange = new GridRange(a1, a2);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1)); // A3:A4

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Beta"));
        sheet.GetCell(3, 1)!.StyleId.Should().Be(boldStyle);
        sheet.GetCell(4, 1)!.StyleId.Should().Be(italicStyle);
    }

    // ---- R27-merged-cells-deep-3 ----------------------------------------------------------

    [Fact]
    public void FillListSeries_Right_FromUniformMergedSource_TilesNewSameSizeMergedRegions()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Q1")));
        sheet.AddMergedRegion(new GridRange(a1, b1)); // A1:B1 merged, anchor A1 = "Q1"

        var sourceRange = new GridRange(a1, b1);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 6)); // C1:F1 -> two new 2-column tiles

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var f1 = new CellAddress(sheet.Id, 1, 6);

        sheet.GetValue(1, 3).Should().Be(new TextValue("Q2"));
        sheet.GetValue(1, 5).Should().Be(new TextValue("Q3"));
        // Non-anchor cells of the new merges must hold no independent value.
        sheet.GetCell(d1).Should().BeNull();
        sheet.GetCell(f1).Should().BeNull();
        // New merges were created, same size/shape as the source merge.
        sheet.MergedRegions.Should().Contain(new GridRange(c1, d1));
        sheet.MergedRegions.Should().Contain(new GridRange(e1, f1));
    }

    [Fact]
    public void FillListSeries_Right_FromUniformMergedSource_RevertRemovesCreatedMergesAndCells()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Q1")));
        sheet.AddMergedRegion(new GridRange(a1, b1));

        var sourceRange = new GridRange(a1, b1);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 4)); // C1:D1

        var cmd = new AutofillCommand(sheet.Id, sourceRange, fillRange);
        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        cmd.Revert(ctx);

        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.MergedRegions.Should().NotContain(new GridRange(c1, d1));
        sheet.GetCell(c1).Should().BeNull();
        sheet.GetCell(d1).Should().BeNull();
        // The original source merge must be untouched.
        sheet.MergedRegions.Should().Contain(new GridRange(a1, b1));
    }

    [Fact]
    public void FillNumberSeries_Down_FromUniformMergedSource_TilesVerticallyWithCorrectOffsets()
    {
        // Same tiling as the horizontal case above, but exercises the vertical (Down) axis and a
        // plain number scalar series (Ctrl-forced, since a lone merged number defaults to copy).
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.AddMergedRegion(new GridRange(a1, a2)); // A1:A2 merged (2 rows, 1 col) = 10

        var sourceRange = new GridRange(a1, a2);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 6, 1)); // A3:A6 -> two new 2-row tiles

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new NumberValue(11));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(12));
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 6, 1)).Should().BeNull();
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1)));
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)));
    }

    [Fact]
    public void FillNumberSeries_Up_FromUniformMergedSource_NearestTileGetsSmallestOffset()
    {
        // Reversed (Up) direction: the tile placement must still start adjacent to the source
        // (nearest tile = smallest series offset), not adjacent to the far edge of the fill range.
        var (_, sheet, ctx) = Setup();
        var a5 = new CellAddress(sheet.Id, 5, 1);
        var a6 = new CellAddress(sheet.Id, 6, 1);
        sheet.SetCell(a5, Cell.FromValue(new NumberValue(20)));
        sheet.AddMergedRegion(new GridRange(a5, a6)); // A5:A6 merged (2 rows) = 20

        var sourceRange = new GridRange(a5, a6);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 1)); // A1:A4, filling upward

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Tile nearest the source (rows 3:4, directly above 5:6) gets offset 1.
        sheet.GetValue(3, 1).Should().Be(new NumberValue(19));
        // Tile farthest from the source (rows 1:2) gets offset 2.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(18));
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1)));
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));
    }

    [Fact]
    public void Autofill_StillRejectsMergeOverlapOutsideTheSupportedUniformShape()
    {
        // Sibling sanity check: the pre-existing blanket rejection must still apply whenever the
        // merge overlap ISN'T the one uniform "source range is exactly one merge" shape this fix
        // supports -- e.g. a merge that only partially overlaps a larger source selection.
        var (_, sheet, ctx) = Setup();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, Cell.FromValue(new TextValue("X")));
        sheet.AddMergedRegion(new GridRange(b1, c1)); // B1:C1 merged

        // Source range A1:D1 only partially overlaps (contains, but isn't equal to) the merge.
        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 1, 8));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged cells");
    }
}
