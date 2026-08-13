using System.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-45 fixes: the WPF print/PDF/XPS renderer used to hardcode every printed cell to a fixed
/// 9pt Segoe UI, non-bold, non-italic (R45-roundtrip-not-consumed-sweep-1); forced every
/// non-rotated cell to a single ellipsis-truncated line regardless of
/// <see cref="CellStyle.WrapText"/> (R45-roundtrip-not-consumed-sweep-2); and never applied the
/// same shrink-to-fit font reduction the interactive grid uses for
/// <see cref="CellStyle.ShrinkToFit"/> (R45-roundtrip-not-consumed-sweep-3). These tests exercise
/// <see cref="PrintRenderer.RenderWorksheet"/> end to end and inspect the extracted
/// <see cref="PdfTextOverlay"/> (the same font/size/bold/italic/text metadata that both the drawn
/// glyphs and the PDF's selectable text layer are built from) to pin the fixed behaviour.
/// </summary>
public sealed class R45_PrintRendererCellStyleTests
{
    [Fact]
    public void RenderWorksheet_UsesCellFontSizeBoldItalicNameInsteadOfFixedPrintFont()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Styled title print");
            var sheet = workbook.AddSheet("Sheet1");
            var titleStyle = workbook.RegisterStyle(new CellStyle
            {
                FontSize = 24,
                Bold = true,
                Italic = true,
                FontName = "Georgia"
            });
            var cell = Cell.FromValue(new TextValue("Quarterly Report"));
            cell.StyleId = titleStyle;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;

            // 24pt converted to WPF DIPs the same way the interactive grid does (pt * 96/72).
            overlay.FontSize.Should().BeApproximately(24.0 * 96.0 / 72.0, 0.01);
            overlay.FontFamily.Should().Be("Georgia");
            overlay.Bold.Should().BeTrue();
            overlay.Italic.Should().BeTrue();
        });
    }

    [Fact]
    public void RenderWorksheet_PlainCellFallsBackToExcelDefaultCalibri11NotFixedPrintFont()
    {
        // No-regression sibling: an unstyled cell still falls back to Excel's own default
        // (Calibri, 11pt, non-bold/non-italic) -- the same default the interactive grid uses --
        // instead of the old fixed 9pt Segoe UI print-only font.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Plain cell print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Plain"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;

            overlay.FontSize.Should().BeApproximately(11.0 * 96.0 / 72.0, 0.01);
            overlay.FontFamily.Should().Be("Calibri");
            overlay.Bold.Should().BeFalse();
            overlay.Italic.Should().BeFalse();
        });
    }

    [Fact]
    public void RenderWorksheet_WrapTextCellPrintsFullTextInsteadOfSingleTruncatedLine()
    {
        StaTestRunner.Run(() =>
        {
            var text = string.Join(" ", Enumerable.Repeat("word", 40));

            var (wrapWorkbook, wrapSheetId) = BuildOverflowCandidateWorkbook(text, wrapText: true);
            var wrapDocument = PrintRenderer.RenderWorksheet(wrapWorkbook, wrapSheetId, new ViewportService());
            var wrapPage = wrapDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var wrapOverlay = PdfTextOverlayExtractor.Extract(wrapPage)
                .Should().ContainSingle(o => o.Text.StartsWith("word", StringComparison.Ordinal)).Subject;

            var (noWrapWorkbook, noWrapSheetId) = BuildOverflowCandidateWorkbook(text, wrapText: false);
            var noWrapDocument = PrintRenderer.RenderWorksheet(noWrapWorkbook, noWrapSheetId, new ViewportService());
            var noWrapPage = noWrapDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var noWrapOverlay = PdfTextOverlayExtractor.Extract(noWrapPage)
                .Should().ContainSingle(o => o.Text.EndsWith("\u2026", StringComparison.Ordinal)).Subject;

            // The old behaviour hard-ellipsized every non-rotated cell regardless of WrapText;
            // the fix keeps that truncation for the non-wrap sibling but prints (and exposes via
            // the PDF text layer) the cell's full text once WrapText is honored.
            wrapOverlay.Text.Should().Be(text);
            noWrapOverlay.Text.Should().EndWith("\u2026");
        });
    }

    [Fact]
    public void RenderWorksheet_ShrinkToFitCellReducesFontBelowRequestedSizeWhenTextOverflows()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Shrink to fit print");
            var sheet = workbook.AddSheet("Sheet1");
            var shrinkStyle = workbook.RegisterStyle(new CellStyle
            {
                FontSize = 20,
                ShrinkToFit = true
            });
            var cell = Cell.FromValue(new TextValue("A very long value that overflows the column"));
            cell.StyleId = shrinkStyle;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
            sheet.ColumnWidths[1] = 4.0;
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;

            var requestedDip = 20.0 * 96.0 / 72.0;
            var minimumDip = 6.0 * 96.0 / 72.0;
            overlay.FontSize.Should().BeLessThan(requestedDip);
            overlay.FontSize.Should().BeGreaterThanOrEqualTo(minimumDip - 0.01);
        });
    }

    [Fact]
    public void RenderWorksheet_NonShrinkToFitCellKeepsRequestedFontSizeEvenWhenTextOverflows()
    {
        // No-regression sibling: without ShrinkToFit the font size must never be reduced -- the
        // cell still just gets ellipsis-truncated at its full requested size, matching pre-fix
        // (and Excel's own) behaviour for non-shrink cells.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Non-shrink overflow print");
            var sheet = workbook.AddSheet("Sheet1");
            var style = workbook.RegisterStyle(new CellStyle { FontSize = 20 });
            var cell = Cell.FromValue(new TextValue("A very long value that overflows the column"));
            cell.StyleId = style;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
            sheet.ColumnWidths[1] = 4.0;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Neighbor"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlay = PdfTextOverlayExtractor.Extract(page)
                .Should().ContainSingle(o => o.Text.EndsWith("\u2026", StringComparison.Ordinal)).Subject;

            overlay.FontSize.Should().BeApproximately(20.0 * 96.0 / 72.0, 0.01);
            overlay.Text.Should().EndWith("\u2026");
        });
    }

    private static (Workbook Workbook, SheetId SheetId) BuildOverflowCandidateWorkbook(string text, bool wrapText)
    {
        var workbook = new Workbook($"Wrap print {wrapText}");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { WrapText = wrapText });
        var cell = Cell.FromValue(new TextValue(text));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        sheet.ColumnWidths[1] = 4.0;
        // A non-blank neighbor blocks the non-wrap cell from overflowing into it, so the
        // non-wrap sibling is forced to actually truncate (rather than widen) -- matching the
        // pre-fix single-line-ellipsis behaviour this test's sibling assertion pins.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Neighbor"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        return (workbook, sheet.Id);
    }
}
