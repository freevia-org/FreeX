using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R104: PageContentRenderModelBuilder.Build's 'Center on page &gt; Vertically' offset (yOffset) and the
/// defensive residual-overflow shrink (ResolveScaleRatio) must both measure against the sheet's actual
/// printable BODY height -- pageH minus whichever is larger of the Top Margin / Header Margin, and
/// whichever is larger of the Bottom Margin / Footer Margin (PageGeometryRules.ResolveBodyEdge) -- the
/// same body-adjusted height that already anchors the grid's top edge (contentTop) via the R99 fix, and
/// the same body-adjusted height WorkbookPdfContentBuilder.cs's contentHeight/centerYOffset already use
/// for the identical centering computation. Before this fix, yOffset was derived from the plain-margin
/// `printableH = pageH - marginTop - marginBottom`, so whenever the Header (or Footer) Margin exceeded
/// the Top (or Bottom) Margin, the centered content was pushed too far down the page.
/// </summary>
public sealed class PageContentRenderModelBuilderVerticalCenteringBodyHeightTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void R104_CenteredContentTopIsUnaffectedByTopMarginOnceHeaderMarginDominates()
    {
        // Both configurations keep HeaderMargin fixed at the sheet default (0.3in) -- the only
        // difference is Top Margin, which starts equal to the Header Margin (config A) and is then
        // reduced well below it (config B). PageGeometryRules.ResolveBodyEdge(marginTop, headerMargin)
        // selects the SAME value (the header margin) in both configs, so the sheet's real printable
        // body -- and therefore the exact centered position of a page-full of content -- must be
        // IDENTICAL in both: reducing Top Margin further, once Header Margin already governs the body's
        // top edge, must not shift centered content any further down the page.
        var (workbookA, sheetA) = CreateWorkbook();
        sheetA.CenterVerticallyOnPage = true;
        sheetA.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.3, Bottom: 0.75);
        sheetA.SetCell(new CellAddress(sheetA.Id, 1, 1), new TextValue("a"));
        var layoutA = BuildFirstPage(workbookA, sheetA)!;

        var (workbookB, sheetB) = CreateWorkbook();
        sheetB.CenterVerticallyOnPage = true;
        sheetB.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.2, Bottom: 0.75);
        sheetB.SetCell(new CellAddress(sheetB.Id, 1, 1), new TextValue("a"));
        var layoutB = BuildFirstPage(workbookB, sheetB)!;

        layoutB.GridBounds.Top.Should().BeApproximately(layoutA.GridBounds.Top, 0.01,
            "Header Margin (0.3in) already exceeds both Top Margins (0.3in and 0.2in), so the sheet's " +
            "real printable body -- and the centered content's position within it -- is identical " +
            "regardless of how much further Top Margin is reduced below it");
    }

    [Fact]
    public void R104_NoRegression_CenteredContentTopUsesPlainMarginsWhenMarginsAlreadyDominateHeaderFooter()
    {
        // Sibling/no-regression case: the ordinary, overwhelmingly common configuration where Top/Bottom
        // Margins (Normal preset: 0.75in) already exceed the default Header/Footer Margins (0.3in), so
        // PageGeometryRules.ResolveBodyEdge picks the plain margin on both edges and bodyHeight must
        // equal the plain-margin printableH exactly -- this fix must not perturb the pre-existing,
        // correct behavior for the common case.
        var (workbook, sheet) = CreateWorkbook();
        sheet.CenterVerticallyOnPage = true;
        sheet.PageMargins = WorksheetPageMargins.Normal; // Top/Bottom 0.75in, both > the 0.3in header/footer default
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var layout = BuildFirstPage(workbook, sheet)!;

        var pageSize = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        const double dpi = PageContentRenderModelBuilder.Dpi;
        var pageH = pageSize.Height * dpi;
        var marginTopPx = sheet.PageMargins.Top * dpi;
        var marginBottomPx = sheet.PageMargins.Bottom * dpi;
        var expectedBodyHeight = pageH - marginTopPx - marginBottomPx;

        var printedHeight = layout.GridBounds.Height; // single unscaled row, no headings
        var expectedYOffset = Math.Max(0, (expectedBodyHeight - printedHeight) / 2);
        var expectedContentTop = marginTopPx + expectedYOffset;

        layout.GridBounds.Top.Should().BeApproximately(expectedContentTop, 0.01,
            "when the plain margin already exceeds the header/footer margin on both edges, centering " +
            "must reduce to exactly the pre-existing plain-margin formula");
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
