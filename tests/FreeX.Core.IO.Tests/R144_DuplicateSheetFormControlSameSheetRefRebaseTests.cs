using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R144-worksheet-lifecycle-F1: Duplicate Sheet must rebase a form control's SAME-SHEET-qualified
/// <see cref="FormControlModel.LinkedCell"/>/<see cref="FormControlModel.ListFillRange"/> reference
/// (e.g. "Sheet1!$A$1:$A$3" on a control hosted on Sheet1 itself) onto the duplicate sheet, exactly
/// like every other same-sheet-qualified reference kind on this path (cell formulas, CF/DV formulas,
/// cell hyperlinks, drawing-object "Place in This Document" hyperlinks). Without the fix, the copy's
/// control keeps reading/writing the ORIGINAL sheet's cells: FormControlListResolver.ResolveSelectedText
/// (called from both FreeX.App.Host/MainWindow.Viewport.cs and FreeX.App.Avalonia/MainWindow.FormControls.cs)
/// resolves an explicit sheet qualifier via <c>workbook.GetSheet(sheetName)</c>, so a copy's list box
/// with ListFillRange still literally "Sheet1!..." drives its dropdown off the SOURCE sheet's cells,
/// not its own copy's cells.
/// </summary>
public sealed class R144_DuplicateSheetFormControlSameSheetRefRebaseTests
{
    [Fact]
    public void DuplicateSheet_FormControlWithSameSheetQualifiedListFillRangeAndLinkedCell_RebasesOntoCopy()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gamma"));

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            Name = "List Box 1",
            ShapeId = 1025,
            Anchor = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1)),
            // Explicitly sheet-qualified with the control's OWN hosting sheet's name -- a real,
            // round-trippable OOXML shape (see XlsxFormControlMapperTests.ReadWorksheet_
            // ControlPrFallbackWithNoCtrlProp_RecoversLinkedCellAndListFillRange).
            LinkedCell = "Sheet1!$B$2",
            ListFillRange = "Sheet1!$A$1:$A$3",
        };
        sheet.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.GetSheet("Sheet1 (2)")!;
        copy.FormControls.Should().ContainSingle();
        var copiedControl = copy.FormControls[0];

        // "Sheet1 (2)" contains a space/parentheses, so the rewritten qualifier is quoted per Excel
        // convention (mirrors SheetNameFormatter.QuoteIfNeeded, the same helper
        // RewriteSameSheetHyperlinkTarget uses for the equivalent drawing-object hyperlink rebase).
        copiedControl.ListFillRange.Should().Be(
            "'Sheet1 (2)'!$A$1:$A$3",
            "a same-sheet-qualified ListFillRange must follow the duplicate, not keep pointing at the source sheet");
        copiedControl.LinkedCell.Should().Be(
            "'Sheet1 (2)'!$B$2",
            "a same-sheet-qualified LinkedCell must follow the duplicate, not keep pointing at the source sheet");

        // The ORIGINAL sheet's control must be completely unaffected.
        sheet.FormControls.Should().ContainSingle();
        sheet.FormControls[0].ListFillRange.Should().Be("Sheet1!$A$1:$A$3");
        sheet.FormControls[0].LinkedCell.Should().Be("Sheet1!$B$2");

        // End-to-end proof via the actual runtime resolver both shells call: the copy's list box now
        // resolves its selected item against ITS OWN cells, not the source sheet's.
        copy.SetCell(new CellAddress(copy.Id, 2, 1), new TextValue("Beta-on-copy"));
        copiedControl.SelectedIndex = 2;
        var resolvedOnCopy = FormControlListResolver.ResolveSelectedText(copiedControl, copy, workbook);
        resolvedOnCopy.Should().Be(
            "Beta-on-copy",
            "the copy's control must read the COPY sheet's cell, not the original's");
    }

    // Sibling no-regression case: an UNQUALIFIED (bare) reference must still be copied verbatim --
    // per RowColumnShiftHelpers.ShiftFormControlRef, a bare token always means "this control's own
    // hosting sheet", so copying it unchanged onto the duplicate already makes it follow the copy
    // correctly, and a cross-sheet reference (naming some OTHER sheet) must be left untouched too,
    // matching Excel's Duplicate Sheet behavior of only rebasing SAME-sheet references.
    [Fact]
    public void DuplicateSheet_FormControlWithBareOrCrossSheetRefs_LeavesThemUnchanged()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("Sheet1");
        var otherSheet = workbook.AddSheet("Data");
        otherSheet.SetCell(new CellAddress(otherSheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Y"));

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            Name = "List Box 1",
            ShapeId = 1025,
            Anchor = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1)),
            LinkedCell = "$B$2", // bare -- own hosting sheet, implicitly
            ListFillRange = "Data!$A$1:$A$3", // cross-sheet -- must stay pointed at Data
        };
        sheet.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.GetSheet("Sheet1 (2)")!;
        var copiedControl = copy.FormControls[0];

        copiedControl.LinkedCell.Should().Be("$B$2", "a bare reference is copied verbatim -- it already follows the copy");
        copiedControl.ListFillRange.Should().Be(
            "Data!$A$1:$A$3",
            "a cross-sheet reference must keep pointing at the original OTHER sheet, matching Excel's Duplicate Sheet behavior");
    }
}
