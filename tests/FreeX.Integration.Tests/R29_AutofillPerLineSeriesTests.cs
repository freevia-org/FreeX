using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for two round-29 AutofillCommand series-detection findings (shared file, so
/// each fix is verified alongside an already-working sibling case it must not disturb):
///
/// R29-autofill-flashfill-deep-1: a source shaped ORTHOGONAL to the fill axis (e.g. a single ROW
/// of 2+ cells dragged DOWN) must not be treated as one shared multi-cell trend across the row --
/// each column only has one sampled value per line, so it must copy independently, exactly like
/// Excel. The already-working sibling (a single row filled along its OWN axis, i.e. dragged
/// RIGHT) must keep continuing its 2-point trend unaffected.
///
/// R29-autofill-flashfill-deep-2: a genuinely rectangular (multi-row AND multi-column) source
/// dragged along the fill axis must continue EACH column's (or row's) own independently-fitted
/// trend, not flatten the whole rectangle into one cyclically-repeated pattern. The already-
/// working sibling (a plain single-column source) must keep continuing its own trend unaffected.
/// </summary>
public class R29_AutofillPerLineSeriesTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ---- R29-autofill-flashfill-deep-1 ---------------------------------------------------

    [Fact]
    public void FillRow_TwoNumbers_DraggedDown_CopiesEachColumnIndependently_NotOneSharedTrend()
    {
        // A1=10, B1=20 (a single row -- two unrelated totals, not a trend along the fill axis).
        // Dragging DOWN must copy each column's own lone value, not fit one 2-point trend across
        // the row and stamp the SAME computed value into both columns.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(b1, Cell.FromValue(new NumberValue(20)));

        var sourceRange = new GridRange(a1, b1); // A1:B1
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)); // A2:B3

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10)); // A2 copies A1
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10)); // A3 copies A1
        sheet.GetValue(2, 2).Should().Be(new NumberValue(20)); // B2 copies B1
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20)); // B3 copies B1
    }

    [Fact]
    public void FillRow_TwoNumbers_DraggedRight_StillContinuesLinearTrend()
    {
        // Sibling already-working case: the SAME single-row source, but filled along its own
        // axis (right), must keep continuing the 2-point trend exactly as before the fix above.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(b1, Cell.FromValue(new NumberValue(20)));

        var sourceRange = new GridRange(a1, b1); // A1:B1
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 4)); // C1:D1

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 3).Should().Be(new NumberValue(30));
        sheet.GetValue(1, 4).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void FillRow_TwoTrailingNumberTexts_DraggedDown_IncrementsEachColumnIndependently()
    {
        // Same orthogonal-mismatch bug, but for the text/list series path: A1="Item1",
        // B1="Row9" dragged DOWN must increment each column's own lone text-with-number value
        // (Excel's default for a single such cell), not fit one shared list-trend across the row.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Item1")));
        sheet.SetCell(b1, Cell.FromValue(new TextValue("Row9")));

        var sourceRange = new GridRange(a1, b1); // A1:B1
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)); // A2:B3

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Item2"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Item3"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Row10"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Row11"));
    }

    // ---- R29-autofill-flashfill-deep-2 ---------------------------------------------------

    [Fact]
    public void FillRectangularSource_DraggedDown_ContinuesEachColumnsOwnTrendIndependently()
    {
        // Column A: 1,2,3 (step 1). Column B: 10,20,30 (step 10) -- two independent linear
        // columns. Dragging DOWN must continue EACH column's own trend, not flatten the 2x3
        // rectangle into one cyclically-repeated pattern that just tiles the original values back.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new NumberValue(30)));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)); // A1:B3
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 6, 2)); // A4:B6

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(40));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(50));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(60));
    }

    [Fact]
    public void FillSingleColumnSource_DraggedDown_StillContinuesLinearTrend()
    {
        // Sibling already-working case: a genuinely single-column (not rectangular) source must
        // keep continuing its own trend exactly as before the per-column fix above.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(3)));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)); // A1:A3
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 6, 1)); // A4:A6

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(6));
    }
}
