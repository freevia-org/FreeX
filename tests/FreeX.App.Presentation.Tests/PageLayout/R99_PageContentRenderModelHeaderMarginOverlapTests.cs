using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R99-presentation-header-band-preview-1: Excel treats Header/Footer margin as the distance from
/// the page edge to the header/footer text band, which sits WITHIN the Top/Bottom margin band as
/// long as it doesn't exceed it -- but once a Header/Footer margin IS larger than its corresponding
/// Top/Bottom margin, the printed grid must be pushed down/up so it never overlaps the header/footer
/// text. <see cref="PagePaginationPlanner"/> already reserves fewer rows per page for this case
/// (Math.Max(margins.Top, headerMarginInches)), and <c>PrintRenderer.HeaderFooter.cs</c> (WPF) /
/// <c>WorkbookPdfContentBuilder</c> (PDF export) were fixed the same round -- but this print-PREVIEW
/// content model (<see cref="PageContentRenderModelBuilder.Build"/>, the geometry
/// <see cref="PrintPreviewInstructionBuilder"/> paints onto the actual Avalonia/every-shell preview
/// canvas) computed <c>contentTop</c> from the plain top margin only, so the preview's own drawn grid
/// (<see cref="PageContentLayout.GridBounds"/>) started above where the header band
/// (<see cref="PageContentLayout.HeaderRuns"/>) was anchored, even though the row CAPACITY for this
/// same page had already shrunk to make room.
/// </summary>
public sealed class R99_PageContentRenderModelHeaderMarginOverlapTests
{
    private static readonly FakeTextMeasurer Measurer = new();
    private const double Dpi = PageContentRenderModelBuilder.Dpi;

    [Fact]
    public void Build_HeaderMarginExceedsTopMargin_GridTopStartsAtHeaderMargin_NotPlainTopMargin()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
        sheet.PageHeader = new WorksheetHeaderFooter("", "HDR", "");

        // Header margin (0.6in) deliberately larger than the top margin (0.2in) -- the scenario the
        // pagination fix already handles for row capacity, but which this print-preview content model
        // used to ignore when positioning the actual drawn grid.
        sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.2, Bottom: 0.75);
        sheet.HeaderMargin = 0.6;
        sheet.FooterMargin = 0.3;

        var layout = BuildFirstPage(workbook, sheet)!;

        var expectedGridTopPx = Math.Max(0.2, 0.6) * Dpi;
        layout.GridBounds.Top.Should().BeApproximately(expectedGridTopPx, 1.0,
            "the grid's top edge must land at max(topMargin, headerMargin), not the smaller plain top margin");

        // Real-geometry no-overlap assertion: the header band's own bottom edge (the actual draw
        // rect PrintPreviewInstructionBuilder paints from) must never sit below (a larger Y than,
        // in this top-left/y-down space) the grid's top edge.
        var headerRun = layout.HeaderRuns.Should().Contain(r => r.Text.Contains("HDR")).Subject;
        headerRun.Bounds.Bottom.Should().BeLessThanOrEqualTo(layout.GridBounds.Top + 0.5,
            "the header band must sit at or above the grid's top edge, never inside/overlapping the first printed row");
    }

    [Fact]
    public void Build_TopMarginExceedsHeaderMargin_GridStillStartsAtPlainTopMargin()
    {
        // No-regression sibling: the common/default case (top margin already the larger of the two)
        // must resolve to the plain top margin exactly as before.
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
        sheet.PageHeader = new WorksheetHeaderFooter("", "HDR", "");
        sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.75);
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.GridBounds.Top.Should().BeApproximately(0.75 * Dpi, 1.0,
            "with the default/normal margins, the grid's top edge should still sit at the plain top margin");
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
