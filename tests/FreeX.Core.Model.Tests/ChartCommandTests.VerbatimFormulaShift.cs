using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    // ── V4 regression: verbatim series formulas shifted on insert/delete rows ──

    [Fact]
    public void InsertRows_ShiftsVerbatimSeriesFormulas_AndUndoRestores()
    {
        // Chart whose series formula is a multi-area union in the REAL OOXML <c:f> format:
        //   (Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5) — parentheses, NO leading '='
        // Inserting a row at row 1 must shift row refs to $A$2:$A$6,$C$2:$C$6.
        // Undo must restore the exact original formula strings (including the parens).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)",
                    CatFormula: "Sheet1!$B$1:$B$5",
                    TxFormula:  null)
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        var vf = chart.VerbatimSeriesFormulas!;
        vf.Should().ContainSingle();
        // Both comma-separated areas inside the parens must have row references shifted by 1.
        vf[0].ValFormula.Should().Be("(Sheet1!$A$2:$A$6,Sheet1!$C$2:$C$6)",
            because: "inserting a row before row 1 shifts $A$1:$A$5 to $A$2:$A$6 (parens re-added)");
        vf[0].CatFormula.Should().Be("Sheet1!$B$2:$B$6",
            because: "the single-area category formula must also be shifted");
        vf[0].TxFormula.Should().BeNull("it was null and should stay null");

        cmd.Revert(ctx);

        var vfAfterUndo = chart.VerbatimSeriesFormulas!;
        vfAfterUndo[0].ValFormula.Should().Be("(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)",
            because: "undo must restore the original verbatim formula including parens");
        vfAfterUndo[0].CatFormula.Should().Be("Sheet1!$B$1:$B$5");
    }

    [Fact]
    public void InsertRows_SingleAreaVerbatimFormula_ShiftsWithoutParens()
    {
        // A single-area formula stored verbatim (no parens) must still shift correctly.
        // (This is the path triggered by e.g. full-column refs like Sheet1!$B:$B.)
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "Sheet1!$A$1:$A$5",
                    CatFormula: null,
                    TxFormula:  null)
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("Sheet1!$A$2:$A$6",
            because: "single-area formula (no parens) must still be shifted");
    }

    [Fact]
    public void InsertRows_QuotedSheetNameWithComma_SplitsCorrectly()
    {
        // A formula with a quoted sheet name that CONTAINS a comma must NOT be split
        // on that comma.  The area list has only one entry, so inserting a row
        // shifts it and wraps back in parens as a single-area parenthesised form.
        // Example real OOXML formula: ('Sheet,1'!$A$1:$A$5)
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet,1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "('Sheet,1'!$A$1:$A$5)",
                    CatFormula: null,
                    TxFormula:  null)
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("('Sheet,1'!$A$2:$A$6)",
            because: "the comma inside the quoted sheet name must not be treated as an area separator");
    }

    [Fact]
    public void InsertRows_ShiftsSeriesRangeDataLabelFormula_AndUndoRestores()
    {
        // Chart with a SeriesRangeDataLabels entry whose Formula references Sheet1 rows.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(
                    SeriesIndex: 0,
                    Formula:     "Sheet1!$D$1:$D$5",
                    PointCount:  5,
                    Points:      [])
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 2);
        cmd.Apply(ctx);

        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Sheet1!$D$3:$D$7",
            because: "inserting 2 rows before row 1 shifts $D$1:$D$5 to $D$3:$D$7");

        cmd.Revert(ctx);

        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Sheet1!$D$1:$D$5",
            because: "undo must restore the original data-label formula");
    }
}
