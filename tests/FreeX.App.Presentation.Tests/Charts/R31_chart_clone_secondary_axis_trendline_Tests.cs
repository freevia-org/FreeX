using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R31-meta-1: round-30 added per-series trendline/error-bar index tracking
/// (<see cref="ChartModel.TrendlineSeriesIndex"/>/<see cref="ChartModel.ErrorBarSeriesIndex"/>) and
/// secondary-axis title/min/max/number-format fields (<see cref="ChartModel.SecondaryAxisTitle"/> etc.)
/// so the XLSX writer reattaches a trendline/error bars to the correct series and preserves a combo
/// chart's own secondary-axis formatting. <c>DuplicateSheetDrawingCloner.CloneChart</c> (internal to
/// FreeX.Core.Commands) was never updated to copy these new fields, so Home &gt; Sheet &gt; Duplicate
/// Sheet silently reset them to their C# defaults on the copy (trendline/error bars reattached to
/// series 0, secondary axis cloned from the primary). Verifies the duplicate now keeps these fields,
/// alongside an already-working sibling field (<see cref="ChartModel.ShowSecondaryAxis"/> /
/// <see cref="ChartModel.SecondaryAxisSeriesIndexes"/>) that the cloner already handled correctly.
/// </summary>
public sealed class R31_chart_clone_secondary_axis_trendline_Tests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateComboSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Price"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Growth"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(3));
        range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 4));
        return sheet;
    }

    // A trendline attached to series index 2 (not the default 0) plus a secondary axis with its
    // own title/min/max/number-format must survive Duplicate Sheet unchanged -- the bug case.
    [Fact]
    public void DuplicateSheet_ChartWithTrendlineOnNonZeroSeriesAndCustomSecondaryAxis_PreservesBothOnCopy()
    {
        var workbook = new Workbook("ChartCloneSecondaryAxisTrendline");
        var sheet = CreateComboSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ShowLinearTrendline = true,
            TrendlineSeriesIndex = 2,
            TrendlineType = ChartTrendlineType.Linear,
            ShowErrorBars = true,
            ErrorBarSeriesIndex = 2,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            SecondaryAxisTitle = "Growth %",
            SecondaryAxisMinimum = 0,
            SecondaryAxisMaximum = 10,
            SecondaryAxisNumberFormat = ChartDataLabelNumberFormat.Percent,
            SecondaryAxisNumberFormatCode = "0.0%",
            SecondaryAxisNumberFormatSourceLinked = false
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;

        copiedChart.TrendlineSeriesIndex.Should().Be(2,
            "the trendline must stay attached to series 2 on the duplicate, not reattach to series 0");
        copiedChart.ErrorBarSeriesIndex.Should().Be(2,
            "the error bars must stay attached to series 2 on the duplicate, not reattach to series 0");
        copiedChart.SecondaryAxisTitle.Should().Be("Growth %",
            "the secondary axis's own title must not be dropped on Duplicate Sheet");
        copiedChart.SecondaryAxisMinimum.Should().Be(0);
        copiedChart.SecondaryAxisMaximum.Should().Be(10);
        copiedChart.SecondaryAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Percent);
        copiedChart.SecondaryAxisNumberFormatCode.Should().Be("0.0%");
        copiedChart.SecondaryAxisNumberFormatSourceLinked.Should().Be(false);

        // Sibling field the cloner already handled correctly before this fix -- must keep working.
        copiedChart.ShowSecondaryAxis.Should().BeTrue();
        copiedChart.SecondaryAxisSeriesIndexes.Should().BeEquivalentTo([2]);
    }

    // Representative already-working sibling case: a plain chart with no trendline/error bars and
    // no secondary axis at all must duplicate cleanly with those fields left at their defaults.
    [Fact]
    public void DuplicateSheet_ChartWithoutTrendlineOrSecondaryAxis_CopiesDefaultsUnchanged()
    {
        var workbook = new Workbook("ChartCloneNoSecondaryAxis");
        var sheet = CreateComboSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;

        copiedChart.ShowLinearTrendline.Should().BeFalse();
        copiedChart.TrendlineSeriesIndex.Should().Be(0);
        copiedChart.ShowErrorBars.Should().BeFalse();
        copiedChart.ErrorBarSeriesIndex.Should().Be(0);
        copiedChart.ShowSecondaryAxis.Should().BeFalse();
        copiedChart.SecondaryAxisTitle.Should().BeNull();
        copiedChart.SecondaryAxisMinimum.Should().BeNull();
        copiedChart.SecondaryAxisMaximum.Should().BeNull();
        copiedChart.SecondaryAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        copiedChart.SecondaryAxisNumberFormatCode.Should().BeNull();
        copiedChart.SecondaryAxisNumberFormatSourceLinked.Should().BeNull();
    }
}
