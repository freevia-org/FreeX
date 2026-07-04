using FluentAssertions;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for the M-autofit review group (findings J20, J21, J57):
///   J20 - AutoFit Row Height must account for WrapText, sizing wrapped single-line content to
///         however many visual lines it occupies at the current column width.
///   J21 - AutoFit measurement must exclude cells that are part of a merged region (Excel never
///         lets a merged cell's content grow row/column sizing).
///   J57 - AutoFit measurement must exclude effectively-hidden rows/columns from the content scan.
/// </summary>
public sealed class MAutoFitFixesTests
{
    [Fact]
    public void EstimateRowHeight_WrapTextEnabled_WrapsAtColumnWidth_AndGrowsRowHeight()
    {
        // 60-char single logical line (no '\n'), WrapText on, narrow column (width 8.43 like Excel's
        // default). Usable width per line = floor(8.43 - 2.0) = 6 chars, so 60 chars wrap onto
        // ceil(60/6) = 10 visual lines.
        var longSentence = new string('x', 60);
        var wrapped = new AutoFitCellText(longSentence, WrapText: true, ColumnWidth: 8.43);

        var height = AutoFitSizingService.EstimateRowHeight([wrapped], defaultHeight: 20);

        var lineHeight = Math.Max(20, AutoFitSizingService.MinimumRowHeight);
        var expectedUnclamped = 10 * lineHeight;
        height.Should().Be(Math.Clamp(expectedUnclamped, AutoFitSizingService.MinimumRowHeight, AutoFitSizingService.MaximumRowHeight));
        height.Should().BeGreaterThan(20); // must have grown past the single-line default
    }

    [Fact]
    public void EstimateRowHeight_SameTextWithoutWrapText_StaysAtSingleLineHeight()
    {
        // Same long single-line text, but WrapText off: Excel does not grow the row for text that
        // simply overflows visually into the next cell (no wrap = no extra lines).
        var longSentence = new string('x', 60);
        var noWrap = new AutoFitCellText(longSentence, WrapText: false, ColumnWidth: 8.43);

        var height = AutoFitSizingService.EstimateRowHeight([noWrap], defaultHeight: 20);

        height.Should().Be(20);
    }

    [Fact]
    public void EstimateRowHeight_WrapTextWithEmbeddedNewlines_WrapsEachLogicalLineSeparately()
    {
        // Two logical lines via '\n', the second one long enough to itself wrap at the column width.
        var text = "short\n" + new string('y', 30);
        var cellText = new AutoFitCellText(text, WrapText: true, ColumnWidth: 8.43);

        var height = AutoFitSizingService.EstimateRowHeight([cellText], defaultHeight: 20);

        // "short" -> 1 visual line; 30 chars at usable width 6 -> ceil(30/6) = 5 visual lines. Total 6.
        var lineHeight = Math.Max(20, AutoFitSizingService.MinimumRowHeight);
        var expected = Math.Clamp(6 * lineHeight, AutoFitSizingService.MinimumRowHeight, AutoFitSizingService.MaximumRowHeight);
        height.Should().Be(expected);
    }

    [Fact]
    public void PlanAutoFitRowHeights_WrapTextCellInNarrowColumn_GrowsRowPastSingleLineDefault()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.DefaultRowHeight = 20;
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var longSentence = new string('z', 60);

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText(longSentence, WrapText: true),
            defaultHeight: sheet.DefaultRowHeight);

        plans.Should().ContainSingle();
        plans[0].Size.Should().BeGreaterThan(sheet.DefaultRowHeight);
    }

    [Fact]
    public void PlanAutoFitColumnWidths_MergedAnchorCell_IsExcludedFromMeasurement()
    {
        // A1:F1 merged with a long title stored on the anchor cell A1. AutoFit column A alone must
        // not inflate to the merged title's length (Excel excludes merged cells from AutoFit).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6)));

        var longTitle = new string('t', 50);
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => (row, col) == (1u, 1u) ? new AutoFitCellText(longTitle) : null,
            defaultWidth: sheet.DefaultColumnWidth);

        plans.Should().ContainSingle();
        plans[0].Size.Should().Be(sheet.DefaultColumnWidth); // no content contributed -> stays default
    }

    [Fact]
    public void PlanAutoFitRowHeights_MergedRowCell_IsExcludedFromMeasurement()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6)));

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6));

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => (row, col) == (1u, 1u) ? new AutoFitCellText("first\nsecond\nthird\nfourth") : null,
            defaultHeight: sheet.DefaultRowHeight);

        plans.Should().ContainSingle();
        plans[0].Size.Should().Be(sheet.DefaultRowHeight); // merged anchor's multi-line text must not grow the row
    }

    [Fact]
    public void PlanAutoFitColumnWidths_HiddenRow_IsExcludedFromMeasurement()
    {
        // Row 5 is hidden and holds an unusually long value; AutoFit column C must ignore it.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.HiddenRows.Add(5);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 10, 3));
        var longValue = new string('h', 40);

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => row == 5 && col == 3 ? new AutoFitCellText(longValue) : null,
            defaultWidth: sheet.DefaultColumnWidth);

        plans.Should().ContainSingle();
        plans[0].Size.Should().Be(sheet.DefaultColumnWidth); // hidden row's content must not widen the column
    }

    [Fact]
    public void PlanAutoFitRowHeights_HiddenColumn_IsExcludedFromMeasurement()
    {
        // Column D is hidden and holds long/multi-line content; AutoFit row 1 must ignore it.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.HiddenCols.Add(4);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5));

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => row == 1 && col == 4 ? new AutoFitCellText("a\nb\nc\nd\ne") : null,
            defaultHeight: sheet.DefaultRowHeight);

        plans.Should().ContainSingle();
        plans[0].Size.Should().Be(sheet.DefaultRowHeight); // hidden column's multi-line content must not grow the row
    }

    [Fact]
    public void PlanAutoFitColumnWidths_GroupCollapsedHiddenRow_IsAlsoExcluded()
    {
        // Excel's "effectively hidden" concept also covers group-collapsed rows, not just manual hide.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.GroupHiddenRows.Add(5);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 10, 3));
        var longValue = new string('g', 40);

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => row == 5 && col == 3 ? new AutoFitCellText(longValue) : null,
            defaultWidth: sheet.DefaultColumnWidth);

        plans.Should().ContainSingle();
        plans[0].Size.Should().Be(sheet.DefaultColumnWidth);
    }
}
