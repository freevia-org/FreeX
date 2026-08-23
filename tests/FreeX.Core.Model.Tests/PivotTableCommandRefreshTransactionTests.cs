using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class PivotTableCommandRefreshTransactionTests
{
    [Fact]
    public void RefreshAndRevert_PreserveCellsModelAndBoundChartBindings()
    {
        var (workbook, sheet, pivot) = CreateFilteredPivot("PivotRefreshTransactionRoundTrip");
        var oldRange = pivot.LastRenderedRange!.Value;
        var oldRowFields = pivot.RowFields.ToList();
        var targetSnapshot = AddPivotTableCommand.Snapshot(sheet, oldRange);
        var chartSheet = workbook.AddSheet("Chart");
        var chart = AddBoundChart(chartSheet, pivot, oldRange);

        PivotTableCommandCollections.Replace(pivot.RowFields, [new PivotFieldModel(0)]);
        var outcome = PivotTableCommandRefreshTransaction.RefreshGuarded(
            workbook,
            sheet,
            pivot,
            () =>
            {
                PivotTableCommandCollections.Replace(pivot.RowFields, oldRowFields);
                pivot.LastRenderedRange = oldRange;
            });

        outcome.Should().BeNull();
        pivot.LastRenderedRange.Should().NotBe(oldRange);
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        chart.PivotCacheId.Should().Be(pivot.CacheId);

        PivotTableCommandRefreshTransaction.Revert(
            workbook,
            sheet,
            pivot,
            targetSnapshot,
            table =>
            {
                PivotTableCommandCollections.Replace(table.RowFields, oldRowFields);
                table.LastRenderedRange = oldRange;
            });

        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);
        pivot.LastRenderedRange.Should().Be(oldRange);
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();
        chart.DataRange.Should().Be(oldRange);
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    [Fact]
    public void RefreshGuarded_OnGrowthConflict_RestoresModelAndLeavesChartBindingUnchanged()
    {
        var (workbook, sheet, pivot) = CreateFilteredPivot("PivotRefreshTransactionConflict");
        var oldRange = pivot.LastRenderedRange!.Value;
        var oldRowFields = pivot.RowFields.ToList();
        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("keep"));
        var chart = AddBoundChart(sheet, pivot, oldRange);

        PivotTableCommandCollections.Replace(pivot.RowFields, [new PivotFieldModel(0)]);
        var outcome = PivotTableCommandRefreshTransaction.RefreshGuarded(
            workbook,
            sheet,
            pivot,
            () =>
            {
                PivotTableCommandCollections.Replace(pivot.RowFields, oldRowFields);
                pivot.LastRenderedRange = oldRange;
            });

        outcome.Should().NotBeNull();
        outcome!.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        pivot.RowFields.Single().SelectedItems.Should().BeEquivalentTo(["A", "B"]);
        pivot.LastRenderedRange.Should().Be(oldRange);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("keep"));
        chart.DataRange.Should().Be(oldRange);
    }

    [Fact]
    public void PivotCommands_AdoptSharedRefreshTransactionAndChartBindingService()
    {
        var transactionFiles = new[]
        {
            "ConfigurePivotTableFieldFiltersCommand.cs",
            "ConfigurePivotTableViewCommand.cs",
            "PivotTableCalculatedAndSourceCommands.cs",
            "PivotTableActionCommands.cs"
        };

        foreach (var file in transactionFiles)
        {
            var source = ModelSourceTestSupport.ReadCommandsSource(file);
            source.Should().Contain("PivotTableCommandRefreshTransaction.RefreshGuarded(", file);
            source.Should().Contain("PivotTableCommandRefreshTransaction.Revert(", file);
        }

        var pivotCommandSources = ModelSourceTestSupport.ReadCommandsSourcesMatching(
            "PivotTableCommands.cs",
            "*PivotTable*Command*.cs");
        pivotCommandSources.Should().NotContain("UpdateBoundPivotChartRanges(");

        ModelSourceTestSupport.ReadCommandsSource("ConfigurePivotTableLayoutCommand.cs")
            .Should().Contain("PivotTableCommandRefreshTransaction.RefreshGuarded(");
        ModelSourceTestSupport.ReadCommandsSource("ConfigurePivotTableOptionsCommand.cs")
            .Should().Contain("PivotTableCommandRefreshTransaction.RefreshGuarded(");
        ModelSourceTestSupport.ReadCommandsSource("PivotTableCommands.cs")
            .Should().Contain("PivotTableCommandRefreshTransaction.RefreshGuarded(");
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateFilteredPivot(string name)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["A", "B"]));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        return (workbook, sheet, pivot);
    }

    private static ChartModel AddBoundChart(Sheet sheet, PivotTableModel pivot, GridRange dataRange)
    {
        var chart = new ChartModel
        {
            Name = "PivotChart1",
            IsPivotChart = true,
            PivotTableName = pivot.Name,
            PivotCacheId = pivot.CacheId,
            DataRange = dataRange
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
