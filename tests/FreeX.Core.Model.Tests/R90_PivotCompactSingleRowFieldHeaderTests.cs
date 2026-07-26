using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R90-render-pivot-layout-5-4: real Excel's Compact report layout always shows the fixed "Row
// Labels" caption above the row-label column, whether the pivot has one row field or several.
// FreeX previously gated that caption on rowFields.Count > 1, so a Compact pivot with exactly one
// row field wrote the field's OWN source header text (e.g. "Region") instead — the universally
// recognizable default single-field pivot table look was wrong. Exercised through the real product
// entry point, PivotTableRefreshService.Refresh, used by every pivot create/refresh command.
public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void R90_Refresh_CompactSingleRowFieldUsesRowLabelsHeader_RowOnlyPivot()
    {
        var workbook = new Workbook("PivotCompactSingleRowFieldTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "F10"),
            ReportLayout = PivotReportLayout.Compact
        };
        // Exactly ONE row field (Region) and no column fields — routes through WriteRowPivot,
        // the row-only writer named in the finding.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Excel's fixed caption — NOT the source field's own name ("Region").
        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Sum of Amount");
    }

    [Fact]
    public void R90_Refresh_CompactSingleRowFieldUsesRowLabelsHeader_MatrixPivot()
    {
        var workbook = new Workbook("PivotCompactSingleRowFieldMatrixTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8"),
            ReportLayout = PivotReportLayout.Compact
        };
        // Exactly ONE row field (Region) but WITH a column field — routes through the row+column
        // matrix writer (PivotTableRefreshService.MatrixWriter.cs), which repeats the same gate.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
    }

    // No-regression sibling: the multi-row-field case (already correctly showing "Row Labels"
    // before this fix, since rowFields.Count > 1 was already true there) must keep working.
    [Fact]
    public void R90_Refresh_CompactMultiRowFieldStillUsesRowLabelsHeader()
    {
        var workbook = new Workbook("PivotCompactMultiRowFieldTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G10"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
    }
}
