using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves slideshow clicks against run-level hyperlinks without making the host
/// renderer responsible for document-model hit testing.
/// </summary>
public static class SlideShowTextHyperlinkHitTestPlanner
{
    private const double EmuPerDip = 9525.0;
    private const double DipPerPoint = 96.0 / 72.0;
    private const double DefaultInsetHorzDip = 9.14;
    private const double DefaultInsetVertDip = 4.57;

    public static Hyperlink? HitTest(SlideShape shape, SlideShowPoint slidePoint)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(slidePoint);

        var body = shape.TextBody;
        if (body is null || body.Paragraphs.Count == 0)
            return null;

        double shapeX = shape.OffsetXEmu / EmuPerDip;
        double shapeY = shape.OffsetYEmu / EmuPerDip;
        double shapeWidth = shape.ExtentCxEmu / EmuPerDip;
        double shapeHeight = shape.ExtentCyEmu / EmuPerDip;
        double insetLeft = ResolveInset(body.InsetLeftPt, DefaultInsetHorzDip);
        double insetRight = ResolveInset(body.InsetRightPt, DefaultInsetHorzDip);
        double insetTop = ResolveInset(body.InsetTopPt, DefaultInsetVertDip);
        double insetBottom = ResolveInset(body.InsetBottomPt, DefaultInsetVertDip);
        double left = shapeX + insetLeft;
        double top = shapeY + insetTop;
        double width = Math.Max(0, shapeWidth - insetLeft - insetRight);
        double height = Math.Max(0, shapeHeight - insetTop - insetBottom);
        if (width <= 0 || height <= 0)
            return null;

        double fontScale = body.FontScalePPT is > 0
            ? Math.Clamp(body.FontScalePPT.Value / 100000.0, 0.05, 4.0)
            : 1.0;
        var paragraphs = body.Paragraphs
            .Select(paragraph => BuildParagraphLines(paragraph, width, fontScale))
            .ToArray();
        double totalHeight = paragraphs.Sum(p => p.TotalHeightDip);
        double currentY = top + (body.Anchor switch
        {
            VerticalAnchor.Middle => Math.Max(0, (height - totalHeight) * 0.5),
            VerticalAnchor.Bottom => Math.Max(0, height - totalHeight),
            _ => 0
        });

        foreach (var paragraph in paragraphs)
        {
            currentY += paragraph.SpaceBeforeDip;
            foreach (var line in paragraph.Lines)
            {
                double lineX = paragraph.Align switch
                {
                    TextAlign.Center => left + Math.Max(0, (width - line.WidthDip) * 0.5),
                    TextAlign.Right => left + Math.Max(0, width - line.WidthDip),
                    _ => left
                };

                foreach (var span in line.Spans)
                {
                    if (span.Hyperlink is not null
                        && slidePoint.X >= lineX + span.XDip
                        && slidePoint.X <= lineX + span.XDip + span.WidthDip
                        && slidePoint.Y >= currentY
                        && slidePoint.Y <= currentY + line.HeightDip)
                    {
                        return span.Hyperlink;
                    }
                }

                currentY += line.HeightDip;
            }

            currentY += paragraph.SpaceAfterDip;
        }

        return null;
    }

    private static ParagraphLines BuildParagraphLines(Paragraph paragraph, double maxWidth, double fontScale)
    {
        var lines = new List<Line>();
        var line = new Line();

        foreach (var run in paragraph.Runs)
        {
            double fontSizeDip = Math.Max(1, (run.FontSizePt ?? 18.0) * DipPerPoint * fontScale);
            double lineHeightDip = Math.Max(12, fontSizeDip * ParagraphSpacingMetrics.LineHeightFactor);
            foreach (var character in run.Text ?? string.Empty)
            {
                if (character is '\r' or '\n')
                {
                    FinishLine(lines, line);
                    line = new Line();
                    continue;
                }

                double characterWidth = EstimateCharacterWidth(character, fontSizeDip, run);
                if (line.WidthDip > 0 && line.WidthDip + characterWidth > maxWidth)
                {
                    FinishLine(lines, line);
                    line = new Line();
                }

                if (run.Hyperlink is not null && !char.IsWhiteSpace(character))
                {
                    var last = line.Spans.LastOrDefault();
                    if (last is not null && ReferenceEquals(last.Hyperlink, run.Hyperlink))
                    {
                        last.WidthDip += characterWidth;
                    }
                    else
                    {
                        line.Spans.Add(new RunSpan(run.Hyperlink, line.WidthDip, characterWidth));
                    }
                }

                line.WidthDip += characterWidth;
                line.HeightDip = Math.Max(line.HeightDip, lineHeightDip);
            }
        }

        if (line.WidthDip > 0 || lines.Count == 0)
            FinishLine(lines, line);

        double spacingBasisFontSizePt = ParagraphSpacingMetrics.MaxRunFontSizePoints(paragraph) * fontScale;
        return new ParagraphLines(
            lines,
            // Percent spacing resolves against a single line's height at the autofit-scaled font
            // size, matching how the renderer resolves it from already-scaled run sizes.
            PointsToDip(ParagraphSpacingMetrics.ResolveSpaceBeforePoints(paragraph, spacingBasisFontSizePt)),
            PointsToDip(ParagraphSpacingMetrics.ResolveSpaceAfterPoints(paragraph, spacingBasisFontSizePt)),
            paragraph.Align ?? TextAlign.Left);
    }

    private static void FinishLine(List<Line> lines, Line line)
    {
        if (line.WidthDip <= 0)
            return;

        line.HeightDip = Math.Max(12, line.HeightDip);
        lines.Add(line);
    }

    private static double EstimateCharacterWidth(char character, double fontSizeDip, Run run)
    {
        double factor = character switch
        {
            ' ' or '\t' => 0.35,
            'i' or 'l' or 'I' or '!' or '.' or ',' or ':' or ';' => 0.28,
            'W' or 'M' => 0.90,
            _ => 0.55
        };
        if (run.Bold)
            factor *= 1.05;
        if (run.Italic)
            factor *= 1.02;
        return Math.Max(1, fontSizeDip * factor);
    }

    private static double ResolveInset(double? points, double fallbackDip) =>
        points.HasValue ? Math.Max(0, PointsToDip(points.Value)) : fallbackDip;

    private static double PointsToDip(double points) => points * DipPerPoint;

    private sealed class Line
    {
        public List<RunSpan> Spans { get; } = new();
        public double WidthDip { get; set; }
        public double HeightDip { get; set; }
    }

    private sealed class RunSpan
    {
        public RunSpan(Hyperlink hyperlink, double xDip, double widthDip)
        {
            Hyperlink = hyperlink;
            XDip = xDip;
            WidthDip = widthDip;
        }

        public Hyperlink Hyperlink { get; }
        public double XDip { get; }
        public double WidthDip { get; set; }
    }

    private sealed record ParagraphLines(
        IReadOnlyList<Line> Lines,
        double SpaceBeforeDip,
        double SpaceAfterDip,
        TextAlign Align)
    {
        public double TotalHeightDip => SpaceBeforeDip + Lines.Sum(line => line.HeightDip) + SpaceAfterDip;
    }
}
