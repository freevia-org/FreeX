using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    // freex-pivot F1: a Compact-layout matrix pivot (row fields > 1, at least one column field) with
    // its DEFAULT report layout collapses every row-field level into a single joined label cell (see
    // PivotTableRefreshService.MatrixWriter.cs, "string.Join(\" \", rowGroup.Key.Values)"). When a
    // row-field item's own text contains a space (e.g. Region = "New York"), a naive
    // label.Split(' ') can no longer recover the individual field values, and Show Details must fall
    // back to reconstructing the combination from the source data instead of returning nothing (or
    // silently matching the wrong field boundaries).
    private static void SeedSalesChannelDataWithSpacedRegion(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("New York"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("New York"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(5));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("New York"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(8));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("Boston"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(20));
    }

    private static PivotTableModel BuildSpacedRegionMatrixPivot(Sheet sheet)
    {
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "E2", "H8"),
            // ReportLayout left at its default (Compact) -- this finding is specifically about the
            // default configuration; see PivotTableModel.ReportLayout.
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        return pivot;
    }

    [Fact]
    public void ExtractDetailRows_ForCompactMatrixLeafRow_WithSpaceInRowFieldText_ReturnsMatchingSourceRow()
    {
        var workbook = new Workbook("PivotCompactMatrixSpacedRegionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelDataWithSpacedRegion(sheet);
        var pivot = BuildSpacedRegionMatrixPivot(sheet);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Sanity: the compact matrix writer really did collapse the row fields into one joined
        // label containing an embedded space from the Region text itself.
        Text(sheet, "E4").Should().Be("New York Q1");

        var q1Detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F4"));
        q1Detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText)))
            .Should().BeEquivalentTo(["New York|Q1|Retail|10"]);

        // A second leaf row sharing the same spaced Region text but a different Quarter must resolve
        // to its OWN field boundary, not silently match the "Q1" row's data (the failure mode called
        // out in the finding: a coincidental token-count match can select the wrong field values).
        Text(sheet, "E5").Should().Be("New York Q2");
        var q2Detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F5"));
        q2Detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText)))
            .Should().BeEquivalentTo(["New York|Q2|Retail|8"]);
    }

    [Fact]
    public void ExtractDetailRows_ForCompactMatrixLeafRow_WithoutSpaceInRowFieldText_StillReturnsMatchingSourceRow()
    {
        // Sibling/no-regression case: a leaf row whose joined compact label happens to split into
        // exactly rowFieldCount single-word tokens (no field text contains a space) must keep working
        // exactly as before.
        var workbook = new Workbook("PivotCompactMatrixSpacedRegionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelDataWithSpacedRegion(sheet);
        var pivot = BuildSpacedRegionMatrixPivot(sheet);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Boston Q1");
        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F3"));
        detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText)))
            .Should().BeEquivalentTo(["Boston|Q1|Retail|20"]);
    }
}
