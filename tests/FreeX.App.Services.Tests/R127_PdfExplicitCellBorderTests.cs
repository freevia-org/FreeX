using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127-services-pdf-cell-borders-1: Format Cells &gt; Border (style.BorderTop/Right/Bottom/Left) was
/// never drawn by <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/> -- the exclusive
/// content builder behind the Avalonia/Skia Save-As-PDF path
/// (<c>src/FreeX.App.Avalonia/Pdf/SkiaPdfDocumentExporter.cs</c>). Cell fills, gridlines (gated by
/// <see cref="Sheet.PrintGridlines"/>), text, CF overlays and sparklines all rendered, but an
/// explicit user-authored border silently vanished even with <c>PrintGridlines</c> off (its default,
/// matching Excel) -- exactly like the WPF native print/PDF path
/// (<c>PrintRenderer.GridCells.cs</c>'s <c>DrawPrintedBorderEdge</c>) and the on-screen Avalonia
/// print-preview model builder (<c>PageContentRenderModelBuilder.ResolveBorders</c>) both already
/// draw it.
/// </summary>
public sealed class R127_PdfExplicitCellBorderTests
{
    [Fact]
    public void BuildWithPageSetup_ExplicitBorder_DrawsBorderLines_EvenWithGridlinesOff()
    {
        var workbook = new Workbook("Borders");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintGridlines = false; // Excel's default -- must not gate explicit borders.

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

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();

        var ops = doc.Pages[0].Ops;

        // Gridlines are off, so any PdfLine op carrying the border's red color can only have come
        // from the explicit Format Cells > Border resolution -- not the (disabled) gridline pass.
        var borderLines = ops.OfType<PdfLine>()
            .Where(l => l.Color.R == 200 && l.Color.G == 0 && l.Color.B == 0)
            .ToList();

        borderLines.Should().NotBeEmpty(
            "an explicit cell border must always be drawn regardless of Sheet.PrintGridlines, matching " +
            "Excel and the sibling print-preview/WPF-print paths");

        // Four distinct edges (top/bottom horizontal, left/right vertical) -- at least one
        // horizontal (Y1 == Y2) and one vertical (X1 == X2) line must be present.
        borderLines.Should().Contain(l => l.Y1 == l.Y2, "the top/bottom edges are horizontal lines");
        borderLines.Should().Contain(l => l.X1 == l.X2, "the left/right edges are vertical lines");

        // Thick style resolves to the WPF-matching 2.5pt weight.
        borderLines.Should().OnlyContain(l => l.LineWidth == 2.5, "BorderStyle.Thick must use the 2.5pt weight");
    }

    [Fact]
    public void BuildWithPageSetup_NoExplicitBorder_DrawsNoBorderLines_GridlinesStillOff()
    {
        // No-regression sibling: a plain cell with no explicit border and PrintGridlines off must
        // still render with zero border/gridline PdfLine ops -- proving the fix only draws lines for
        // cells that actually carry a non-None border, and never falls back to always-on borders.
        var workbook = new Workbook("NoBorders");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintGridlines = false;

        var plainCell = Cell.FromValue(new TextValue("Plain"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), plainCell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();

        doc.Pages[0].Ops.OfType<PdfLine>().Should().BeEmpty(
            "with PrintGridlines off and no explicit cell border, no line ops should be emitted at all");
    }

    [Fact]
    public void BuildWithPageSetup_BlackAndWhiteMode_ForcesExplicitBorderToBlack()
    {
        // Sibling coverage for the Black-and-White-mode override this fix also mirrors from
        // PrintRenderer.GridCells.cs's DrawPrintedBorderEdge.
        var workbook = new Workbook("BwBorders");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintGridlines = false;
        sheet.PrintBlackAndWhite = true;

        var greenBorderedStyle = workbook.RegisterStyle(new CellStyle
        {
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 200, 0)),
        });
        var cell = Cell.FromValue(new TextValue("Underlined"));
        cell.StyleId = greenBorderedStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);

        var borderLines = doc.Pages[0].Ops.OfType<PdfLine>().ToList();
        borderLines.Should().NotBeEmpty("the explicit bottom border must still draw in B&W mode");
        borderLines.Should().OnlyContain(l => l.Color.R == 0 && l.Color.G == 0 && l.Color.B == 0,
            "Black-and-white mode must force every border to solid black regardless of its authored color");
    }
}
