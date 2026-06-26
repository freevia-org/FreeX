using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests for NN1: form-control LinkedCell, ListFillRange, and Anchor shift on
/// InsertRows / DeleteRows / InsertColumns / DeleteColumns.
/// </summary>
public sealed class FormControlShiftTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static void Apply(IWorkbookCommand cmd, Workbook wb) =>
        cmd.Apply(new TestCommandContext(wb)).Success.Should().BeTrue();

    private static void Revert(IWorkbookCommand cmd, Workbook wb) =>
        cmd.Revert(new TestCommandContext(wb));

    // ── NN1: checkbox LinkedCell shifts on InsertRows ─────────────────────

    [Fact]
    public void InsertRow_Above_LinkedCell_ShiftsDown()
    {
        var (wb, sheet) = NewWorkbook();
        // Checkbox linked to $A$5 anchored in row 5.
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
            Anchor = Range(sheet, 5, 1, 5, 2),
        };
        sheet.FormControls.Add(control);

        // Insert 1 row before row 1.
        Apply(new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1), wb);

        control.LinkedCell.Should().Be("$A$6", "LinkedCell shifts from row 5 → 6 after insert at row 1");
        control.Anchor!.Value.Start.Row.Should().Be(6, "Anchor start row shifts too");
        control.Anchor!.Value.End.Row.Should().Be(6, "Anchor end row shifts too");
    }

    [Fact]
    public void InsertRow_Below_LinkedCell_DoesNotShift()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$2",
            Anchor = Range(sheet, 2, 1, 2, 2),
        };
        sheet.FormControls.Add(control);

        // Insert after row 5 — nothing above should shift.
        Apply(new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1), wb);

        control.LinkedCell.Should().Be("$A$2");
        control.Anchor!.Value.Start.Row.Should().Be(2);
    }

    [Fact]
    public void InsertRow_Above_LinkedCell_ThenUndo_RestoresOriginal()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
            Anchor = Range(sheet, 5, 1, 5, 2),
        };
        sheet.FormControls.Add(control);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        Apply(cmd, wb);

        control.LinkedCell.Should().Be("$A$6");

        Revert(cmd, wb);

        control.LinkedCell.Should().Be("$A$5", "undo restores original LinkedCell");
        control.Anchor!.Value.Start.Row.Should().Be(5, "undo restores original Anchor");
    }

    [Fact]
    public void DeleteRow_Above_LinkedCell_ShiftsUp()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
            Anchor = Range(sheet, 5, 1, 5, 2),
        };
        sheet.FormControls.Add(control);

        Apply(new DeleteRowsCommand(sheet.Id, startRow: 1, count: 1), wb);

        control.LinkedCell.Should().Be("$A$4", "LinkedCell shifts from row 5 → 4 after delete at row 1");
        control.Anchor!.Value.Start.Row.Should().Be(4);
    }

    [Fact]
    public void DeleteRow_ThenUndo_RestoresLinkedCellAndAnchor()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
            Anchor = Range(sheet, 5, 1, 5, 2),
        };
        sheet.FormControls.Add(control);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 1);
        Apply(cmd, wb);
        control.LinkedCell.Should().Be("$A$4");

        Revert(cmd, wb);

        control.LinkedCell.Should().Be("$A$5");
        control.Anchor!.Value.Start.Row.Should().Be(5);
    }

    // ── NN1: listbox ListFillRange + LinkedCell shift on InsertColumns ────

    [Fact]
    public void InsertColumn_Shifts_ListFillRange_And_LinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            LinkedCell = "$C$1",
            ListFillRange = "$A$1:$A$3",
            Anchor = Range(sheet, 1, 3, 3, 4),
        };
        sheet.FormControls.Add(control);

        // Insert 1 column before column 1 (A).
        Apply(new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1), wb);

        control.LinkedCell.Should().Be("$D$1", "LinkedCell shifts from C → D");
        control.ListFillRange.Should().Be("$B$1:$B$3", "ListFillRange shifts from A → B");
        control.Anchor!.Value.Start.Col.Should().Be(4, "Anchor col shifts C(3) → D(4)");
    }

    [Fact]
    public void InsertColumn_ThenUndo_RestoresAllRefs()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            LinkedCell = "$C$1",
            ListFillRange = "$A$1:$A$3",
            Anchor = Range(sheet, 1, 3, 3, 4),
        };
        sheet.FormControls.Add(control);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        Apply(cmd, wb);

        control.LinkedCell.Should().Be("$D$1");
        control.ListFillRange.Should().Be("$B$1:$B$3");

        Revert(cmd, wb);

        control.LinkedCell.Should().Be("$C$1");
        control.ListFillRange.Should().Be("$A$1:$A$3");
        control.Anchor!.Value.Start.Col.Should().Be(3);
    }

    // ── NN1: control anchored on deleted row is removed ───────────────────

    [Fact]
    public void DeleteRow_ControlOnDeletedRow_ControlIsRemoved()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$3",
            Anchor = Range(sheet, 3, 1, 3, 2),
        };
        sheet.FormControls.Add(control);

        Apply(new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1), wb);

        // The control lived on the deleted row — it should be removed (anchor gone).
        // LinkedCell ref to deleted row also becomes null.
        sheet.FormControls.Should().BeEmpty("control whose anchor row was deleted is removed");
    }
}
