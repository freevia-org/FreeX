using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R100: RenameStructuredTableCommand (Table Design > Table Name, and the Name Manager) delegated
/// its formula-fixup entirely to RowColumnShiftHelpers.RewriteAllFormulas, which only enumerates
/// sheet.EnumerateFormulaCells() -- ordinary sheet-cell formulas -- across the workbook. It never
/// touched ConditionalFormats[*].FormulaText, DataValidations[*].Formula1/Formula2, or chart
/// series/error-bar formulas on ANY sheet, so a manual table rename silently broke every CF rule,
/// DV rule, and chart series in the entire workbook that referenced the old name. This is the
/// workbook-wide counterpart of the R99 DuplicateSheetCommand bug (see
/// R99_DuplicateSheetTableFormulaRewriteTests), fixed there for just the cloned sheet.
/// </summary>
public sealed class R100_RenameStructuredTableWorkbookWideFormulaTests
{
    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    private static (Workbook wb, Sheet sheet, StructuredTableModel table) CreateSheetWithTable(
        Workbook? wb = null, string sheetName = "Sheet1", string tableName = "Table1", int tableId = 1)
    {
        wb ??= new Workbook("test");
        var sheet = wb.AddSheet(sheetName);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var table = new StructuredTableModel
        {
            Id = tableId,
            Name = tableName,
            DisplayName = tableName,
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Item"),
                new StructuredTableColumnModel(2, "Values")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Conditional format — same sheet as the table
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameStructuredTableCommand_RewritesConditionalFormatOnOwnSheet()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 10, 1, 10, 1),
            FormulaText = "Table1[Values]>5"
        };
        sheet.ConditionalFormats.Add(cf);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        command.Apply(ctx).Success.Should().BeTrue();

        cf.FormulaText.Should().Be("Revenue[Values]>5");

        command.Revert(ctx);
        cf.FormulaText.Should().Be("Table1[Values]>5");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Data validation — a DIFFERENT sheet than the table (workbook-wide, not
    // sheet-scoped like R99's DuplicateSheetCommand fix)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameStructuredTableCommand_RewritesDataValidationOnAnotherSheet()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var other = wb.AddSheet("Other");
        var dv = new DataValidation
        {
            AppliesTo = Range(other.Id, 1, 1, 1, 1),
            Formula1 = "Table1[Values]"
        };
        other.DataValidations.Add(dv);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        command.Apply(ctx).Success.Should().BeTrue();

        dv.Formula1.Should().Be("Revenue[Values]");

        command.Revert(ctx);
        dv.Formula1.Should().Be("Table1[Values]");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Chart series formulas: Val/Cat/Tx/BubbleSize -- on another sheet, and the
    // exact "comma inside brackets" trap the task calls out explicitly.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameStructuredTableCommand_RewritesChartSeriesFormulasIncludingHeadersCommaShape()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var other = wb.AddSheet("Chart Sheet");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    // The exact comma-inside-brackets shape the FormulaRewriter splitter trap
                    // would corrupt if this ran through RewriteChartVerbatimFormulas/
                    // RewriteVerbatimFormula instead of a whole-formula FormulaRewriter.Rewrite.
                    ValFormula: "Table1[[#Headers],[Values]]",
                    CatFormula: "Table1[Item]",
                    TxFormula: "Table1[[#Totals],[Values]]",
                    BubbleSizeFormula: "Table1[Values]")
            ]
        };
        other.Charts.Add(chart);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        command.Apply(ctx).Success.Should().BeTrue();

        var entry = chart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        entry.ValFormula.Should().Be("Revenue[[#Headers],[Values]]");
        entry.CatFormula.Should().Be("Revenue[Item]");
        entry.TxFormula.Should().Be("Revenue[[#Totals],[Values]]");
        entry.BubbleSizeFormula.Should().Be("Revenue[Values]");

        command.Revert(ctx);

        var revertedEntry = chart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        revertedEntry.ValFormula.Should().Be("Table1[[#Headers],[Values]]");
        revertedEntry.CatFormula.Should().Be("Table1[Item]");
        revertedEntry.TxFormula.Should().Be("Table1[[#Totals],[Values]]");
        revertedEntry.BubbleSizeFormula.Should().Be("Table1[Values]");
    }

    [Fact]
    public void RenameStructuredTableCommand_RewritesChartDataLabelAndErrorBarFormulas()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(SeriesIndex: 0, Formula: "Table1[Values]", PointCount: 2, Points: [])
            ],
            ErrorBarPlusRangeFormula = "Table1[Values]",
            ErrorBarMinusRangeFormula = "Table1[[#Totals],[Values]]"
        };
        sheet.Charts.Add(chart);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        command.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.Formula.Should().Be("Revenue[Values]");
        chart.ErrorBarPlusRangeFormula.Should().Be("Revenue[Values]");
        chart.ErrorBarMinusRangeFormula.Should().Be("Revenue[[#Totals],[Values]]");

        command.Revert(ctx);

        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.Formula.Should().Be("Table1[Values]");
        chart.ErrorBarPlusRangeFormula.Should().Be("Table1[Values]");
        chart.ErrorBarMinusRangeFormula.Should().Be("Table1[[#Totals],[Values]]");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // No-regression: a workbook with no structured tables at all must be untouched
    // (guards against a null-ref / needless full-workbook walk regression).
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameStructuredTableCommand_NoOtherTablesWorkbook_LeavesUnrelatedRulesAndChartsUntouched()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var other = wb.AddSheet("Other");

        var unrelatedCf = new ConditionalFormat
        {
            AppliesTo = Range(other.Id, 1, 1, 1, 1),
            FormulaText = "$A1>0"
        };
        other.ConditionalFormats.Add(unrelatedCf);

        var unrelatedDv = new DataValidation
        {
            AppliesTo = Range(other.Id, 2, 1, 2, 1),
            Formula1 = "10"
        };
        other.DataValidations.Add(unrelatedDv);

        var unrelatedChart = new ChartModel
        {
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(0, "Sheet1!$A$1:$A$5", "Sheet1!$B$1:$B$5", null)
            ]
        };
        other.Charts.Add(unrelatedChart);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        command.Apply(ctx).Success.Should().BeTrue();

        unrelatedCf.FormulaText.Should().Be("$A1>0");
        unrelatedDv.Formula1.Should().Be("10");
        unrelatedChart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Sheet1!$A$1:$A$5");
        unrelatedChart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Sheet1!$B$1:$B$5");
    }

    /// <summary>
    /// Executes through the real product entry point (RenameStructuredTableCommand via a command
    /// context), not a hand-built rewrite -- exercising Apply then Undo (Revert) end to end for a
    /// chart on the table's own sheet plus CF/DV on other sheets simultaneously.
    /// </summary>
    [Fact]
    public void RenameStructuredTableCommand_EndToEnd_ApplyThenUndo_AcrossSheetKinds()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var other = wb.AddSheet("Other");

        var cf = new ConditionalFormat { AppliesTo = Range(sheet.Id, 10, 1, 10, 1), FormulaText = "Table1[Values]>0" };
        sheet.ConditionalFormats.Add(cf);

        var dv = new DataValidation { AppliesTo = Range(other.Id, 1, 1, 1, 1), Formula1 = "Table1[Item]" };
        other.DataValidations.Add(dv);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "Table1[Values]", "Table1[Item]", null)]
        };
        other.Charts.Add(chart);

        var ctx = new TestCommandContext(wb);
        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        cf.FormulaText.Should().Be("Revenue[Values]>0");
        dv.Formula1.Should().Be("Revenue[Item]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Revenue[Values]");
        chart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Revenue[Item]");

        command.Revert(ctx);

        cf.FormulaText.Should().Be("Table1[Values]>0");
        dv.Formula1.Should().Be("Table1[Item]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Table1[Values]");
        chart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Table1[Item]");
        sheet.StructuredTables.Should().ContainSingle().Which.Name.Should().Be("Table1");
    }
}
