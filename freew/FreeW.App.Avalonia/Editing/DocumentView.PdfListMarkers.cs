using Free.Shared.Pdf;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private void AppendPdfListMarkers(List<List<PdfDrawOp>> pages, double pageHeightPt)
    {
        // This is the same stream Render paints: native/preserved body markers and table
        // markers are visual chrome, so none belongs to the authored/placed character stream.
        foreach (var (x, y, text, formatting) in _markers)
        {
            if (string.IsNullOrEmpty(text)) continue;
            var page = PageIndexFromPageSpaceY(y);
            if (page < 0) continue;
            while (pages.Count <= page) pages.Add([]);

            var face = (formatting.Bold, formatting.Italic) switch
            {
                (true, true) => PdfFontFace.BoldItalic,
                (true, false) => PdfFontFace.Bold,
                (false, true) => PdfFontFace.Italic,
                _ => PdfFontFace.Regular,
            };
            // Render draws FormattedText at its top-left. Carry its actual baseline into
            // PDF's bottom-left coordinate system, without the editor's desk/page gaps.
            var baselineDip = Build(text, formatting).Baseline;
            pages[page].Add(new PdfText(
                Math.Max(0, (x - _contentLeft) / PxPerPoint + _doc.Page.MarginLeftPt),
                pageHeightPt - (y - _surfacePlan.PageTopDip(page) + baselineDip) / PxPerPoint,
                formatting.FontSizePt ?? DefaultFontSizePt,
                face, ParseColor(formatting.ColorHex), text, formatting.FontFamily));
        }
    }
}
