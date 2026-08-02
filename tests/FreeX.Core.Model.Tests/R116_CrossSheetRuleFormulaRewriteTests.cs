using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116: Insert/Delete Rows/Columns/Cells and same-sheet MoveRange must rewrite a
/// ConditionalFormat/DataValidation rule's formula text even when the rule is HOSTED on a
/// different sheet than the one being structurally edited, so long as the rule's own
/// FormulaText/Formula1/Formula2 holds a cross-sheet reference into the shifted sheet
/// (e.g. a List validation on Sheet1 sourced from "=Sheet2!$A$1:$A$10" when rows are
/// inserted/deleted on Sheet2). Before this fix, RowColumnShiftHelpers.RewriteRuleFormulas
/// was only ever called with the single sheet under structural edit, so a rule hosted
/// elsewhere in the workbook was silently left stale -- exactly the gap RenameSheetCommand/
/// DeleteSheetCommand already close for their own analogous rewrite (SheetCommands.cs
/// T7/R100, which explicitly loops `foreach (var s in ctx.Workbook.Sheets)`).
/// </summary>
public sealed class R116_CrossSheetRuleFormulaRewriteTests
{
    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // ══════════════════════════════════════════════════════════════════════════
    // Primary regression: InsertRows on Sheet2 must rewrite a DV rule hosted on Sheet1
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRows_UpdatesCrossSheetDvFormula1HostedOnOtherSheet()
    {
        // Sheet1 hosts a List DV sourced from Sheet2!$A$1:$A$10. Inserting 5 rows before row 1
        // on SHEET2 (not Sheet1) must still shift the DV's Formula1 the same way an ordinary
        // cell formula on Sheet1 referencing Sheet2!A1 would.
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 2, 10, 2),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1:$A$10"
        };
        sheet1.DataValidations.Add(dv);

        new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 5).Apply(ctx);

        dv.Formula1.Should().Be("Sheet2!$A$6:$A$15",
            because: "a DV rule hosted on Sheet1 that sources its list from Sheet2 must have its " +
                     "cross-sheet reference shifted when rows are inserted on Sheet2, exactly like a " +
                     "plain cell formula on Sheet1 referencing Sheet2!A1 already would");
    }

    [Fact]
    public void InsertRowsRevert_RestoresCrossSheetDvFormula1HostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 2, 10, 2),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1:$A$10"
        };
        sheet1.DataValidations.Add(dv);

        var cmd = new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        dv.Formula1.Should().Be("Sheet2!$A$1:$A$10",
            because: "undo must restore the original cross-sheet DV Formula1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling: DeleteRows must rewrite a CF rule hosted on a different sheet
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_UpdatesCrossSheetCfFormulaTextHostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet1.Id, 1, 1, 5, 1),
            FormulaText = "Sheet2!$A6>0"
        };
        sheet1.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet2.Id, startRow: 1, count: 5).Apply(ctx);

        cf.FormulaText.Should().Be("Sheet2!$A1>0",
            because: "a CF rule hosted on Sheet1 that references Sheet2!$A6 must have its row ref " +
                     "shifted from 6 to 1 when 5 rows above it are deleted on Sheet2");
    }

    [Fact]
    public void DeleteRowsRevert_RestoresCrossSheetCfFormulaTextHostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet1.Id, 1, 1, 5, 1),
            FormulaText = "Sheet2!$A6>0"
        };
        sheet1.ConditionalFormats.Add(cf);

        var cmd = new DeleteRowsCommand(sheet2.Id, startRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.FormulaText.Should().Be("Sheet2!$A6>0",
            because: "undo must restore the original cross-sheet CF FormulaText");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling: InsertColumns / DeleteColumns
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertColumns_UpdatesCrossSheetDvFormula1HostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 1, 10, 1),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1:$A$10"
        };
        sheet1.DataValidations.Add(dv);

        new InsertColumnsCommand(sheet2.Id, beforeCol: 1, count: 3).Apply(ctx);

        dv.Formula1.Should().Be("Sheet2!$D$1:$D$10",
            because: "inserting 3 columns before column A on Sheet2 shifts a Sheet1-hosted DV's " +
                     "cross-sheet column reference from A to D");
    }

    [Fact]
    public void DeleteColumns_UpdatesCrossSheetCfFormulaTextHostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet1.Id, 1, 1, 5, 1),
            FormulaText = "Sheet2!$D1>0"
        };
        sheet1.ConditionalFormats.Add(cf);

        new DeleteColumnsCommand(sheet2.Id, startCol: 1, count: 3).Apply(ctx);

        cf.FormulaText.Should().Be("Sheet2!$A1>0",
            because: "deleting 3 columns before column D on Sheet2 shifts a Sheet1-hosted CF's " +
                     "cross-sheet column reference from D to A");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling: Insert/Delete Cells (band-scoped shift)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertCellsShiftDown_UpdatesCrossSheetDvFormula1HostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 1, 10, 1),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1:$A$10"
        };
        sheet1.DataValidations.Add(dv);

        // Insert cells shift-down at Sheet2!A1:A1 -- a whole-column-width band scoped to col A.
        var range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));
        new InsertCellsCommand(sheet2.Id, range, InsertCellsShiftDirection.Down).Apply(ctx);

        dv.Formula1.Should().Be("Sheet2!$A$2:$A$11",
            because: "an Insert-Cells-Shift-Down at Sheet2!A1 must shift a Sheet1-hosted DV's " +
                     "cross-sheet reference into that column exactly like it shifts a cell formula");
    }

    [Fact]
    public void DeleteCellsShiftUp_UpdatesCrossSheetCfFormulaTextHostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet1.Id, 1, 1, 5, 1),
            FormulaText = "Sheet2!$A6>0"
        };
        sheet1.ConditionalFormats.Add(cf);

        // Delete cells shift-up at Sheet2!A1:A1 -- a whole-column-width band scoped to col A.
        var range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));
        new DeleteCellsCommand(sheet2.Id, range, DeleteCellsShiftDirection.Up).Apply(ctx);

        cf.FormulaText.Should().Be("Sheet2!$A5>0",
            because: "a Delete-Cells-Shift-Up at Sheet2!A1 must shift a Sheet1-hosted CF's " +
                     "cross-sheet reference into that column exactly like it shifts a cell formula");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling: same-sheet MoveRange
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveRangeSameSheet_UpdatesCrossSheetDvFormula1HostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 1, 5, 1),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1"
        };
        sheet1.DataValidations.Add(dv);

        // Same-sheet move on Sheet2: A1:B1 -> C3:D3.
        var sourceRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 2));
        var destination = new CellAddress(sheet2.Id, 3, 3);
        new MoveRangeCommand(sheet2.Id, sourceRange, destination).Apply(ctx);

        dv.Formula1.Should().Be("Sheet2!$C$3",
            because: "a same-sheet MoveRange on Sheet2 (A1->C3) must retarget a Sheet1-hosted DV's " +
                     "cross-sheet reference to the moved cell exactly like it retargets a cell formula");
    }

    [Fact]
    public void MoveRangeSameSheetRevert_RestoresCrossSheetDvFormula1HostedOnOtherSheet()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet1.Id, 1, 1, 5, 1),
            Type      = DvType.List,
            Formula1  = "Sheet2!$A$1"
        };
        sheet1.DataValidations.Add(dv);

        var sourceRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 2));
        var destination = new CellAddress(sheet2.Id, 3, 3);
        var cmd = new MoveRangeCommand(sheet2.Id, sourceRange, destination);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        dv.Formula1.Should().Be("Sheet2!$A$1",
            because: "undo must restore the original cross-sheet DV Formula1");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // No-regression guard: the pre-existing same-sheet-hosted rewrite path must still work
    // (i.e. this fix must not have broken the ordinary case where the rule lives ON the
    // sheet under structural edit -- already covered by RuleFormulaShiftTests, duplicated
    // narrowly here as a same-file safety net).
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRows_StillShiftsSameSheetHostedDvFormula1()
    {
        var wb    = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx   = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 2, 10, 2),
            Type      = DvType.List,
            Formula1  = "$A$1:$A$10"
        };
        sheet.DataValidations.Add(dv);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5).Apply(ctx);

        dv.Formula1.Should().Be("$A$6:$A$15",
            because: "the same-sheet rewrite path (rule and structural edit on the same sheet) must " +
                     "keep working after routing the call through the workbook-wide overload");
    }
}
