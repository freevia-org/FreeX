using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_CanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(45d / 70d, 0.0000001);
        Number(sheet, "F5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfGrandTotal()
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
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 70d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 70d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F5").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfRowTotal()
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
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Row Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfRowTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 25d, 0.0000001);
        Number(sheet, "H3").Should().Be(1);
        Number(sheet, "F4").Should().BeApproximately(20d / 45d, 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d / 45d, 0.0000001);
        Number(sheet, "H4").Should().Be(1);
        Number(sheet, "F5").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfColumnTotal()
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
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Column Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfColumnTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 40d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(20d / 30d, 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d / 40d, 0.0000001);
        Number(sheet, "H4").Should().BeApproximately(45d / 70d, 0.0000001);
        Number(sheet, "F5").Should().Be(1);
        Number(sheet, "G5").Should().Be(1);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_ColumnOnlyCanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "E3").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "F3").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "G3").Should().Be(1);
    }

    [Fact]
    public void Refresh_ValuesOnlyCanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G5")
        };
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "E3").Should().Be(1);
    }

    [Fact]
    public void Refresh_CanShowValuesAsRunningTotalInBaseField()
    {
        var workbook = new Workbook("PivotRunningTotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Running Total",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RunningTotalIn,
            BaseFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Q1");
        Number(sheet, "F3").Should().Be(30);
        Text(sheet, "E4").Should().Be("Q2");
        Number(sheet, "F4").Should().Be(70);
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_CanShowValuesAsDifferenceFromBaseItem()
    {
        var workbook = new Workbook("PivotDifferenceFromTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Difference From Q1",
            "sum",
            ShowValuesAs: PivotShowValuesAs.DifferenceFrom,
            BaseFieldIndex: 1,
            BaseItem: "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(0);
        Number(sheet, "F4").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
    }

    [Fact]
    public void Refresh_CanShowValuesAsPercentDifferenceFromBaseItem()
    {
        var workbook = new Workbook("PivotPercentDifferenceFromTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "% Difference From Q1",
            "sum",
            ShowValuesAs: PivotShowValuesAs.PercentDifferenceFrom,
            BaseFieldIndex: 1,
            BaseItem: "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(0);
        Number(sheet, "F4").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "F5").Should().BeApproximately(40d / 30d, 0.0000001);
    }

    [Fact]
    public void Refresh_CanShowValuesAsRankSmallestAndLargest()
    {
        var workbook = new Workbook("PivotRankTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Rank Smallest",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RankSmallest,
            BaseFieldIndex: 1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Rank Largest",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RankLargest,
            BaseFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(1);
        Number(sheet, "G3").Should().Be(2);
        Number(sheet, "F4").Should().Be(2);
        Number(sheet, "G4").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsIndex()
    {
        var workbook = new Workbook("PivotIndexTest");
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
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Index", "sum", ShowValuesAs: PivotShowValuesAs.Index));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d * 70d / (25d * 30d), 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d * 70d / (25d * 40d), 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(20d * 70d / (45d * 30d), 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d * 70d / (45d * 40d), 0.0000001);
        Number(sheet, "H3").Should().Be(1);
        Number(sheet, "F5").Should().Be(1);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfParentTotals()
    {
        var workbook = new Workbook("PivotParentTotalsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "K7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Row", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentRowTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Column", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentColumnTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(10d / 70d, 0.0000001);
    }

}
