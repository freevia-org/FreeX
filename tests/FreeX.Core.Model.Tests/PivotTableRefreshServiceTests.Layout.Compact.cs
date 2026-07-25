using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    // EXAMPLE A — 2 row fields, NO subtotals.
    // SeedSalesData: Region/Quarter/Amount (East Q1=10, East Q2=15, West Q1=20, West Q2=25)
    // RowFields=[Region(0),Quarter(1)], DataField "Sum of Amount"(2,sum), target E2.
    //
    // E2 "Row Labels"   F2 "Sum of Amount"
    // E3 "East"         F3 (blank)         indent 0
    // E4 "Q1"           F4 10              indent 1 (= 1 * indentStep, indentStep defaults to 1)
    // E5 "Q2"           F5 15              indent 1
    // E6 "West"         F6 (blank)         indent 0
    // E7 "Q1"           F7 20              indent 1
    // E8 "Q2"           F8 25              indent 1
    // E9 "Grand Total"  F9 70              indent 0
    [Fact]
    public void Refresh_CompactReportLayoutUsesSingleRowLabelColumn()
    {
        var workbook = new Workbook("PivotCompactLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G10"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = false // R90-render-pivot-layout-5-1: model default flipped to true; this test's
                                  // EXAMPLE A scenario is deliberately subtotal-free to isolate compact
                                  // single-column rendering, so pin it explicitly.
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Header row
        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        // East header row (level 0, no data value)
        Text(sheet, "E3").Should().Be("East");
        sheet.GetCell(Addr(sheet, "F3")).Should().BeNull();
        // East Q1 leaf row
        Text(sheet, "E4").Should().Be("Q1");
        Number(sheet, "F4").Should().Be(10);
        // East Q2 leaf row
        Text(sheet, "E5").Should().Be("Q2");
        Number(sheet, "F5").Should().Be(15);
        // West header row (level 0, no data value)
        Text(sheet, "E6").Should().Be("West");
        sheet.GetCell(Addr(sheet, "F6")).Should().BeNull();
        // West Q1 leaf row
        Text(sheet, "E7").Should().Be("Q1");
        Number(sheet, "F7").Should().Be(20);
        // West Q2 leaf row
        Text(sheet, "E8").Should().Be("Q2");
        Number(sheet, "F8").Should().Be(25);
        // Grand Total
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "F9").Should().Be(70);
        // Third column is unused (compact uses single label column)
        sheet.GetCell(Addr(sheet, "G4")).Should().BeNull();
    }

    // EXAMPLE A with CompactRowLabelIndent=3:
    // - Non-leaf header rows (E3 "East", E6 "West") → indent 0
    // - Leaf rows (E4 "Q1", E5 "Q2", E7 "Q1", E8 "Q2") → indent (N-1)*3 = 1*3 = 3
    // - Data value column (F4 etc.) → indent 0
    [Fact]
    public void Refresh_CompactReportLayoutAppliesConfiguredRowLabelIndent()
    {
        var workbook = new Workbook("PivotCompactIndentRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G10"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 3,
            ShowSubtotals = false // R90-render-pivot-layout-5-1: model default flipped to true; this test's
                                  // EXAMPLE A scenario is deliberately subtotal-free to isolate the indent
                                  // calculation, so pin it explicitly.
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // E3 = "East" (header, level 0) → indent 0
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId).IndentLevel.Should().Be(0);
        // E4 = "Q1" (leaf, level 1) → indent 1 * 3 = 3
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId).IndentLevel.Should().Be(3);
        // E5 = "Q2" (leaf, level 1) → indent 3
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId).IndentLevel.Should().Be(3);
        // Data value column has indent 0
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F4"))!.StyleId).IndentLevel.Should().Be(0);
    }

    // EXAMPLE B — 3 row fields WITH bottom subtotals.
    // SeedSalesChannelData: Region/Quarter/Channel/Amount (8 rows), target F2, indentStep=1.
    //
    // F3 "East"           (blank)     indent 0
    // F4 "Q1"             (blank)     indent 1
    // F5 "Retail"         G5 10       indent 2
    // F6 "Wholesale"      G6 15       indent 2
    // F7 "Q1 Total"       G7 25       indent 1
    // F8 "Q2"             (blank)     indent 1
    // F9 "Retail"         G9 20       indent 2
    // F10 "Wholesale"     G10 25      indent 2
    // F11 "Q2 Total"      G11 45      indent 1
    // F12 "East Total"    G12 70      indent 0
    // F13 "West"          (blank)     indent 0
    // F14 "Q1"            (blank)     indent 1
    // F15 "Retail"        G15 30      indent 2
    // F16 "Wholesale"     G16 35      indent 2
    // F17 "Q1 Total"      G17 65      indent 1
    // F18 "Q2"            (blank)     indent 1
    // F19 "Retail"        G19 40      indent 2
    // F20 "Wholesale"     G20 45      indent 2
    // F21 "Q2 Total"      G21 85      indent 1
    // F22 "West Total"    G22 150     indent 0
    // F23 "Grand Total"   G23 220     indent 0
    [Fact]
    public void Refresh_CompactReportLayoutUsesSubtotaledFieldCaptionForNestedSubtotals()
    {
        var workbook = new Workbook("PivotCompactNestedSubtotalCaptionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "H24"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1: pin the (former) Bottom default -- this test's "X Total"
            // assertions expect the subtotal after the leaf rows.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // East group
        Text(sheet, "F3").Should().Be("East");
        // East Q1 group
        Text(sheet, "F4").Should().Be("Q1");
        // East Q1 leaf rows
        Text(sheet, "F5").Should().Be("Retail");
        Number(sheet, "G5").Should().Be(10);
        Text(sheet, "F6").Should().Be("Wholesale");
        Number(sheet, "G6").Should().Be(15);
        // East Q1 subtotal (innermost subtotaled level = level 1 = Quarter)
        Text(sheet, "F7").Should().Be("Q1 Total");
        Number(sheet, "G7").Should().Be(25);
        // East Q2 group
        Text(sheet, "F8").Should().Be("Q2");
        // East Q2 leaf rows
        Text(sheet, "F9").Should().Be("Retail");
        Number(sheet, "G9").Should().Be(20);
        Text(sheet, "F10").Should().Be("Wholesale");
        Number(sheet, "G10").Should().Be(25);
        // East Q2 subtotal
        Text(sheet, "F11").Should().Be("Q2 Total");
        Number(sheet, "G11").Should().Be(45);
        // East Total (outermost subtotaled level = level 0 = Region)
        Text(sheet, "F12").Should().Be("East Total");
        Number(sheet, "G12").Should().Be(70);
        // West group
        Text(sheet, "F13").Should().Be("West");
        // West Q1 group
        Text(sheet, "F14").Should().Be("Q1");
        // West Q1 leaf rows
        Text(sheet, "F15").Should().Be("Retail");
        Number(sheet, "G15").Should().Be(30);
        Text(sheet, "F16").Should().Be("Wholesale");
        Number(sheet, "G16").Should().Be(35);
        // West Q1 subtotal
        Text(sheet, "F17").Should().Be("Q1 Total");
        Number(sheet, "G17").Should().Be(65);
        // West Q2 group
        Text(sheet, "F18").Should().Be("Q2");
        // West Q2 leaf rows
        Text(sheet, "F19").Should().Be("Retail");
        Number(sheet, "G19").Should().Be(40);
        Text(sheet, "F20").Should().Be("Wholesale");
        Number(sheet, "G20").Should().Be(45);
        // West Q2 subtotal
        Text(sheet, "F21").Should().Be("Q2 Total");
        Number(sheet, "G21").Should().Be(85);
        // West Total
        Text(sheet, "F22").Should().Be("West Total");
        Number(sheet, "G22").Should().Be(150);
        // Grand Total
        Text(sheet, "F23").Should().Be("Grand Total");
        Number(sheet, "G23").Should().Be(220);
    }

}
