using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the F-autofill-core review findings:
///   J37 - Ctrl at drop time flips copy&lt;-&gt;series.
///   J38 - dragging the fill handle inward clears the shrunk-away cells (with undo).
///   J53 - series detection for text-with-trailing-number and Excel's built-in weekday/month lists.
/// </summary>
public class AutofillCommandFAutofillCoreTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ---- J37: Ctrl flips copy <-> series ------------------------------------------------

    [Fact]
    public void CtrlHeld_OnDetectedNumericSeries_ForcesRepeatOfSourceBlockInstead()
    {
        // Excel gesture: A1=1, A2=2 (a detected series), Ctrl-drag down to A5 forces a plain
        // copy instead of continuing the series (3, 4, 5) - but a *copy* of a multi-cell source
        // repeats the whole source block cyclically (1, 2, 1), it does not collapse every
        // destination cell to the single last value in the block. (Round-7 finding M31 fixed
        // this collapse-to-last-edge-cell bug; this test previously asserted the pre-M31 buggy
        // behavior of 2, 2, 2 and has been corrected to Excel's actual block-repeat semantics.)
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CtrlHeld_OnSingleNumericCell_ForcesIncrementingSeriesInsteadOfCopy()
    {
        // Excel gesture: a lone numeric cell normally just copies; Ctrl forces a +1 series.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void CtrlNotHeld_OnSingleNumericCell_StillCopiesUnchanged()
    {
        // Baseline: without Ctrl, a lone number still just copies (existing behavior preserved).
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void CtrlHeld_OnDetectedListSeries_ForcesCopyOfLastValueInstead()
    {
        // Ctrl also flips a detected text/list series (built-in weekday list here) to a plain copy.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Monday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, ctrlHeld: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Monday"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Monday"));
    }

    // ---- J38: inward drag clears the shrunk-away cells ----------------------------------

    [Fact]
    public void InwardDrag_ClearsCellsBeyondShrunkBoundary_Vertical()
    {
        var (_, sheet, ctx) = Setup();
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        // Fill range is a sub-range of source: A4:A5 is what the fill-handle-inward gesture
        // resolves to when dragging from A5 up to A3 (see GridAutofillPlannerTests).
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(4, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void InwardDrag_ClearsCellsBeyondShrunkBoundary_Horizontal()
    {
        var (_, sheet, ctx) = Setup();
        for (uint col = 1; col <= 4; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 4));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(1, 3).Should().Be(BlankValue.Instance);
        sheet.GetValue(1, 4).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void InwardDrag_ClearsValueButPreservesStyle()
    {
        // Matches ClearContentsCommand semantics (Excel's Clear Contents / Delete key): the value
        // is dropped but formatting on the cell is left in place.
        var (workbook, sheet, ctx) = Setup();
        var style = workbook.RegisterStyle(new CellStyle { Bold = true });
        for (uint row = 1; row <= 5; row++)
        {
            var cell = Cell.FromValue(new NumberValue(row));
            cell.StyleId = style;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), cell);
        }

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var clearedCell = sheet.GetCell(4, 1);
        clearedCell.Should().NotBeNull();
        clearedCell!.Value.Should().Be(BlankValue.Instance);
        clearedCell.StyleId.Should().Be(style);
    }

    [Fact]
    public void InwardDrag_Revert_RestoresClearedCells()
    {
        var (_, sheet, ctx) = Setup();
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));

        var cmd = new AutofillCommand(sheet.Id, sourceRange, clearRange);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void InwardDrag_RejectsLockedTargetsOnProtectedSheet()
    {
        var (_, sheet, ctx) = Setup();
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.IsProtected = true;

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, clearRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
    }

    // ---- J53: text-with-trailing-number and built-in list series -------------------------

    [Fact]
    public void FillTextWithTrailingNumber_Down_IncrementsFromSingleSourceCell()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Item 1"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Item 2"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Item 3"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Item 4"));
    }

    [Fact]
    public void FillTextWithTrailingNumber_Down_ContinuesStepFromMultiCellSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Qtr 1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Qtr 2"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Qtr 3"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Qtr 4"));
    }

    [Fact]
    public void FillTextWithTrailingNumber_PreservesLeadingZeroWidth()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Row 08"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Row 09"));
    }

    [Fact]
    public void FillBuiltInWeekdayList_Down_ContinuesAndWrapsAround()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Friday"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Saturday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Sunday"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Monday"));
    }

    [Fact]
    public void FillBuiltInMonthAbbreviationList_Down_WrapsFromDecemberToJanuary()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Nov"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Dec"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Jan"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Feb"));
    }

    [Fact]
    public void FillPlainTextWithNoNumberOrListMembership_StillJustCopies()
    {
        // Sanity: arbitrary text with no trailing number and no built-in list membership
        // has no series to continue, so it still falls back to a plain copy.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Widget"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Widget"));
    }
}
