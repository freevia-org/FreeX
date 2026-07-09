using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the R17 form-control findings that live in FreeX.Core.Commands:
/// <list type="bullet">
///   <item>R17-form-controls-linkedcell-2 — a bare/unqualified form-control reference (e.g.
///       <c>$B$1</c>) always belongs to the control's OWN hosting sheet, never to whatever sheet a
///       structural edit happens to target. <see cref="RowColumnShiftHelpers"/>'s cross-sheet
///       form-control pass was rewriting such bare tokens against the EDITED sheet's row/column
///       shift, corrupting a control's own-sheet LinkedCell whenever a row/column was
///       inserted/deleted on a completely different sheet the control merely also references (via
///       an explicitly-qualified ListFillRange).</item>
///   <item>R17-form-controls-linkedcell-3 — <see cref="FormControlInteractionService"/> clamped a
///       spinner/scroll-bar's value with <c>Math.Clamp(value, min, max)</c>, which throws
///       <see cref="ArgumentException"/> whenever a malformed control has <c>Min &gt; Max</c> (e.g.
///       loaded from an XLSX where Min defaults above an explicit Max). That must never crash the
///       WPF click handler or the Avalonia render refresh.</item>
/// </list>
/// </summary>
public sealed class R17_formctrl_Tests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    private static void Apply(IWorkbookCommand cmd, Workbook wb) =>
        cmd.Apply(new TestCommandContext(wb)).Success.Should().BeTrue();

    // ── R17-form-controls-linkedcell-2: cross-sheet bare-token shift ──────────

    [Fact]
    public void InsertRow_OnOtherSheet_DoesNotShiftControlsBareOwnSheetLinkedCell()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");

        // A control hosted on Dashboard: LinkedCell is a BARE (unqualified) reference, so it
        // always means "Dashboard!$B$1" -- it never targets Data, no matter what Data's name is.
        // ListFillRange explicitly qualifies Data, so IT should shift when Data is edited.
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            LinkedCell = "$B$1",
            ListFillRange = "Data!$A$1:$A$10",
        };
        dashboard.FormControls.Add(control);

        // Insert a row at the top of Data -- a structural edit on a DIFFERENT sheet than the one
        // hosting the control.
        Apply(new InsertRowsCommand(data.Id, beforeRow: 1, count: 1), wb);

        control.LinkedCell.Should().Be(
            "$B$1",
            "a bare LinkedCell belongs to the control's OWN hosting sheet (Dashboard), so a row " +
            "insert on Data must never shift it");
        control.ListFillRange.Should().Be(
            "Data!$A$2:$A$11",
            "the explicit Data! qualifier DOES target the edited sheet and must shift normally");
    }

    [Fact]
    public void DeleteRow_OnOtherSheet_DoesNotShiftControlsBareOwnSheetLinkedCell()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$B$5",
            ListFillRange = "Data!$A$1:$A$10",
        };
        dashboard.FormControls.Add(control);

        Apply(new DeleteRowsCommand(data.Id, startRow: 1, count: 1), wb);

        control.LinkedCell.Should().Be(
            "$B$5",
            "a bare LinkedCell belongs to Dashboard, so deleting a row on Data must not shift it");
        control.ListFillRange.Should().Be(
            "Data!$A$1:$A$9",
            "the explicit Data! qualifier must still shift for the deletion on Data");
    }

    [Fact]
    public void InsertRow_OnControlsOwnSheet_StillShiftsBareLinkedCell()
    {
        // Sanity check that allowBareToken:false only applies to the CROSS-sheet pass -- a bare
        // LinkedCell on the sheet actually being edited must keep shifting exactly as before.
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "$A$5",
        };
        sheet.FormControls.Add(control);

        Apply(new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1), wb);

        control.LinkedCell.Should().Be("$A$6", "a bare LinkedCell on the edited sheet itself must still shift");
    }

    // ── R17-form-controls-linkedcell-3: Min > Max must not throw ──────────────

    [Fact]
    public void SyncControlsFromLinkedCells_SpinnerWithMinGreaterThanMax_DoesNotThrow()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(60)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            LinkedCell = "A1",
            Min = 100, // malformed: Min > Max
            Max = 30,
            Value = 50,
        };
        sheet.FormControls.Add(control);

        var act = () => FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        act.Should().NotThrow("a malformed control with Min > Max must not crash the render-refresh sync path");
        control.Value.Should().Be(100, "with no valid [min, max] window, the control collapses to Min rather than throwing");
    }

    [Fact]
    public void CreateStepCommand_SpinnerWithMinGreaterThanMax_DoesNotThrow()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(50)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            LinkedCell = "A1",
            Min = 100, // malformed: Min > Max
            Max = 30,
            Value = 50,
        };
        sheet.FormControls.Add(control);

        var act = () => FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        act.Should().NotThrow("stepping a malformed Min>Max spinner must not crash the click handler");
    }

    [Fact]
    public void CreateStepCommand_ScrollBarWithMinGreaterThanMax_DoesNotThrow()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.ScrollBar,
            LinkedCell = null,
            Min = 5,
            Max = 1, // malformed: Min > Max
            Value = 3,
        };
        sheet.FormControls.Add(control);

        var act = () => FormControlInteractionService.CreateStepCommand(control, -1, sheet.Id, wb);

        act.Should().NotThrow("stepping a malformed Min>Max scroll-bar must not crash even with no linked cell");
    }
}
