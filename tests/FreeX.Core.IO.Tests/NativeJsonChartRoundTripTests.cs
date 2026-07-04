using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies charts survive a native JSON (.fxl) round-trip, including cross-sheet charts
/// whose data range references a different sheet than the one they are displayed on.
/// Regression for the fxl IsChartOnSheet save filter that silently dropped cross-sheet charts.
/// </summary>
public sealed class NativeJsonChartRoundTripTests
{
    [Fact]
    public void SameSheetChart_SurvivesRoundTrip()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Data");
        Seed(sheet);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B2", sheet.Id),
        });

        var reloaded = RoundTrip(wb);

        reloaded.Sheets[0].Charts.Should().HaveCount(1);
    }

    [Fact]
    public void CrossSheetChart_SurvivesRoundTrip_AndKeepsDataSourceSheet()
    {
        var wb = new Workbook("T");
        var dataSheet = wb.AddSheet("Settings");
        var hostSheet = wb.AddSheet("Budget");
        Seed(dataSheet);

        // Chart displayed on "Budget" but its data range lives on "Settings".
        var crossSheetRange = GridRange.Parse("A1:B2", dataSheet.Id);
        hostSheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = crossSheetRange,
        });

        var reloaded = RoundTrip(wb);

        var reloadedHost = reloaded.GetSheet("Budget")!;
        var reloadedData = reloaded.GetSheet("Settings")!;
        reloadedHost.Charts.Should().HaveCount(1,
            "a cross-sheet chart must not be dropped on an fxl round-trip");

        var chart = (ChartModel)reloadedHost.Charts.Single()!;
        chart.DataRange.Start.Sheet.Should().Be(reloadedData.Id,
            "the chart's data-range source sheet identity must survive the round-trip");
        chart.DataRange.End.Sheet.Should().Be(reloadedData.Id);
    }

    [Fact]
    public void SwitchedRowColumnChart_SurvivesRoundTrip()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Data");
        Seed(sheet);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B2", sheet.Id),
            SeriesInRows = true,
        });

        var reloaded = RoundTrip(wb);

        var chart = (ChartModel)reloaded.Sheets[0].Charts.Single()!;
        chart.SeriesInRows.Should().BeTrue("the Switch Row/Column orientation must survive an fxl round-trip");
    }

    private static void Seed(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
    }

    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
