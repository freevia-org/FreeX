using System.IO;
using System.Linq;
using System.Text;
using Free.Shared.Pdf.Import;
using FreeW.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FreeW.Core.IO;

/// <summary>
/// Best-effort, read-only PDF text extraction (design §5.8). PDF has no document model — it is a bag of
/// positioned glyphs — so this reader recovers only <em>text</em>, in best-effort reading order, page by
/// page.
///
/// Fidelity improvements over the naive line-per-paragraph approach:
/// <list type="bullet">
///   <item>Letters are grouped into <em>lines</em> by comparing each letter's baseline Y value within a
///   tolerance of half the dominant font size. Lines are sorted top-to-bottom.</item>
///   <item>Consecutive lines are merged into <em>paragraphs</em> when the vertical gap between them is
///   ≤ 1.3× the median line height; a larger gap starts a new paragraph, producing structural blocks
///   that roughly mirror the PDF's visual paragraph breaks.</item>
///   <item>Each paragraph gets <em>run formatting</em>: the dominant font name and point size are sampled
///   for every line, and when the font name contains a bold weight marker ("Bold", "-Bd", "-B", or common
///   weight variants) the run's <see cref="RunFormatting.Bold"/> flag is set. The modal font size among
///   the letters in the paragraph becomes <see cref="RunFormatting.FontSizePt"/>.</item>
/// </list>
/// Known-poor cases (unchanged from before, inherent to text-only PDF): multi-column layouts, RTL text,
/// table-heavy or scanned (image-only, no text layer) PDFs. No OCR. Fidelity is LOW by design.
/// Because of this, PDF import is read-only — see <see cref="PdfFileAdapter"/>, which refuses to save.
/// </summary>
public static class PdfTextReader
{
    /// <summary>
    /// Extracts text from the PDF in <paramref name="stream"/> into a sparse <see cref="TextDocument"/>.
    /// The stream is fully read into memory (PdfPig needs random access) but is <em>not</em> disposed by
    /// this reader, matching the adapter stream-ownership contract.
    ///
    /// The returned document groups glyphs into visual lines then lines into paragraphs. Each paragraph
    /// becomes one <see cref="Paragraph"/> with a single <see cref="Run"/> whose formatting reflects the
    /// dominant font/size. An empty or text-less PDF yields a document with a single empty paragraph
    /// (never zero blocks), so the editor always has a caret position.
    /// </summary>
    public static TextDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // PdfPig needs a seekable, fully-materialised buffer; copy without taking ownership of the caller's stream.
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var document = new TextDocument();
        document.Blocks.Clear();

        using (var pdf = PdfDocument.Open(bytes))
        {
            foreach (var page in pdf.GetPages())
            {
                var pageBlocks = ExtractPageBlocks(page);
                foreach (var block in pageBlocks)
                    document.Blocks.Add(block);
            }
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        return document;
    }

    // ── geometry helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the font name contains a bold weight marker in common PDF name conventions.
    /// Covers: "Bold", "-Bd", "-B" suffix (e.g. "Helvetica-Bd"), ",Bold", weight tokens ("-700",
    /// "-900", "Black", "Heavy", "ExtraBold", "UltraBold"). Case-insensitive.
    /// </summary>
    private static bool FontNameIndicatesBold(string fontName)
    {
        if (string.IsNullOrEmpty(fontName))
            return false;
        var n = fontName;
        // Common verbatim substrings (case-insensitive comparison is fine; most PDF names are ASCII).
        if (n.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.Contains("Black", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.Contains("Heavy", StringComparison.OrdinalIgnoreCase))
            return true;
        // Abbreviated suffix forms: "-Bd", "-B" (e.g. "Helvetica-Bd"), ",B", ":B"
        if (n.EndsWith("-Bd", StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith(",Bd", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.EndsWith("-B", StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith(",B", StringComparison.OrdinalIgnoreCase))
            return true;
        // Weight tokens embedded in name: -700, -800, -900 (OpenType/PostScript numerics)
        if (n.Contains("-700", StringComparison.Ordinal) ||
            n.Contains("-800", StringComparison.Ordinal) ||
            n.Contains("-900", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static IReadOnlyList<IReadOnlyList<Letter>> SplitLettersForReading(Page page)
    {
        var letters = page.Letters;
        if (letters == null || letters.Count < 4)
            return [letters ?? []];

        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new PositionedWord(
                word.BoundingBox.Left,
                word.BoundingBox.Right))
            .OrderBy(word => word.Left)
            .ToList();
        if (words.Count < 4)
            return [letters];

        var pageWidth = Math.Max(1, page.Width);
        var gaps = words
            .Zip(words.Skip(1), (left, right) => new WordGap(left.Right, right.Left))
            .Where(gap => gap.Width > pageWidth * 0.10)
            .OrderByDescending(gap => gap.Width)
            .ToList();
        if (gaps.Count == 0)
            return [letters];

        var splitX = (gaps[0].LeftEdge + gaps[0].RightEdge) / 2;
        var leftWords = words.Where(word => word.CenterX < splitX).ToList();
        var rightWords = words.Where(word => word.CenterX >= splitX).ToList();
        if (leftWords.Count < 2 || rightWords.Count < 2)
            return [letters];

        var leftRight = leftWords.Max(word => word.Right);
        var rightLeft = rightWords.Min(word => word.Left);
        if (rightLeft - leftRight < pageWidth * 0.08)
            return [letters];

        var leftLetters = letters.Where(letter => letter.GlyphRectangle.BottomLeft.X < splitX).ToList();
        var rightLetters = letters.Where(letter => letter.GlyphRectangle.BottomLeft.X >= splitX).ToList();
        return leftLetters.Count == 0 || rightLetters.Count == 0
            ? [letters]
            : [leftLetters, rightLetters];
    }

    /// <summary>
    /// Groups all letters on <paramref name="page"/> into visual text lines, then groups consecutive
    /// lines into paragraph blocks, and returns the resulting <see cref="Paragraph"/> objects.
    /// </summary>
    private static List<Paragraph> ExtractPageBlocks(Page page)
    {
        var result = new List<Paragraph>();
        foreach (var letters in SplitLettersForReading(page))
            result.AddRange(ExtractPageBlocks(letters));
        return result;
    }

    private static List<Paragraph> ExtractPageBlocks(IReadOnlyList<Letter> letters)
    {
        // 1. Collect all letters with geometry.
        if (letters == null || letters.Count == 0)
            return [];

        // 2. Group letters into lines by baseline Y (PdfPig Y grows upward, so higher Y = higher on page).
        var clustering = PdfTextLineClusterer.Cluster(letters, GetGlyphMetrics);
        var dominantSize = clustering.ModalFontSize ?? PdfTextLineClusterer.DefaultFontSize;
        var lines = clustering.Lines.Select(ProjectTextLine).ToList();

        if (lines.Count == 0)
            return [];

        // 3. Compute median line height (= dominant font size as a proxy; use actual bbox heights
        //    if available, but PointSize is more reliable for this purpose).
        var lineHeights = lines.Select(l => l.DominantSize).Where(s => s > 0).ToList();
        var medianLineHeight = lineHeights.Count > 0 ? Median(lineHeights) : dominantSize;

        // 4. Group lines into paragraphs: a new paragraph starts when the gap between the bottom of
        //    the previous line and the top of the current line exceeds 1.3× the median line height.
        var paragraphGapThreshold = medianLineHeight * 1.3;

        var paragraphs = new List<List<TextLine>>();
        var currentParagraph = new List<TextLine> { lines[0] };

        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var curr = lines[i];
            // Gap between the baselines of consecutive lines (both in PDF coords = up is positive).
            // We subtract curr.BaselineY from prev.BaselineY since lines are sorted top→bottom.
            var gap = prev.BaselineY - curr.BaselineY;
            if (gap > paragraphGapThreshold)
            {
                paragraphs.Add(currentParagraph);
                currentParagraph = [curr];
            }
            else
            {
                currentParagraph.Add(curr);
            }
        }
        paragraphs.Add(currentParagraph);

        // 5. Convert each paragraph group into a Paragraph model object.
        var result = new List<Paragraph>(paragraphs.Count);
        foreach (var paraLines in paragraphs)
        {
            var para = BuildParagraph(paraLines);
            result.Add(para);
        }
        return result;
    }

    /// <summary>
    /// Converts a group of text lines (already sorted top-to-bottom within a paragraph) into a
    /// <see cref="Paragraph"/> with a single <see cref="Run"/> that has best-effort bold and font-size
    /// formatting derived from the dominant letters in the paragraph.
    /// </summary>
    private static Paragraph BuildParagraph(List<TextLine> paraLines)
    {
        // Join lines with a space (visual reading order within a paragraph).
        var text = string.Join(" ", paraLines.Select(l => l.Text.TrimEnd()));

        // Determine dominant formatting across all letters in the paragraph.
        var allLetters = paraLines.SelectMany(l => l.Letters).ToList();

        var bold = IsDominantlyBold(allLetters);
        var fontSize = PdfTextLineClusterer.CalculateModalFontSize(allLetters, GetGlyphMetrics);

        RunFormatting formatting;
        if (bold || fontSize.HasValue)
        {
            formatting = new RunFormatting
            {
                Bold = bold,
                FontSizePt = fontSize,
            };
        }
        else
        {
            formatting = RunFormatting.Default;
        }

        var run = new Run(text, formatting);
        var para = new Paragraph();
        para.Runs.Add(run);
        return para;
    }

    /// <summary>
    /// Returns true when the majority (>50%) of letters in <paramref name="letters"/> come from a bold font.
    /// </summary>
    private static bool IsDominantlyBold(List<Letter> letters)
    {
        if (letters.Count == 0)
            return false;
        var boldCount = letters.Count(l =>
            l.FontName != null && FontNameIndicatesBold(l.FontName));
        return boldCount > letters.Count / 2.0;
    }

    private static TextLine ProjectTextLine(PdfTextLine<Letter> line)
    {
        var projected = new TextLine(line.BaselineY, line.ModalFontSize ?? 0, line.Glyphs);
        projected.FinalizeText();
        return projected;
    }

    private static PdfTextGlyphMetrics GetGlyphMetrics(Letter letter) =>
        new(
            letter.Value,
            letter.GlyphRectangle.BottomLeft.Y,
            letter.GlyphRectangle.BottomLeft.X,
            letter.PointSize);

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    // ── internal model ───────────────────────────────────────────────────────

    /// <summary>
    /// Working representation of a single visual text line during extraction.
    /// </summary>
    private sealed class TextLine(
        double baselineY,
        double dominantSize,
        IReadOnlyList<Letter> letters)
    {
        public double BaselineY { get; } = baselineY;
        public double DominantSize { get; } = dominantSize;
        public List<Letter> Letters { get; } = [.. letters];
        public string Text { get; private set; } = string.Empty;

        /// <summary>
        /// Builds <see cref="Text"/> from the sorted <see cref="Letters"/> list, inserting a space
        /// between letters whose horizontal gap exceeds ~0.25× the font size (a simple word-gap heuristic).
        /// </summary>
        public void FinalizeText()
        {
            if (Letters.Count == 0)
            {
                Text = string.Empty;
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(Letters[0].Value);
            var refSize = Letters[0].PointSize > 0 ? Letters[0].PointSize : 12.0;
            var wordGap = refSize * 0.25;

            for (var i = 1; i < Letters.Count; i++)
            {
                var prev = Letters[i - 1];
                var curr = Letters[i];
                // Gap between the right edge of prev and the left edge of curr.
                var gap = curr.GlyphRectangle.BottomLeft.X - prev.GlyphRectangle.TopRight.X;
                if (gap > wordGap)
                    sb.Append(' ');
                sb.Append(curr.Value);
            }

            Text = sb.ToString();
        }
    }

    private readonly record struct PositionedWord(double Left, double Right)
    {
        public double CenterX => (Left + Right) / 2;
    }

    private readonly record struct WordGap(double LeftEdge, double RightEdge)
    {
        public double Width => RightEdge - LeftEdge;
    }
}
