using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PageAxisPaginationParityTests
{
    [Fact]
    public void PreviewAndPdfPagination_MixedAxisRulesProduceIdenticalPagePlans()
    {
        var workbook = new Workbook("Parity");
        var sheet = workbook.AddSheet("Sheet1");
        var printRange = GridRange.Parse("A1:H12", sheet.Id);

        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 2);

        sheet.RowHeights[1] = 40.0;
        sheet.RowHeights[2] = 10_000.0;
        sheet.RowHeights[3] = 700.0;
        sheet.RowHeights[4] = 400.0;
        sheet.HiddenRows.UnionWith([2u, 6u]);
        sheet.RowPageBreaks.UnionWith([4u, 8u]);

        sheet.ColumnWidths[2] = 10_000.0;
        sheet.ColumnWidths[3] = 80.0;
        sheet.ColumnWidths[4] = 40.0;
        sheet.HiddenCols.UnionWith([2u, 5u]);
        sheet.ColumnPageBreaks.UnionWith([4u, 7u]);

        var previewPlan = PagePaginationPlanner.BuildPlan(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowHeights,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths,
            sheet.DefaultColumnWidth,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks,
            sheet.IsRowEffectivelyHidden,
            sheet.IsColEffectivelyHidden);

        var pdfPagination = SheetPdfPageSetupResolver.ResolvePagination(sheet, printRange);
        var pdfRowPlans = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            sheet.PrintTitleRows,
            pdfPagination.Capacity.RowsPerPage,
            pdfPagination.RowBreaks,
            sheet.IsRowEffectivelyHidden);
        var pdfColumnPlans = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            sheet.PrintTitleColumns,
            pdfPagination.Capacity.ColumnsPerPage,
            pdfPagination.ColumnBreaks,
            sheet.IsColEffectivelyHidden);

        pdfRowPlans.Should().BeEquivalentTo(previewPlan.RowPlans, options => options.WithStrictOrdering());
        pdfColumnPlans.Should().BeEquivalentTo(previewPlan.ColumnPlans, options => options.WithStrictOrdering());

        pdfRowPlans.Select(plan => plan.BodyRows).Should().BeEquivalentTo(
            new uint[][] { [3], [4, 5, 7], [8, 9, 10, 11, 12] },
            options => options.WithStrictOrdering());
        pdfColumnPlans.Select(plan => plan.BodyColumns).Should().BeEquivalentTo(
            new uint[][] { [3], [4, 6], [7, 8] },
            options => options.WithStrictOrdering());
    }
}
