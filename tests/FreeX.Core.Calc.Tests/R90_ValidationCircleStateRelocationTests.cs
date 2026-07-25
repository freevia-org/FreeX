using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R90-print-twin-two-tier-sweep-1: before the fix, Circle Invalid Data's circled-cell set lived
/// only in the WPF host's <c>GridView.ValidationCircleCells</c> DependencyProperty (screen-only,
/// interactive-instance-only state) -- unreachable from a print/PDF renderer, which only ever has a
/// <see cref="Workbook"/> and a <see cref="SheetId"/>, never the live GridView. <see cref="Sheet.ValidationCircleCells"/>
/// is the sheet/session-level accessor a print renderer could now read directly from
/// <c>workbook.GetSheet(sheetId).ValidationCircleCells</c>. These tests drive the real
/// <see cref="DataValidationCirclePlanner.FindInvalidDataCells"/> planner -- the exact product code
/// <c>MainWindow.DataCommands.cs</c>'s Circle Invalid Data menu handler calls -- and confirm the
/// resulting circled set, once assigned the same way the fixed handler now assigns it (mirrored onto
/// <see cref="Sheet.ValidationCircleCells"/> alongside the screen-only DependencyProperty), is
/// readable back off the workbook/sheet alone. The full WPF menu-click handler itself
/// (<c>MainWindow.CircleInvalidDataMenuItem_Click</c>) is not exercised headlessly here: it lives in
/// FreeX.App.Host, requires a live WPF MainWindow/STA thread, and that test project is documented as
/// hang-prone for full-window scenarios, so this test targets the same real planner + the new
/// model-level accessor instead -- the state-relocation surface this finding asks for.
/// </summary>
public sealed class R90_ValidationCircleStateRelocationTests
{
    [Fact]
    public void SheetValidationCircleCells_PopulatedFromRealPlannerResult_IsReadableFromWorkbookAndSheetIdAlone()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var invalid = new CellAddress(sheet.Id, 1, 1);
        var valid = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(invalid, new NumberValue(15));
        sheet.SetCell(valid, new NumberValue(5));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(invalid, valid),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });

        // Exactly what CircleInvalidDataMenuItem_Click does: run the real planner, then mirror the
        // result onto the sheet model (in addition to the screen-only GridView DependencyProperty).
        var matches = DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet);
        sheet.ValidationCircleCells = matches;

        // A print renderer only ever has the Workbook + SheetId (e.g. PrintRenderer.RenderWorksheet's
        // signature), never the live GridView instance -- re-resolving the sheet through the workbook
        // must expose the same circled cells for that to be possible.
        var resolvedSheet = workbook.GetSheet(sheet.Id);
        resolvedSheet.Should().NotBeNull();
        resolvedSheet!.ValidationCircleCells.Should().Equal(matches);
        resolvedSheet.ValidationCircleCells.Should().Equal(invalid);
    }

    [Fact]
    public void SheetValidationCircleCells_DefaultsToNullAndIsNotCopiedByClone()
    {
        // No-regression sibling: a fresh sheet has no circles, and duplicating a sheet that DOES have
        // circles must not carry stale, unre-checked circles onto the copy (the copy has never had
        // Circle Invalid Data re-run against it, so it should start clean like Excel's own duplicate).
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        sheet.ValidationCircleCells.Should().BeNull();

        sheet.ValidationCircleCells = [new CellAddress(sheet.Id, 1, 1)];
        var copy = sheet.Clone(SheetId.New(), "Copy");

        copy.ValidationCircleCells.Should().BeNull("a cloned sheet has not been re-validated, so it must not inherit stale circles");
        sheet.ValidationCircleCells.Should().NotBeNull("cloning the source sheet must not clear its own circled-cell state");
    }
}
