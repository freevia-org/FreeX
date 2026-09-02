using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r219: the Configure* family -- options dialogs, which are the purest form of this class. A dialog
/// that pre-fills the current settings and writes them all back on OK changes nothing whenever the
/// user opens it, reads it, and closes it with OK instead of Cancel.
/// <para>
/// Two of these guards are built the way a guard for a wide options dialog should be. Rather than
/// hand-listing the fields -- a transcription that can silently fall out of step with what Apply
/// writes -- they build the target state through the SAME function the mutation uses and compare it
/// against the state it came from. ConfigureSparklineCommand gets this for free because
/// SparklineSettings is a record struct whose Capture and ApplyTo are inverse over the same members;
/// ConfigureStructuredTableStyleOptionsCommand gets it by lifting its single `with` expression into
/// a function that Apply uses for both the decision and the write.
/// </para>
/// </summary>
public sealed class R219_ConfigureNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static ChartModel Chart(Sheet sheet)
    {
        var chart = new ChartModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataInHiddenRowsAndColumns = true,
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    private static SparklineModel Sparkline(Sheet sheet)
    {
        var sparkline = new SparklineModel
        {
            Location = new CellAddress(sheet.Id, 2, 2),
            Kind = SparklineKind.Column,
            ShowHighPoint = true,
        };
        sheet.Sparklines.Add(sparkline);
        return sparkline;
    }

    [Fact]
    public void ReSubmittingAChartsOwnHiddenAndEmptyCellSettings_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        new ConfigureChartHiddenEmptyCellsCommand(
                sheet.Id,
                chart.Id,
                chart.BlankDisplayMode,
                chart.ShowDataInHiddenRowsAndColumns)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue("the sub-dialog pre-selects both controls");
    }

    [Fact]
    public void ChangingOnlyTheBlankDisplayMode_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(
                sheet.Id,
                chart.Id,
                ChartBlankDisplayMode.Gap,
                chart.ShowDataInHiddenRowsAndColumns)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
    }

    [Fact]
    public void ChangingOnlyTheHiddenRowsCheckBox_DoesNotReportNoOp()
    {
        // Both fields matter. A guard that compared only the display mode would have suppressed
        // this, which is the dangerous direction to be wrong in.
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(
                sheet.Id, chart.Id, chart.BlankDisplayMode, false).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
    }

    [Fact]
    public void ReSubmittingASparklinesOwnSettings_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var sparkline = Sparkline(sheet);

        new ConfigureSparklineCommand(
                sheet.Id, sparkline.Id, SparklineSettings.Capture(sparkline))
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingAnySingleSparklineSetting_DoesNotReportNoOp()
    {
        // Capture and ApplyTo are inverse over the same eight members, so this exercises the whole
        // mirror at once: flip one member of the captured record and the guard must let it through.
        var (sheet, ctx) = Fixture();
        var sparkline = Sparkline(sheet);
        var changed = SparklineSettings.Capture(sparkline) with { ShowLowPoint = true };

        var outcome = new ConfigureSparklineCommand(sheet.Id, sparkline.Id, changed).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sparkline.ShowLowPoint.Should().BeTrue();
    }

    [Fact]
    public void ReSubmittingATablesOwnStyleOptions_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var table = Table(sheet);

        new ConfigureStructuredTableStyleOptionsCommand(
                sheet.Id,
                table.Id,
                table.ShowFirstColumn,
                table.ShowLastColumn,
                table.ShowRowStripes,
                table.ShowColumnStripes)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue("Table Design shows the current state of every checkbox");
    }

    [Fact]
    public void TickingATableStyleOptionThatWasOff_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var table = Table(sheet);
        table.ShowFirstColumn.Should().BeFalse();

        var outcome = new ConfigureStructuredTableStyleOptionsCommand(
                sheet.Id,
                table.Id,
                showFirstColumn: true,
                table.ShowLastColumn,
                table.ShowRowStripes,
                table.ShowColumnStripes)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].ShowFirstColumn.Should().BeTrue();
    }

    private static StructuredTableModel Table(Sheet sheet)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            ShowRowStripes = true,
        };
        sheet.StructuredTables.Add(table);
        return table;
    }
}
