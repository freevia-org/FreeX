using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MatrixAppliesLabelFiltersToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(1, PivotLabelFilterKind.Equals, "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F4").Should().Be(20);
        Number(sheet, "G4").Should().Be(20);
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(30);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesComparisonAndBetweenLabelFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Between, "East", "West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesSelectedItemsToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["West"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("West");
        Number(sheet, "F3").Should().Be(45);
        Text(sheet, "E4").Should().Be("Grand Total");
        Number(sheet, "F4").Should().Be(45);
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesSelectedItemsToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItems: ["Q2"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "F4").Should().Be(25);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_SelectedItemsIgnoreBlankAndAllSentinels()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItems: ["", "(All)", "q2"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "F4").Should().Be(25);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixAppliesValueFiltersToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 35, SourceFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "F4").Should().Be(25);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesBetweenValueFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Between, ComparisonValue: 40, ComparisonValue2: 75, SourceFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Central");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesAboveAverageValueFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Central");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixSortsColumnLabelsDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(30);
    }

    [Fact]
    public void Refresh_MatrixSortsColumnValuesDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(30);
    }

    [BenchmarkFact]
    public void Benchmark_ColumnValueFilterAndSort_ReportsTimingAndAllocatedBytes()
    {
        const int rowCount = 12_000;
        const int columnItemCount = 240;
        const int iterations = 3;

        var workbook = new Workbook("PivotRefreshPerfTest");
        var sheet = workbook.AddSheet("Data");
        SeedPivotRefreshPerformanceData(sheet, rowCount, columnItemCount);
        var pivot = new PivotTableModel
        {
            Name = "PivotTablePerf",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, (uint)rowCount + 1, 3)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 2, 5),
                new CellAddress(sheet.Id, 3, 5 + (uint)columnItemCount + 1))
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(
            0,
            PivotValueFilterKind.GreaterThanOrEqual,
            ComparisonValue: 0,
            SourceFieldIndex: 1));
        pivot.Sorts.Add(new PivotSortModel(
            PivotSortTarget.Value,
            PivotSortDirection.Descending,
            DataFieldIndex: 0,
            FieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var step = System.Diagnostics.Stopwatch.StartNew();
            PivotTableRefreshService.Refresh(workbook, sheet, pivot);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var meanMs = total.Elapsed.TotalMilliseconds / iterations;
        Console.WriteLine(
            $"PERF PIVOT_REFRESH_COLUMN_VALUE_FILTER_SORT rows={rowCount} column_items={columnItemCount} iterations={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={meanMs:F2} max_ms={timings.Max():F2} allocated_bytes={allocatedBytes}");

        Text(sheet, "E2").Should().NotBeEmpty();
        allocatedBytes.Should().BeLessThan(9_000_000);
        total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Refresh_AppliesPageFieldSelectedItemFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Quarter");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("Region");
        Text(sheet, "E5").Should().Be("East");
        Number(sheet, "F5").Should().Be(10);
        Text(sheet, "E6").Should().Be("West");
        Number(sheet, "F6").Should().Be(20);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(30);
    }

    [Fact]
    public void Refresh_AppliesPageFieldMultiSelectFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItems: ["East", "North"]));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("(Multiple Items)");
        Text(sheet, "E5").Should().Be("Q1");
        Number(sheet, "F5").Should().Be(60);
        Text(sheet, "E6").Should().Be("Q2");
        Number(sheet, "F6").Should().Be(15);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(75);
    }

    [Fact]
    public void Refresh_AppliesTopNValueFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 2));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
    }

    [Fact]
    public void Refresh_AppliesLabelFilterContains()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "st"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_AppliesValueGreaterThanFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 45));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("Grand Total");
        Number(sheet, "F4").Should().Be(50);
    }

    [Fact]
    public void Refresh_SortsRowsByValueDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("East");
        Number(sheet, "F5").Should().Be(25);
    }

}
