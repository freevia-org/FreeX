using System.Linq;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127B (r127 ScopeAudit follow-up to R127-services-pdf-cell-borders-1): the R127 fix taught
/// <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/> to draw <c>Format Cells &gt; Border</c>
/// edges, but <see cref="WorkbookPdfContentBuilder.BuildPage"/> -- the older, options-driven,
/// fixed-geometry sibling content-generation method -- still only drew fills, a gridline stroke, CF
/// overlays, and text; it never drew an explicit border. This method is not dead code: it is the
/// exclusive builder behind <see cref="WorkbookPdfContentBuilder.Build"/>, which
/// <see cref="PortablePdfDocumentExporter"/>'s <c>CreateDocument</c> unconditionally calls (regardless
/// of whether the caller-supplied options are null), and <see cref="PortablePdfDocumentExporter.Save"/>
/// is in turn the documented Skia-unavailable fallback wired into the Avalonia shell's Save-As-PDF
/// command (<c>AvaloniaPdfDocumentExporter.Save</c>). So on any Linux/macOS install where SkiaSharp's
/// native asset fails to load, Save-As-PDF silently dropped explicit cell borders again -- the same
/// user-visible defect R127 closed on the Skia/page-setup-aware branch.
/// </summary>
public sealed class R127B_PdfExplicitCellBorderLegacyPathTests
{
    [Fact]
    public void BuildPage_ExplicitBorder_DrawsBorderLines()
    {
        var workbook = new Workbook("Borders");
        var sheet = workbook.AddSheet("Sheet1");

        var boxedStyle = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(200, 0, 0)),
            BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(200, 0, 0)),
            BorderLeft = new CellBorder(BorderStyle.Thick, new CellColor(200, 0, 0)),
            BorderRight = new CellBorder(BorderStyle.Thick, new CellColor(200, 0, 0)),
        });
        var boxedCell = Cell.FromValue(new TextValue("Total"));
        boxedCell.StyleId = boxedStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), boxedCell);

        var exportPlan = CreateExportPlan(workbook, sheet);
        var options = new PortablePdfDocumentOptions();

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        document.Pages.Should().NotBeEmpty();

        var ops = document.Pages[0].Ops;

        // The gridline stroke (PdfStrokeRect, not PdfLine) is a fixed neutral grey, so any PdfLine op
        // carrying the border's red color can only have come from the explicit Format Cells > Border
        // resolution this test targets.
        var borderLines = ops.OfType<PdfLine>()
            .Where(l => l.Color.R == 200 && l.Color.G == 0 && l.Color.B == 0)
            .ToList();

        borderLines.Should().NotBeEmpty(
            "an explicit cell border must always be drawn by the legacy/portable-fallback PDF builder " +
            "too, matching the page-setup-aware path R127 already fixed");

        borderLines.Should().Contain(l => l.Y1 == l.Y2, "the top/bottom edges are horizontal lines");
        borderLines.Should().Contain(l => l.X1 == l.X2, "the left/right edges are vertical lines");
        borderLines.Should().OnlyContain(l => l.LineWidth == 2.5, "BorderStyle.Thick must use the 2.5pt weight");
    }

    [Fact]
    public void BuildPage_NoExplicitBorder_DrawsOnlyTheNeutralGridlineStroke_NoRedBorderLines()
    {
        // No-regression sibling: a plain cell with no explicit border must still render with its
        // existing neutral gridline PdfStrokeRect and zero border-colored PdfLine ops -- proving the
        // fix only draws lines for cells that actually carry a non-None border and does not disturb
        // the pre-existing gridline-stroke behavior this legacy path already had.
        var workbook = new Workbook("NoBorders");
        var sheet = workbook.AddSheet("Sheet1");

        var plainCell = Cell.FromValue(new TextValue("Plain"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), plainCell);

        var exportPlan = CreateExportPlan(workbook, sheet);
        var options = new PortablePdfDocumentOptions();

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        document.Pages.Should().NotBeEmpty();

        var ops = document.Pages[0].Ops;
        ops.OfType<PdfLine>().Should().BeEmpty(
            "with no explicit cell border, no PdfLine border op should be emitted at all");
        ops.OfType<PdfStrokeRect>().Should().NotBeEmpty(
            "the legacy path's per-cell neutral gridline stroke must still be drawn, unchanged");
    }

    private static PortablePdfExportPlan CreateExportPlan(Workbook workbook, Sheet sheet)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }
}
