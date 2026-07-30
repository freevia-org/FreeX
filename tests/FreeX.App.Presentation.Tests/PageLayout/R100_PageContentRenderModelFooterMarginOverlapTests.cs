using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R100-presentation-footer-margin-overlap-1: mirror-image of
/// R99-presentation-header-band-preview-1. Excel treats Footer margin as the distance from the page
/// edge to the footer TEXT band, which sits WITHIN the Bottom margin band as long as it doesn't
/// exceed it -- but once Footer margin IS larger than the Bottom margin, the footer text must stay
/// below the printed grid's own bottom edge (<c>pageH - Math.Max(bottomMargin, footerMargin)</c>, the
/// same <c>bodyBottomInches</c> PagePaginationPlanner already used to size this page's row capacity)
/// rather than climbing up into the grid. This print-PREVIEW content model
/// (<see cref="PageContentRenderModelBuilder.Build"/>, the geometry
/// <see cref="PrintPreviewInstructionBuilder"/> paints onto the actual preview canvas on every shell)
/// computed <c>footerY</c> purely from <c>pageH - footerMargin - lineHeight</c> with no reference to
/// the grid's own bottom edge, so whenever FooterMargin exceeded BottomMargin the footer band landed
/// inside the grid's own vertical span, even though R99 already fixed the mirror-image header case in
/// this same file.
/// </summary>
public sealed class R100_PageContentRenderModelFooterMarginOverlapTests
{
    private static readonly FakeTextMeasurer Measurer = new();
    private const double Dpi = PageContentRenderModelBuilder.Dpi;
    private const double LineHeight = 16.0; // matches BuildHeaderFooterRuns' internal const

    [Fact]
    public void Build_FooterMarginExceedsBottomMargin_FooterStaysAtOrBelowGridBottomEdge()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
        sheet.PageFooter = new WorksheetHeaderFooter("", "FTR", "");

        // Footer margin (1.5in) deliberately larger than the bottom margin (0.2in) -- the
        // mirror-image scenario of the R99 header-side bug case, but on the footer/bottom side.
        sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.2);
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 1.5;

        var layout = BuildFirstPage(workbook, sheet)!;

        var pageH = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation).Height * Dpi;
        var gridBottomEdge = pageH - Math.Max(sheet.PageMargins.Bottom * Dpi, sheet.FooterMargin * Dpi);

        var footerRun = layout.FooterRuns.Should().Contain(r => r.Text.Contains("FTR")).Subject;

        footerRun.Bounds.Top.Should().BeGreaterThanOrEqualTo(gridBottomEdge - 0.5,
            "the footer band must sit at or below the grid's own bottom edge " +
            "(max(bottomMargin, footerMargin) from the page top), never above/inside it");
    }

    [Fact]
    public void Build_BottomMarginExceedsFooterMargin_FooterStillAnchorsAtPlainFooterMargin()
    {
        // No-regression sibling: the common/default case (bottom margin already the larger of the
        // two) must resolve to the plain, unclamped footer position exactly as before.
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
        sheet.PageFooter = new WorksheetHeaderFooter("", "FTR", "");
        sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.75);
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;

        var layout = BuildFirstPage(workbook, sheet)!;

        var pageH = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation).Height * Dpi;
        var expectedFooterY = pageH - (sheet.FooterMargin * Dpi) - LineHeight;

        var footerRun = layout.FooterRuns.Should().Contain(r => r.Text.Contains("FTR")).Subject;

        footerRun.Bounds.Top.Should().BeApproximately(expectedFooterY, 1.0,
            "with the default/normal margins, the footer band should still sit at the plain, " +
            "unclamped position");
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
