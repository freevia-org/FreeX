using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Free.Shared.Pdf;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private Rect HeaderFooterPdfBounds(Rect bounds, int pageIndex, double pageHeightPt) => new(
        (bounds.X - _surfacePlan.RenderedPageLeftDip(pageIndex)) / PxPerPoint,
        pageHeightPt - (bounds.Bottom - _surfacePlan.PageTopDip(pageIndex)) / PxPerPoint,
        bounds.Width / PxPerPoint, bounds.Height / PxPerPoint);

    private IReadOnlyList<PdfDrawOp> BuildPdfHeaderFooterTableSurface(HfTableSurface surface, double pageHeightPt)
    {
        var operations = new List<PdfDrawOp>();
        var bounds = HeaderFooterPdfBounds(surface.Bounds, surface.PageIndex, pageHeightPt);
        if (surface.Fill.EffectiveFillHex is { Length: > 0 } fill)
            operations.Add(new PdfFillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, ParseColor(fill)));
        foreach (var edge in surface.Borders.Edges.Where(edge => edge.IsVisible))
        {
            var contours = TableCellBorderVisualPlanner.BuildStrokeSegments(edge,
                surface.Bounds.Left, surface.Bounds.Top, surface.Bounds.Right, surface.Bounds.Bottom,
                waveRegistrationDip: -4.0).Select(segment =>
            {
                PdfPathPoint Point(double x, double y) => new(
                    (x - _surfacePlan.RenderedPageLeftDip(surface.PageIndex)) / PxPerPoint,
                    pageHeightPt - (y - _surfacePlan.PageTopDip(surface.PageIndex)) / PxPerPoint);
                return new PdfPathContour(Point(segment.X1Dip, segment.Y1Dip),
                    [PdfPathSegment.LineTo(Point(segment.X2Dip, segment.Y2Dip))], false);
            }).ToArray();
            var width = edge.WidthDip / PxPerPoint;
            var dash = edge.Style switch
            {
                BorderLineStyle.Dashed => new PdfDashPattern([4 * width, 3 * width]),
                BorderLineStyle.Dotted => new PdfDashPattern([width, 2 * width]),
                _ => null,
            };
            PdfDrawOp stroke = new PdfPath(contours, null, ParseColor(edge.ColorHex), width, dash);
            operations.Add(edge.StrokeOpacity < 1 ? new PdfOpacityGroup(edge.StrokeOpacity, [stroke]) : stroke);
        }
        return operations;
    }

    private IReadOnlyList<PdfDrawOp> ClipHeaderFooterPdfOps(HfRenderItem item, int pageIndex,
        double pageHeightPt, IReadOnlyList<PdfDrawOp> operations)
    {
        if (!_headerFooterTableClips.TryGetValue(item, out var clip))
            return operations;
        var bounds = HeaderFooterPdfBounds(clip, pageIndex, pageHeightPt);
        return [new PdfClipGroup(bounds.X, bounds.Y, bounds.Width, bounds.Height, operations)];
    }

    // These operations share the live H/F layout, including line baselines, alignment and source
    // run intersections. Notes keep their established export adapter.
    private IReadOnlyList<PdfDrawOp> BuildPdfHeaderFooterText(HfRenderItem item, int pageIndex, double pageHeightPt)
    {
        var operations = new List<PdfDrawOp>();
        if (string.IsNullOrEmpty(item.Text))
            return operations;
        if (_headerFooterTableParagraphs.TryGetValue(item, out var tableLayout))
        {
            AppendLayout(tableLayout.Layout, new Point(item.X, item.Y + RunBaselinePositionPlanner.ResolveOffsetDip(item.Fmt, PxPerPoint)), tableLayout.LineTops,
                tableLayout.Paragraph, tableLayout.TableBold);
        }
        else if (SharedHeaderFooterLayout(item) is { } shared)
        {
            using (shared)
                AppendLayout(shared, HeaderFooterLayoutOrigin(item, shared), null,
                    item.Target is { } target ? GetHfParagraph(target) : null, false);
        }
        else if (HeaderFooterContainsSoftBreak(item.Text))
        {
            using var layout = BuildHeaderFooterLiteralLayout(item.Text, item.Fmt);
            AppendLayout(layout, HeaderFooterLayoutOrigin(item, layout), null, null, false);
        }
        else if (!HeaderFooterHasOnlyNonprintingControls(item.Text))
        {
            var text = Build(item.Text, item.Fmt);
            var x = item.X + AlignmentOffset(item.Alignment, item.AvailableWidth,
                text.WidthIncludingTrailingWhitespace, isLast: true);
            AddText(item.Text, item.Fmt, x,
                item.Y + text.Baseline + RunBaselinePositionPlanner.ResolveOffsetDip(item.Fmt, PxPerPoint));
        }

        return ClipHeaderFooterPdfOps(item, pageIndex, pageHeightPt, operations);

        void AddText(string text, RunFormatting formatting, double x, double baseline)
        {
            if (text.Length == 0)
                return;
            var face = (formatting.Bold, formatting.Italic) switch
            {
                (true, true) => PdfFontFace.BoldItalic,
                (true, false) => PdfFontFace.Bold,
                (false, true) => PdfFontFace.Italic,
                _ => PdfFontFace.Regular,
            };
            operations.Add(new PdfText(
                (x - _surfacePlan.RenderedPageLeftDip(pageIndex)) / PxPerPoint,
                pageHeightPt - (baseline - _surfacePlan.PageTopDip(pageIndex)) / PxPerPoint,
                formatting.FontSizePt ?? DefaultFontSizePt, face, ParseColor(formatting.ColorHex), text,
                formatting.FontFamily));
        }

        void AppendLayout(TextLayout layout, Point origin, IReadOnlyList<double>? lineTops,
            Paragraph? paragraph, bool tableBold)
        {
            var runs = new List<(int Start, int End, RunFormatting Formatting)>();
            var offset = 0;
            if (paragraph is not null)
                foreach (var run in paragraph.Runs)
                {
                    runs.Add((offset, offset + run.Text.Length,
                        ResolveHeaderFooterTableRunFmt(run, paragraph, tableBold)));
                    offset += run.Text.Length;
                }
            else
                runs.Add((0, item.Text.Length, item.Fmt));
            var naturalTop = 0d;
            for (var index = 0; index < layout.TextLines.Count; index++)
            {
                var line = layout.TextLines[index];
                var baseline = origin.Y + (lineTops is null ? naturalTop : lineTops[index]) + line.Baseline;
                var lineEnd = Math.Min(item.Text.Length, line.FirstTextSourceIndex + line.Length);
                foreach (var run in runs)
                {
                    var start = Math.Max(run.Start, line.FirstTextSourceIndex);
                    var end = Math.Min(run.End, lineEnd);
                    while (end > start && IsBodySoftLineBreak(item.Text[end - 1])) end--;
                    if (end <= start) continue;
                    var x = origin.X + line.GetDistanceFromCharacterHit(new CharacterHit(start));
                    AddText(item.Text[start..end], run.Formatting, x, baseline);
                }
                naturalTop += line.Height;
            }
        }
    }
}
