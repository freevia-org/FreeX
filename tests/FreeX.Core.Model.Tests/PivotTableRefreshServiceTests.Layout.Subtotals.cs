using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_WritesOuterRowFieldSubtotalsWhenEnabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1/5-3: pin the (former) model defaults this Tabular/Bottom-
            // subtotal scenario was written against, now that both defaults changed.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E5").Should().Be("East Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E8").Should().Be("West Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

    [Fact]
    public void Refresh_CanPlaceOuterRowFieldSubtotalsAtTopOfGroup()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H10"),
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            // R90-render-pivot-layout-5-3: pin the (former) Tabular default -- this test asserts a
            // per-row-field-column layout (E="East", F="Q1"), not the new Compact default.
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East Total");
        Number(sheet, "G3").Should().Be(25);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q1");
        Text(sheet, "E6").Should().Be("West Total");
        Number(sheet, "G6").Should().Be(45);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q1");
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

    [Fact]
    public void Refresh_TabularLayout_WritesMultiLevelSubtotalsForThreeRowFields()
    {
        // Verifies that with 3 row fields both the Quarter-level (inner) and
        // Region-level (outer) subtotal rows are emitted with correct sums.
        var workbook = new Workbook("PivotTabularMultiLevelSubtotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "J22"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1: pin the (former) Bottom default -- this test's "Q1 Total"
            // assertions expect the subtotal after the leaf rows, not Excel's actual Top default.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region
        pivot.RowFields.Add(new PivotFieldModel(1)); // Quarter
        pivot.RowFields.Add(new PivotFieldModel(2)); // Channel
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Quarter-level subtotals (inner)
        Text(sheet, "F5").Should().Be("Q1 Total");
        Number(sheet, "I5").Should().Be(25);
        Text(sheet, "F8").Should().Be("Q2 Total");
        Number(sheet, "I8").Should().Be(45);
        // Region-level subtotals (outer)
        Text(sheet, "F9").Should().Be("East Total");
        Number(sheet, "I9").Should().Be(70);
        // West quarter subtotals
        Text(sheet, "F12").Should().Be("Q1 Total");
        Number(sheet, "I12").Should().Be(65);
        Text(sheet, "F15").Should().Be("Q2 Total");
        Number(sheet, "I15").Should().Be(85);
        // West region subtotal
        Text(sheet, "F16").Should().Be("West Total");
        Number(sheet, "I16").Should().Be(150);
        // Grand Total
        Text(sheet, "F17").Should().Be("Grand Total");
        Number(sheet, "I17").Should().Be(220);
    }

    [Fact]
    public void Refresh_MatrixLayout_WritesMultiLevelSubtotalsForThreeRowFields()
    {
        // Verifies that a 3-row-field + 1-column-field matrix (WriteMatrixPivot) emits
        // Quarter-level and Region-level subtotal rows with correct per-column values.
        // Data (SeedSalesProductChannelData): Region/Product/Quarter/Channel/Amount
        //   East Widget Q1 Retail=10, East Widget Q1 Wholesale=15
        //   West Gadget Q2 Retail=20, West Gadget Q2 Wholesale=25
        var workbook = new Workbook("PivotMatrixMultiLevelSubtotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesProductChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "E5"),
            TargetRange = Range(sheet, "G2", "N15"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1: pin the (former) Bottom default -- this test's "Widget Total"
            // assertions expect the subtotal after the leaf row, not Excel's actual Top default.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom
        };
        // Row fields: Region(0), Product(1), Quarter(2)
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region
        pivot.RowFields.Add(new PivotFieldModel(1)); // Product
        pivot.RowFields.Add(new PivotFieldModel(2)); // Quarter
        pivot.ColumnFields.Add(new PivotFieldModel(3)); // Channel
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Column layout: G=Row Labels, H=Retail, I=Wholesale, J=Grand Total
        // Row fields [Region, Product, Quarter]: innermost (Quarter) has no subtotal;
        // subtotals fire for Product-level ([Region,Product] prefix) and Region-level ([Region] prefix).
        // Row 3: East Widget Q1 (Retail=10, Wholesale=15, GT=25)
        Text(sheet, "G3").Should().Be("East Widget Q1");
        Number(sheet, "H3").Should().Be(10);
        Number(sheet, "I3").Should().Be(15);
        Number(sheet, "J3").Should().Be(25);
        // Widget Total (Product-level subtotal: innermost subtotaled level, key=[East,Widget])
        Text(sheet, "G4").Should().Be("Widget Total");
        Number(sheet, "H4").Should().Be(10);
        Number(sheet, "I4").Should().Be(15);
        Number(sheet, "J4").Should().Be(25);
        // East Total (Region-level subtotal: outermost level, key=[East])
        Text(sheet, "G5").Should().Be("East Total");
        Number(sheet, "H5").Should().Be(10);
        Number(sheet, "I5").Should().Be(15);
        Number(sheet, "J5").Should().Be(25);
        // West Gadget Q2
        Text(sheet, "G6").Should().Be("West Gadget Q2");
        Number(sheet, "H6").Should().Be(20);
        Number(sheet, "I6").Should().Be(25);
        Number(sheet, "J6").Should().Be(45);
        // Gadget Total (Product-level subtotal for [West,Gadget])
        Text(sheet, "G7").Should().Be("Gadget Total");
        Number(sheet, "H7").Should().Be(20);
        Number(sheet, "I7").Should().Be(25);
        Number(sheet, "J7").Should().Be(45);
        // West Total (Region-level subtotal for [West])
        Text(sheet, "G8").Should().Be("West Total");
        Number(sheet, "H8").Should().Be(20);
        Number(sheet, "I8").Should().Be(25);
        Number(sheet, "J8").Should().Be(45);
        // Grand Total
        Text(sheet, "G9").Should().Be("Grand Total");
        Number(sheet, "H9").Should().Be(30);
        Number(sheet, "I9").Should().Be(40);
        Number(sheet, "J9").Should().Be(70);
    }

}
