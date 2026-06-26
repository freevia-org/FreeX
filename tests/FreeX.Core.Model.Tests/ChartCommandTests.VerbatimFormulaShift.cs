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
        // Chart whose series formula is a multi-area union stored verbatim
        // (ValFormula = "=Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5").
        // Inserting a row at row 1 must shift row refs to $A$2:$A$6,$C$2:$C$6.
        // Undo must restore the original formula strings.
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
                    ValFormula: "=Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5",
                    CatFormula: "=Sheet1!$B$1:$B$5",
                    TxFormula:  null)
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        var vf = chart.VerbatimSeriesFormulas!;
        vf.Should().ContainSingle();
        // Both comma-separated areas must have their row references shifted by 1.
        vf[0].ValFormula.Should().Be("=Sheet1!$A$2:$A$6,Sheet1!$C$2:$C$6",
            because: "inserting a row before row 1 shifts $A$1:$A$5 to $A$2:$A$6");
        vf[0].CatFormula.Should().Be("=Sheet1!$B$2:$B$6",
            because: "the category formula must also be shifted");
        vf[0].TxFormula.Should().BeNull("it was null and should stay null");

        cmd.Revert(ctx);

        var vfAfterUndo = chart.VerbatimSeriesFormulas!;
        vfAfterUndo[0].ValFormula.Should().Be("=Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5",
            because: "undo must restore the original verbatim formula");
        vfAfterUndo[0].CatFormula.Should().Be("=Sheet1!$B$1:$B$5");
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
