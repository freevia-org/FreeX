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

        // East/Q1 = 10. With a single (outermost) row and column field the parent is the
        // grand total taken in the SAME column (parent row) or SAME row (parent column):
        //   % of Parent Row Total    = 10 / (grand total at the Q1 column = 30)
        //   % of Parent Column Total = 10 / (grand total at the East row  = 25)
        //   % of Parent Total        = 10 / grand total (70), base-field selection not modeled
        Number(sheet, "F3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(10d / 70d, 0.0000001);
    }

    [Fact]
    public void Refresh_MatrixPercentOfParentTotal_UsesSelectedRowBaseFieldWithinColumnContext()
    {
        var workbook = new Workbook("PivotParentTotalRowBaseTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "K12"),
            ReportLayout = PivotReportLayout.Tabular,
            // R90-render-pivot-layout-5-1: pin the (former) no-subtotal default -- this 2-row-field
            // layout test's cell coordinates assume no subtotal rows.
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(
            3,
            "% Region",
            "sum",
            ShowValuesAs: PivotShowValuesAs.PercentOfParentTotal,
            BaseFieldIndex: 0,
            BaseItem: "West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "H3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "I3").Should().BeApproximately(15d / 40d, 0.0000001);
        Number(sheet, "J3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "H4").Should().BeApproximately(20d / 30d, 0.0000001);
        Number(sheet, "I4").Should().BeApproximately(25d / 40d, 0.0000001);
        Number(sheet, "J4").Should().BeApproximately(45d / 70d, 0.0000001);
    }

    [Fact]
    public void Refresh_MatrixPercentOfParentTotal_UsesSelectedColumnBaseFieldWithinRowContext()
    {
        var workbook = new Workbook("PivotParentTotalColumnBaseTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "L12"),
            ReportLayout = PivotReportLayout.Tabular,
            // R90-render-pivot-layout-5-1: pin the (former) no-subtotal default -- this 2-column-field
            // layout test's cell coordinates assume no subtotal columns.
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(
            3,
            "% Quarter",
            "sum",
            ShowValuesAs: PivotShowValuesAs.PercentOfParentTotal,
            BaseFieldIndex: 1,
            BaseItem: "Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "G4").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "H4").Should().BeApproximately(15d / 25d, 0.0000001);
        Number(sheet, "I4").Should().BeApproximately(20d / 45d, 0.0000001);
        Number(sheet, "J4").Should().BeApproximately(25d / 45d, 0.0000001);
    }

    [Fact]
    public void Refresh_RowOnlyNestedShowValuesAsPercentOfParentRowTotal()
    {
        var workbook = new Workbook("PivotParentRowNestedTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "H20"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1: pin the (former) Bottom default -- this test's "East total"
            // assertion expects the subtotal after the group's leaf rows.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "% Parent Row", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentRowTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Region>Quarter, no columns. Each Quarter row is a % of its Region subtotal,
        // each Region subtotal is a % of the grand total (220).
        //   East Q1=25, East Q2=45, East total=70; West Q1=65, West Q2=85, West total=150.
        Number(sheet, "H3").Should().BeApproximately(25d / 70d, 0.0000001);  // East Q1 / East
        Number(sheet, "H4").Should().BeApproximately(45d / 70d, 0.0000001);  // East Q2 / East
        Number(sheet, "H5").Should().BeApproximately(70d / 220d, 0.0000001); // East total / grand
        Number(sheet, "H6").Should().BeApproximately(65d / 150d, 0.0000001); // West Q1 / West
        Number(sheet, "H7").Should().BeApproximately(85d / 150d, 0.0000001); // West Q2 / West
        Number(sheet, "H8").Should().BeApproximately(150d / 220d, 0.0000001);// West total / grand
        Number(sheet, "H9").Should().BeApproximately(1d, 0.0000001);         // grand total
    }
}
