using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R141 (finding "convert-to-range-leaves-cf-dv-chart-structured-refs-dangling"):
/// ConvertStructuredTableToRangeCommand only lowered ordinary sheet-cell formulas via
/// ConvertToRangeStructuredReferenceLowering.LowerAllFormulas -- it never touched
/// ConditionalFormats[*].FormulaText/threshold values, DataValidations[*].Formula1/Formula2, or
/// chart series/data-label/error-bar formulas on ANY sheet. Because the table model is removed
/// entirely (unlike a rename, there is nothing left to repoint at), every such CF/DV/chart formula
/// permanently broke the instant "Convert to Range" ran: CF rules stopped evaluating, DV rules
/// stopped validating, and chart series lost their data, with no way to recover short of manually
/// re-typing every formula. Mirrors the sibling R100_RenameStructuredTableWorkbookWideFormulaTests
/// coverage for RenameStructuredTableCommand, except here the expected result is an absolute A1
/// reference (not a renamed structured reference), since the table is gone.
/// </summary>
public sealed class R141_ConvertToRangeWorkbookWideRuleAndChartFormulaTests
{
    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // Table1 spans Sheet1!A1:B3 -- header row 1 (Item, Values), data rows 2-3. No totals row, so
    // every structured selector used below (plain column, [#Headers]) resolves without one.
    private static (Workbook wb, Sheet sheet, StructuredTableModel table) CreateSheetWithTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet.Id, 1, 1, 3, 2),
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
    public void ConvertToRange_LowersConditionalFormatOnOwnSheet_AndUndoRestoresIt()
    {
        var (wb, sheet, table) = CreateSheetWithTable();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 10, 1, 10, 1),
            FormulaText = "Table1[Values]>5"
        };
        sheet.ConditionalFormats.Add(cf);

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        cf.FormulaText.Should().NotContain("Table1", "the table is gone -- nothing left to point a structured reference at");
        cf.FormulaText.Should().Be("$B$2:$B$3>5");

        command.Revert(ctx);
        cf.FormulaText.Should().Be("Table1[Values]>5");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Data validation — a DIFFERENT sheet than the table (workbook-wide, not just
    // the table's own sheet)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToRange_LowersDataValidationOnAnotherSheet_AndUndoRestoresIt()
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
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        dv.Formula1.Should().Be("Sheet1!$B$2:$B$3");

        command.Revert(ctx);
        dv.Formula1.Should().Be("Table1[Values]");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Chart series formulas: Val/Cat/Tx/BubbleSize -- on another sheet.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToRange_LowersChartSeriesFormulas_AndUndoRestoresThem()
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
                    ValFormula: "Table1[[#Headers],[Values]]",
                    CatFormula: "Table1[Item]",
                    TxFormula: null,
                    BubbleSizeFormula: "Table1[Values]")
            ]
        };
        other.Charts.Add(chart);

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var entry = chart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        entry.ValFormula.Should().Be("Sheet1!$B$1");
        entry.CatFormula.Should().Be("Sheet1!$A$2:$A$3");
        entry.BubbleSizeFormula.Should().Be("Sheet1!$B$2:$B$3");

        command.Revert(ctx);

        var revertedEntry = chart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        revertedEntry.ValFormula.Should().Be("Table1[[#Headers],[Values]]");
        revertedEntry.CatFormula.Should().Be("Table1[Item]");
        revertedEntry.BubbleSizeFormula.Should().Be("Table1[Values]");
    }

    [Fact]
    public void ConvertToRange_LowersChartDataLabelAndErrorBarFormulas_AndUndoRestoresThem()
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
            ErrorBarMinusRangeFormula = "Table1[Item]"
        };
        sheet.Charts.Add(chart);

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.Formula.Should().Be("$B$2:$B$3");
        chart.ErrorBarPlusRangeFormula.Should().Be("$B$2:$B$3");
        chart.ErrorBarMinusRangeFormula.Should().Be("$A$2:$A$3");

        command.Revert(ctx);

        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.Formula.Should().Be("Table1[Values]");
        chart.ErrorBarPlusRangeFormula.Should().Be("Table1[Values]");
        chart.ErrorBarMinusRangeFormula.Should().Be("Table1[Item]");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // No-regression: CF/DV/chart formulas that do NOT reference the converted table
    // must be left completely untouched by the pass.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToRange_LeavesUnrelatedConditionalFormatDataValidationAndChartFormulasUntouched()
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
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        unrelatedCf.FormulaText.Should().Be("$A1>0");
        unrelatedDv.Formula1.Should().Be("10");
        unrelatedChart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Sheet1!$A$1:$A$5");
        unrelatedChart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Sheet1!$B$1:$B$5");

        command.Revert(ctx);

        unrelatedCf.FormulaText.Should().Be("$A1>0");
        unrelatedDv.Formula1.Should().Be("10");
        unrelatedChart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Sheet1!$A$1:$A$5");
        unrelatedChart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Sheet1!$B$1:$B$5");
    }

    /// <summary>
    /// Executes through the real product entry point (ConvertStructuredTableToRangeCommand via a
    /// command context), not a hand-built rewrite -- exercising Apply then Undo (Revert) end to end
    /// for a CF rule and a DV rule and a chart simultaneously, matching how a real user's "Convert
    /// to Range" click followed by Ctrl+Z must behave.
    /// </summary>
    [Fact]
    public void ConvertToRange_EndToEnd_ApplyThenUndo_AcrossSheetKinds()
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
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.StructuredTables.Should().BeEmpty();
        cf.FormulaText.Should().Be("$B$2:$B$3>0");
        dv.Formula1.Should().Be("Sheet1!$A$2:$A$3");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Sheet1!$B$2:$B$3");
        chart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Sheet1!$A$2:$A$3");

        command.Revert(ctx);

        cf.FormulaText.Should().Be("Table1[Values]>0");
        dv.Formula1.Should().Be("Table1[Item]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Table1[Values]");
        chart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Table1[Item]");
        sheet.StructuredTables.Should().ContainSingle().Which.Name.Should().Be("Table1");
    }
}
