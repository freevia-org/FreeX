using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// K15/K16 regression: a chart hosted on one sheet (e.g. "Dashboard") whose series data
/// (DataRange and/or verbatim series/data-label formulas) reference a DIFFERENT sheet
/// (e.g. "Data") must have those references shifted/rewritten when the REFERENCED sheet
/// (not the hosting sheet) undergoes a structural edit: row/column insert/delete, rename,
/// or delete. Previously only same-sheet charts were ever touched because the shift/rewrite
/// helpers only walked the edited sheet's own <see cref="Sheet.Charts"/> list.
/// </summary>
public sealed class ChartCrossSheetReferenceCommandTests
{
    [Fact]
    public void InsertRows_OnDataSheet_ShiftsCrossSheetChartDataRange_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(
            new CellAddress(data.Id, 2, 2),
            new CellAddress(data.Id, 10, 2));
        var chart = new ChartModel { DataRange = originalRange, Type = ChartType.Column };
        dashboard.Charts.Add(chart);

        var cmd = new InsertRowsCommand(data.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(data.Id, 3, 2),
            new CellAddress(data.Id, 11, 2)),
            because: "the chart lives on Dashboard but its DataRange points at Data, which had a row inserted");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalRange, because: "undo must restore the original cross-sheet DataRange");
    }

    [Fact]
    public void DeleteRows_OnDataSheet_ShiftsCrossSheetChartDataRange_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(
            new CellAddress(data.Id, 5, 2),
            new CellAddress(data.Id, 10, 2));
        var chart = new ChartModel { DataRange = originalRange, Type = ChartType.Column };
        dashboard.Charts.Add(chart);

        var cmd = new DeleteRowsCommand(data.Id, startRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(data.Id, 4, 2),
            new CellAddress(data.Id, 9, 2)),
            because: "deleting a row above the referenced range on Data must shift it up even though the chart lives on Dashboard");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void InsertColumns_OnDataSheet_ShiftsCrossSheetChartDataRange_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(
            new CellAddress(data.Id, 1, 2),
            new CellAddress(data.Id, 5, 2));
        var chart = new ChartModel { DataRange = originalRange, Type = ChartType.Column };
        dashboard.Charts.Add(chart);

        var cmd = new InsertColumnsCommand(data.Id, beforeCol: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(data.Id, 1, 3),
            new CellAddress(data.Id, 5, 3)),
            because: "the chart lives on Dashboard but its DataRange points at Data, which had a column inserted");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void DeleteColumns_OnDataSheet_ShiftsCrossSheetChartDataRange_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(
            new CellAddress(data.Id, 1, 5),
            new CellAddress(data.Id, 5, 5));
        var chart = new ChartModel { DataRange = originalRange, Type = ChartType.Column };
        dashboard.Charts.Add(chart);

        var cmd = new DeleteColumnsCommand(data.Id, startCol: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(data.Id, 1, 4),
            new CellAddress(data.Id, 5, 4)),
            because: "deleting a column to the left of the referenced range on Data must shift it left");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void InsertRows_OnDataSheet_ShiftsCrossSheetVerbatimSeriesFormula_HostedOnAnotherSheet_AndUndoRestores()
    {
        // Multi-area verbatim series formula (the REAL OOXML <c:f> union form) that references
        // the "Data" sheet, but the chart itself lives on "Dashboard".
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 5, 3));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "(Data!$A$1:$A$5,Data!$C$1:$C$5)",
                    CatFormula: "Data!$B$1:$B$5",
                    TxFormula:  null)
            ]
        };
        dashboard.Charts.Add(chart);

        var cmd = new InsertRowsCommand(data.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var vf = chart.VerbatimSeriesFormulas!;
        vf[0].ValFormula.Should().Be("(Data!$A$2:$A$6,Data!$C$2:$C$6)",
            because: "the chart's own hosting sheet is Dashboard, but its verbatim formula refs Data, which had a row inserted");
        vf[0].CatFormula.Should().Be("Data!$B$2:$B$6");

        cmd.Revert(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(Data!$A$1:$A$5,Data!$C$1:$C$5)",
            because: "undo must restore the original cross-sheet verbatim formula");
        chart.VerbatimSeriesFormulas![0].CatFormula.Should().Be("Data!$B$1:$B$5");
    }

    [Fact]
    public void InsertRows_OnDataSheet_ShiftsCrossSheetSeriesRangeDataLabelFormula_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 5, 2));
        var chart = new ChartModel
        {
            DataRange = dataRange,
            Type = ChartType.Column,
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(
                    SeriesIndex: 0,
                    Formula:     "Data!$D$1:$D$5",
                    PointCount:  5,
                    Points:      [])
            ]
        };
        dashboard.Charts.Add(chart);

        var cmd = new InsertRowsCommand(data.Id, beforeRow: 1, count: 2);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Data!$D$3:$D$7",
            because: "the chart lives on Dashboard but its data-label formula refs Data, which had rows inserted");

        cmd.Revert(ctx);

        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Data!$D$1:$D$5");
    }

    [Fact]
    public void RenameSheet_RewritesCrossSheetChartVerbatimFormula_HostedOnAnotherSheet_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 5, 2)),
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "(Data!$A$1:$A$5,Data!$C$1:$C$5)",
                    CatFormula: null,
                    TxFormula:  null)
            ],
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(
                    SeriesIndex: 0, Formula: "Data!$D$1:$D$5", PointCount: 5, Points: [])
            ]
        };
        dashboard.Charts.Add(chart);

        var cmd = new RenameSheetCommand(data.Id, "Revenue");
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(Revenue!$A$1:$A$5,Revenue!$C$1:$C$5)",
            because: "the chart is hosted on Dashboard but its verbatim series formula names the renamed sheet");
        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Revenue!$D$1:$D$5",
            because: "the data-label formula also names the renamed sheet");

        cmd.Revert(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(Data!$A$1:$A$5,Data!$C$1:$C$5)",
            because: "undo must restore the original sheet name in the verbatim formula");
        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Data!$D$1:$D$5");
    }

    [Fact]
    public void RemoveSheet_TurnsCrossSheetChartVerbatimFormula_IntoRefError_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(dashboard.Id, 1, 1), new CellAddress(dashboard.Id, 1, 1)),
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "(Data!$A$1:$A$5,Data!$C$1:$C$5)",
                    CatFormula: null,
                    TxFormula:  null)
            ],
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(
                    SeriesIndex: 0, Formula: "Data!$D$1:$D$5", PointCount: 5, Points: [])
            ]
        };
        dashboard.Charts.Add(chart);

        var cmd = new RemoveSheetCommand(data.Id);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(#REF!,#REF!)",
            because: "each area of the multi-area union references the now-deleted Data sheet and must become #REF!, mirroring ordinary cell/CF/DV formulas");
        chart.SeriesRangeDataLabels[0].Formula.Should().Be("#REF!",
            because: "the data-label formula also references the deleted sheet");

        cmd.Revert(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(Data!$A$1:$A$5,Data!$C$1:$C$5)",
            because: "undo must restore the original verbatim formula text after the sheet comes back");
        chart.SeriesRangeDataLabels[0].Formula.Should().Be("Data!$D$1:$D$5");
    }

    [Fact]
    public void InsertRows_SameSheetChart_StillShiftsAsBefore_NoRegression()
    {
        // Guard against regressing the ordinary same-sheet case while generalizing to
        // workbook-wide iteration.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 5, 1));
        var chart = new ChartModel { DataRange = originalRange, Type = ChartType.Column };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 6, 1)));

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
    }
}
