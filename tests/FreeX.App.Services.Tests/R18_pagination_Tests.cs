using System.Reflection;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R18-print-pagination-exact-2: WorkbookPdfContentBuilder.ComputeActualGridSizes's defensive
/// fit-to-page shrink must apply a SINGLE uniform scale to BOTH axes -- the smaller of the width and
/// height overflow ratios -- mirroring PrintRenderer.HeaderFooter.cs's uniform scaleRatio (and
/// Excel's own fit-to-page behavior). Pre-fix, width and height were shrunk independently: a page
/// whose columns alone overflowed the available width (but whose rows already fit the available
/// height) only shrank the columns, squishing them while the rows kept their full scaled height --
/// distorting the printed grid's aspect ratio.
/// </summary>
public sealed class R18_pagination_Tests
{
    [Fact]
    public void ComputeActualGridSizes_WidthOnlyOverflow_ShrinksBothAxesByTheSameUniformRatio()
    {
        var workbook = new Workbook("W");
        var sheet = workbook.AddSheet("S");

        const uint columnCount = 5;
        const uint rowCount = 5;
        const double wideColumnChars = 100.0; // very wide -> real px/pt width overflows availableWidth
        const double normalRowHeightPx = 20.0; // normal -> real px/pt height comfortably fits availableHeight

        for (var c = 1u; c <= columnCount; c++)
            sheet.ColumnWidths[c] = wideColumnChars;
        for (var r = 1u; r <= rowCount; r++)
            sheet.RowHeights[r] = normalRowHeightPx;

        var rows = Enumerable.Range(1, (int)rowCount)
            .Select(r => new PortablePdfPageRow((uint)r, PortablePdfPageAxisRole.Body))
            .ToArray();
        var columns = Enumerable.Range(1, (int)columnCount)
            .Select(c => new PortablePdfPageColumn((uint)c, PortablePdfPageAxisRole.Body))
            .ToArray();
        var contentPlan = new PortablePdfPageContentPlan(
            Status: PortablePdfPageContentPlanStatus.Ready,
            StatusText: "",
            PageRequest: null,
            Rows: rows,
            Columns: columns,
            Cells: []);

        // availableWidth is small enough that the very wide columns overflow it heavily; availableHeight
        // is generous enough that the normal-height rows comfortably fit -- an overflow on ONE axis only.
        const double availableWidth = 200.0;
        const double availableHeight = 2000.0;

        var method = typeof(WorkbookPdfContentBuilder).GetMethod(
            "ComputeActualGridSizes", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("WorkbookPdfContentBuilder.ComputeActualGridSizes must still exist under that name");

        var resultObj = method!.Invoke(null, [sheet, contentPlan, availableWidth, availableHeight, 1.0]);
        var (colWidthsPt, rowHeightsPt) = ((double[] ColWidths, double[] RowHeights))resultObj!;

        const double ptPerPx = SheetPdfPageSetupResolver.PdfPointsPerInch / 96.0;
        var unscaledColWidthPt = Math.Max(4.0, ColumnWidthPixelMapper.ColumnWidthToPixels(wideColumnChars)) * ptPerPx;
        var unscaledRowHeightPt = Math.Max(1.0, normalRowHeightPx * ptPerPx);

        var actualWidthRatio = colWidthsPt[0] / unscaledColWidthPt;
        var actualHeightRatio = rowHeightsPt[0] / unscaledRowHeightPt;

        // Width alone overflows -> some shrink is required.
        actualWidthRatio.Should().BeLessThan(1.0, "the very wide columns overflow availableWidth and must shrink");
        // R18-print-pagination-exact-2: the SAME ratio must apply to the row axis too, even though the
        // rows alone would already fit inside availableHeight -- proving a single uniform scale is used,
        // not an independent per-axis one that would leave rowHeightsPt unshrunk (ratio == 1.0).
        actualHeightRatio.Should().BeApproximately(actualWidthRatio, precision: 1e-9,
            "PrintRenderer.HeaderFooter.cs applies one uniform Math.Min scale to both axes; shrinking " +
            "width and height independently distorts the aspect ratio of every printed cell");
    }
}
