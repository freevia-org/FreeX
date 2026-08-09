using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static void DrawHeaderFooter(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double marginBottom,
        double headerMargin,
        double footerMargin,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        WorksheetHeaderFooterPictureSet headerPictures,
        WorksheetHeaderFooterPictureSet footerPictures,
        string workbookName,
        string sheetName,
        bool alignWithMargins,
        int pageNumber,
        int totalPages,
        bool draftQuality,
        double fontScale,
        string workbookDirectory = "")
    {
        var headerHeight = CalculateHeaderFooterLineHeight(header, headerPictures, draftQuality, fontScale);
        var footerHeight = CalculateHeaderFooterLineHeight(footer, footerPictures, draftQuality, fontScale);
        var headerY = Math.Max(4, headerMargin - headerHeight);
        // R100-app-host-footer-margin-overlap-1: mirrors the header-side fix (R99, this file's
        // headerY above) and WorkbookPdfContentBuilder's footerY clamp
        // (footerY = Math.Min(footerEdgePt + 2, contentBottom)) for the PDF tier. The printed grid's
        // own bottom edge sits at pageH - Math.Max(marginBottom, footerMargin) -- the same
        // bodyBottomInches the pagination planner already used to size this page's row capacity
        // (PagePaginationPlanner.CalculatePageCapacityDetail) -- so once FooterMargin exceeds
        // BottomMargin, the unclamped "pageH - footerMargin - footerHeight" placed the footer text
        // band entirely inside that same grid span, printing the footer on top of the last row(s).
        // Clamping footerY to never start above the grid's own bottom edge keeps the footer band
        // below the grid, matching Excel and the already-fixed PDF export tier.
        var gridBottomEdge = pageH - PageGeometryRules.ResolveBodyEdge(marginBottom, footerMargin);
        var footerY = Math.Max(Math.Max(4, pageH - footerMargin - footerHeight), gridBottomEdge);
        var leftInset = alignWithMargins ? marginLeft : 0.3 * 96.0;
        var rightInset = alignWithMargins ? marginRight : 0.3 * 96.0;
        DrawHeaderFooterLine(dc, textOverlays, header, headerPictures, pageW, leftInset, rightInset, headerY, headerHeight, pageNumber, totalPages, workbookName, sheetName, draftQuality, fontScale, workbookDirectory);
        DrawHeaderFooterLine(dc, textOverlays, footer, footerPictures, pageW, leftInset, rightInset, footerY, footerHeight, pageNumber, totalPages, workbookName, sheetName, draftQuality, fontScale, workbookDirectory);
    }

    /// <summary>
    /// R131-app-host-headerfooter-center-asymmetric-margin-1: computes the three header/footer band
    /// rects (Left/Center/Right thirds of the PRINTABLE width, i.e. the page width minus the left and
    /// right insets). Excel centers the center section on this printable width BETWEEN the margins,
    /// not on the raw page width -- the two only coincide when the left and right insets are equal.
    /// The old formula centered the band on the full page width unconditionally
    /// (<c>(pageW - sectionWidth) / 2</c>), so with asymmetric left/right margins (or the 0.3in
    /// "don't align with margins" inset, which is itself always symmetric so this only bites the
    /// margin-aligned case) the center section drifted toward whichever side had the smaller inset --
    /// disagreeing with both Excel and this app's own PDF export path (<see
    /// cref="FreeX.App.Services.WorkbookPdfContentBuilder.RenderHeaderFooterBand"/>, which already
    /// placed the center section at <c>mL + sectionWidth</c>, the correct printable-area-relative
    /// position). Extracted to its own <c>internal</c> method (mirroring <see
    /// cref="CalculateHeaderFooterPictureRect"/>/<see cref="CalculateHeaderFooterTextRect"/> just below,
    /// already internal for the same reason) so the exact band geometry -- not text-measurement-shifted
    /// glyph positions -- is directly unit-testable and comparable against the PDF/Presentation paths.
    /// </summary>
    internal static (Rect Left, Rect Center, Rect Right) ResolveHeaderFooterSectionRects(
        double pageW,
        double leftInset,
        double rightInset,
        double y,
        double lineHeight)
    {
        var availableWidth = Math.Max(1, pageW - leftInset - rightInset);
        var sectionWidth = Math.Max(1, availableWidth / 3);

        var leftRect   = new Rect(leftInset, y, sectionWidth, lineHeight);
        var centerRect = new Rect(leftInset + sectionWidth, y, sectionWidth, lineHeight);
        var rightRect  = new Rect(pageW - rightInset - sectionWidth, y, sectionWidth, lineHeight);
        return (leftRect, centerRect, rightRect);
    }

    private static void DrawHeaderFooterLine(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        double pageW,
        double leftInset,
        double rightInset,
        double y,
        double lineHeight,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        bool draftQuality,
        double fontScale,
        string workbookDirectory = "")
    {
        // Tokenize each section into formatted runs. workbookDirectory is the folder containing the
        // workbook's saved file (trailing separator), or "" for an unsaved workbook -- substituted
        // for &Z / &[Path], matching Sheet's WPF page-setup preview and the portable PDF path.
        var leftRuns   = PagePrintTextPlanner.TokenizeSectionText(value.Left,   pageNumber, totalPages, workbookName, workbookDirectory, sheetName, DateTime.Now);
        var centerRuns = PagePrintTextPlanner.TokenizeSectionText(value.Center, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, DateTime.Now);
        var rightRuns  = PagePrintTextPlanner.TokenizeSectionText(value.Right,  pageNumber, totalPages, workbookName, workbookDirectory, sheetName, DateTime.Now);

        var (leftRect, centerRect, rightRect) = ResolveHeaderFooterSectionRects(pageW, leftInset, rightInset, y, lineHeight);

        var leftPicture   = !draftQuality && HasHeaderFooterPictureToken(value.Left)   ? pictures.Left   : null;
        var centerPicture = !draftQuality && HasHeaderFooterPictureToken(value.Center) ? pictures.Center : null;
        var rightPicture  = !draftQuality && HasHeaderFooterPictureToken(value.Right)  ? pictures.Right  : null;

        DrawHeaderFooterPicture(dc, leftPicture,   leftRect,   TextAlignment.Left);
        DrawHeaderFooterPicture(dc, centerPicture, centerRect, TextAlignment.Center);
        DrawHeaderFooterPicture(dc, rightPicture,  rightRect,  TextAlignment.Right);

        DrawHeaderFooterFormattedRuns(dc, textOverlays, leftRuns,   CalculateHeaderFooterTextRect(leftRect,   leftPicture,   TextAlignment.Left),   TextAlignment.Left,   fontScale);
        DrawHeaderFooterFormattedRuns(dc, textOverlays, centerRuns, CalculateHeaderFooterTextRect(centerRect, centerPicture, TextAlignment.Center), TextAlignment.Center, fontScale);
        DrawHeaderFooterFormattedRuns(dc, textOverlays, rightRuns,  CalculateHeaderFooterTextRect(rightRect,  rightPicture,  TextAlignment.Right),  TextAlignment.Right,  fontScale);
    }

    /// <summary>
    /// Draws a sequence of formatted runs within the given rect, splitting on any embedded line break
    /// first (R111-app-host-multiline-header-footer-1: a section may contain a literal Alt+Enter line
    /// break that <see cref="PagePrintTextPlanner.TokenizeSectionText"/> preserves verbatim inside a
    /// run's text) and drawing each resulting line on its own row within the rect -- rect's height
    /// already reflects every sibling section's line count too (<see
    /// cref="CalculateHeaderFooterLineHeight"/>), not just this one section's, so a section with fewer
    /// lines than its siblings simply leaves the remaining rows blank, keeping line N of every section
    /// aligned to the same row (matching Excel). <paramref name="fontScale"/> is Sheet.
    /// HeaderFooterScaleWithDocument's resolved multiplier (R111-app-host-headerfooter-scale-with-document-1)
    /// -- the per-line row step must scale by the same factor as the text drawn within it so lines stay
    /// non-overlapping at any scale.
    /// </summary>
    private static void DrawHeaderFooterFormattedRuns(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyList<HeaderFooterFormattedRun> runs,
        Rect rect,
        TextAlignment alignment,
        double fontScale)
    {
        if (runs.Count == 0) return;

        var scaledLineHeight = HeaderFooterSingleLineHeight * fontScale;
        var lines = PagePrintTextPlanner.SplitRunsIntoLines(runs);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var lineRuns = lines[lineIndex];
            if (lineRuns.Count == 0) continue;

            var lineRect = new Rect(
                rect.Left,
                rect.Top + (lineIndex * scaledLineHeight),
                rect.Width,
                scaledLineHeight);
            DrawHeaderFooterFormattedRunsLine(dc, textOverlays, lineRuns, lineRect, alignment, fontScale);
        }
    }

    /// <summary>
    /// Draws one already-split line's worth of runs within the given rect, advancing x as each run is
    /// drawn. Each run may carry its own font family, size, weight, style, and color from the Excel
    /// format codes. This is the single-line body previously inlined directly into
    /// <see cref="DrawHeaderFooterFormattedRuns"/> before the R111 multi-line split was added above.
    /// <paramref name="fontScale"/> multiplies every run's declared/default font size
    /// (R111-app-host-headerfooter-scale-with-document-1) -- 1.0 when Sheet.
    /// HeaderFooterScaleWithDocument is false or the page's own print scale is 100%, matching the
    /// caller-resolved multiplier from <see cref="DrawHeaderFooterFormattedRuns"/>.
    /// </summary>
    private static void DrawHeaderFooterFormattedRunsLine(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyList<HeaderFooterFormattedRun> runs,
        Rect rect,
        TextAlignment alignment,
        double fontScale)
    {
        if (runs.Count == 0) return;

        // Measure the total text width so we can compute the correct starting x for center/right.
        var totalWidth = MeasureTotalRunsWidth(runs, fontScale);
        var maxWidth = Math.Max(1, rect.Width - 4);

        // Clamp so we don't overflow the rect (match the single-run CharacterEllipsis behaviour
        // at a coarse level — we skip runs that are fully outside).
        var startX = alignment switch
        {
            TextAlignment.Center => rect.Left + 2 + Math.Max(0, (maxWidth - totalWidth) / 2),
            TextAlignment.Right  => rect.Left + 2 + Math.Max(0, maxWidth - totalWidth),
            _                    => rect.Left + 2
        };

        var x = startX;
        var rightBoundary = rect.Left + 2 + maxWidth;

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;
            if (x >= rightBoundary) break; // no room left

            var typeface = ResolveRunTypeface(run);
            var fontSize = (run.FontSize ?? PrintFontSize) * fontScale;
            var textColor = run.Color is { } c ? Color.FromRgb(c.R, c.G, c.B) : Colors.Black;
            var brush = new SolidColorBrush(textColor);
            var remainingWidth = Math.Max(1, rightBoundary - x);

            var ft = new FormattedText(
                run.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush,
                1.0)
            {
                MaxTextWidth = remainingWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Left // per-run is always left; we position x manually
            };

            if (run.Underline)
                ft.SetTextDecorations(TextDecorations.Underline);
            else if (run.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough);
            // Double-underline: no WPF built-in, render as Underline as a best-effort.
            else if (run.DoubleUnderline)
                ft.SetTextDecorations(TextDecorations.Underline);

            var textPoint = new Point(x, rect.Top + (rect.Height - ft.Height) / 2);
            dc.DrawText(ft, textPoint);

            // PDF overlay for this run
            AddHeaderFooterTextOverlay(textOverlays, run.Text, textPoint, remainingWidth, typeface, fontSize, TextAlignment.Left, textColor);

            x += ft.WidthIncludingTrailingWhitespace;
        }
    }

    private static double MeasureTotalRunsWidth(IReadOnlyList<HeaderFooterFormattedRun> runs, double fontScale)
    {
        var total = 0.0;
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;
            var typeface = ResolveRunTypeface(run);
            var fontSize = (run.FontSize ?? PrintFontSize) * fontScale;
            total += MeasurePrintedSingleLineText(run.Text, typeface, fontSize).WidthIncludingTrailingWhitespace;
        }
        return total;
    }

    private static Typeface ResolveRunTypeface(HeaderFooterFormattedRun run)
    {
        var family = new FontFamily(run.FontName ?? "Segoe UI");
        var weight = run.Bold ? FontWeights.Bold : FontWeights.Normal;
        var style  = run.Italic ? FontStyles.Italic : FontStyles.Normal;
        return new Typeface(family, style, weight, FontStretches.Normal);
    }

    private static void AddHeaderFooterTextOverlay(
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Point textPoint,
        double maxTextWidth,
        Typeface typeface,
        double fontSize,
        TextAlignment alignment,
        Color color)
    {
        var overlayText = BoundPrintedSingleLineOverlayText(text, maxTextWidth, typeface, fontSize);
        if (string.IsNullOrEmpty(overlayText))
            return;

        var overlayX = textPoint.X;
        var overlayWidth = MeasurePrintedSingleLineText(overlayText, typeface, fontSize).WidthIncludingTrailingWhitespace;
        if (alignment == TextAlignment.Center)
            overlayX += Math.Max(0, (maxTextWidth - overlayWidth) / 2);
        else if (alignment == TextAlignment.Right)
            overlayX += Math.Max(0, maxTextWidth - overlayWidth);

        textOverlays.Add(new PdfTextOverlay(
            overlayText,
            overlayX,
            textPoint.Y,
            fontSize,
            typeface.FontFamily.Source,
            typeface.Weight >= FontWeights.SemiBold,
            typeface.Style == FontStyles.Italic || typeface.Style == FontStyles.Oblique,
            color));
    }

}
