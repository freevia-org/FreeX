using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for T4 (CF FormulaText shifted on insert/delete rows/cols),
/// T5 (DV Formula1/Formula2 shifted on insert/delete rows/cols),
/// T6 (RenameSheet updates string sheet-name refs on pivot caches / charts / slicers / pictures),
/// and T7 (RenameSheet rewrites CF/DV formula text that contains cross-sheet refs).
/// </summary>
public sealed class RuleFormulaShiftTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup(string sheetName = "Sheet1")
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet(sheetName);
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // ══════════════════════════════════════════════════════════════════════════
    // T4 — CF FormulaText rewritten on InsertRows / DeleteRows
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRows_ShiftsCFFormulaTextDown()
    {
        // CF on A1:A10 with FormulaText '=$A1>0'; insert 5 rows before row 1
        // → FormulaText must become '$A6>0' (leading = is stripped by FormulaSerializer; row shifted by 5).
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo  = Range(sheet.Id, 1, 1, 10, 1),
            FormulaText = "=$A1>0"
        };
        sheet.ConditionalFormats.Add(cf);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5).Apply(ctx);

        cf.FormulaText.Should().Be("$A6>0",
            because: "inserting 5 rows before row 1 shifts the relative row ref from 1 to 6; FormulaSerializer omits the leading =");
    }

    [Fact]
    public void InsertRowsRevert_RestoresCFFormulaText()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet.Id, 1, 1, 10, 1),
            FormulaText = "=$A1>0"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.FormulaText.Should().Be("=$A1>0",
            because: "undo must restore the original CF formula text");
    }

    [Fact]
    public void DeleteRows_ShiftsCFFormulaTextUp()
    {
        // CF with FormulaText '$A6>0' (no leading =); delete rows 1–5 → FormulaText becomes '$A1>0'.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet.Id, 6, 1, 15, 1),
            FormulaText = "$A6>0"
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5).Apply(ctx);

        cf.FormulaText.Should().Be("$A1>0",
            because: "deleting 5 rows above row 6 shifts the CF formula row ref from 6 to 1");
    }

    [Fact]
    public void DeleteRowsRevert_RestoresCFFormulaText()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet.Id, 6, 1, 15, 1),
            FormulaText = "$A6>0"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.FormulaText.Should().Be("$A6>0",
            because: "undo must restore the original CF formula text");
    }

    [Fact]
    public void InsertRows_LeavesCFFormulaTextUnchangedWhenAboveInsertPoint()
    {
        // CF FormulaText with fully absolute ref to B2 — insert rows below row 3 should not change it.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet.Id, 1, 1, 2, 2),
            FormulaText = "=$B$2>0"
        };
        sheet.ConditionalFormats.Add(cf);

        new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3).Apply(ctx);

        cf.FormulaText.Should().Be("=$B$2>0",
            because: "absolute refs to rows above the insert point are not shifted");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // T5 — DV Formula1/Formula2 rewritten on InsertRows / DeleteRows
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRows_ShiftsDvFormula1Down()
    {
        // List DV with Formula1 '$A$1:$A$10' (no leading =); insert 5 rows at row 1
        // → Formula1 becomes '$A$6:$A$15'.
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 2, 10, 2),
            Type      = DvType.List,
            Formula1  = "$A$1:$A$10"
        };
        sheet.DataValidations.Add(dv);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5).Apply(ctx);

        dv.Formula1.Should().Be("$A$6:$A$15",
            because: "inserting 5 rows before row 1 shifts both endpoints of the list source range");
    }

    [Fact]
    public void InsertRowsRevert_RestoresDvFormula1()
    {
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 2, 10, 2),
            Type      = DvType.List,
            Formula1  = "$A$1:$A$10"
        };
        sheet.DataValidations.Add(dv);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        dv.Formula1.Should().Be("$A$1:$A$10",
            because: "undo must restore the original DV Formula1");
    }

    [Fact]
    public void InsertRows_ShiftsDvFormula2Down()
    {
        // WholeNumber DV Between Formula1=$A$5 and Formula2=$A$10; insert 5 rows before row 1
        // → both become $A$10 and $A$15 (no leading = after rewrite).
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),
            Type      = DvType.WholeNumber,
            Operator  = DvOperator.Between,
            Formula1  = "$A$5",
            Formula2  = "$A$10"
        };
        sheet.DataValidations.Add(dv);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5).Apply(ctx);

        dv.Formula1.Should().Be("$A$10");
        dv.Formula2.Should().Be("$A$15");
    }

    [Fact]
    public void InsertRowsRevert_RestoresDvFormula2()
    {
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),
            Type      = DvType.WholeNumber,
            Operator  = DvOperator.Between,
            Formula1  = "$A$5",
            Formula2  = "$A$10"
        };
        sheet.DataValidations.Add(dv);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        dv.Formula1.Should().Be("$A$5");
        dv.Formula2.Should().Be("$A$10");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // T6 — RenameSheet updates string sheet-name refs on model objects
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameSheet_UpdatesPivotCacheSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        wb.PivotCaches.Add(new PivotCacheModel
        {
            CacheId         = 1,
            SourceType      = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data"
        });

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx).Success.Should().BeTrue();

        wb.PivotCaches[0].SourceSheetName.Should().Be("Sales",
            because: "RenameSheet must update PivotCacheModel.SourceSheetName to the new name");
    }

    [Fact]
    public void RenameSheetRevert_RestoresPivotCacheSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        wb.PivotCaches.Add(new PivotCacheModel
        {
            CacheId         = 1,
            SourceType      = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data"
        });

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.PivotCaches[0].SourceSheetName.Should().Be("Data",
            because: "undo must restore the original PivotCacheModel.SourceSheetName");
    }

    [Fact]
    public void RenameSheet_UpdatesChartPivotSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        var chart = new ChartModel { IsPivotChart = true, PivotSourceSheetName = "Data" };
        sheet.Charts.Add(chart);

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.PivotSourceSheetName.Should().Be("Sales");
    }

    [Fact]
    public void RenameSheetRevert_RestoresChartPivotSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        var chart = new ChartModel { IsPivotChart = true, PivotSourceSheetName = "Data" };
        sheet.Charts.Add(chart);

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        chart.PivotSourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void RenameSheet_UpdatesSlicerSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        var slicer = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(slicer);

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx).Success.Should().BeTrue();

        slicer.SourceSheetName.Should().Be("Sales");
    }

    [Fact]
    public void RenameSheetRevert_RestoresSlicerSourceSheetName()
    {
        var (wb, sheet, ctx) = Setup("Data");
        var slicer = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(slicer);

        var cmd = new RenameSheetCommand(sheet.Id, "Sales");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        slicer.SourceSheetName.Should().Be("Data");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // T7 — RenameSheet rewrites CF/DV formula text with cross-sheet refs
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameSheet_UpdatesCrossSheetDvFormula1()
    {
        // Sheet2 has a DV list pointing to Sheet1!$A$1:$A$10 (stored without leading =).
        // Renaming Sheet1 → Data must update the formula to Data!$A$1:$A$10.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet2.Id, 1, 1, 10, 1),
            Type      = DvType.List,
            Formula1  = "Sheet1!$A$1:$A$10"
        };
        sheet2.DataValidations.Add(dv);

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx).Success.Should().BeTrue();

        dv.Formula1.Should().Be("Data!$A$1:$A$10",
            because: "RenameSheet must update DV Formula1 cross-sheet refs to the new name");
    }

    [Fact]
    public void RenameSheetRevert_RestoresCrossSheetDvFormula1()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var dv = new DataValidation
        {
            AppliesTo = Range(sheet2.Id, 1, 1, 10, 1),
            Type      = DvType.List,
            Formula1  = "Sheet1!$A$1:$A$10"
        };
        sheet2.DataValidations.Add(dv);

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        dv.Formula1.Should().Be("Sheet1!$A$1:$A$10",
            because: "undo must restore the original DV Formula1 cross-sheet ref");
    }

    [Fact]
    public void RenameSheet_UpdatesCrossSheetCFFormulaText()
    {
        // Sheet2 has a CF formula referencing Sheet1!A1 (stored without leading =).
        // After renaming Sheet1 → Data the formula must reference Data!A1.
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet2.Id, 1, 1, 5, 1),
            FormulaText = "Sheet1!A1>0"
        };
        sheet2.ConditionalFormats.Add(cf);

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx).Success.Should().BeTrue();

        cf.FormulaText.Should().Be("Data!A1>0",
            because: "RenameSheet must update CF FormulaText cross-sheet refs to the new name");
    }

    [Fact]
    public void RenameSheetRevert_RestoresCrossSheetCFFormulaText()
    {
        var wb     = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx    = new TestCommandContext(wb);

        var cf = new ConditionalFormat
        {
            AppliesTo   = Range(sheet2.Id, 1, 1, 5, 1),
            FormulaText = "Sheet1!A1>0"
        };
        sheet2.ConditionalFormats.Add(cf);

        var cmd = new RenameSheetCommand(sheet1.Id, "Data");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.FormulaText.Should().Be("Sheet1!A1>0",
            because: "undo must restore the original CF FormulaText cross-sheet ref");
    }
}
