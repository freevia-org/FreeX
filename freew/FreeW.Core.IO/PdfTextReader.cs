using System.IO;
using System.Linq;
using System.Text;
using FreeW.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FreeW.Core.IO;

/// <summary>
/// Best-effort, read-only PDF text extraction (design §5.8). PDF has no document model — it is a bag of
/// positioned glyphs — so this reader recovers only <em>text</em>, in best-effort reading order, page by
/// page. Each recovered text block becomes a <see cref="Paragraph"/> with a single default <see cref="Run"/>
/// and no <see cref="Paragraph.StyleId"/>. It deliberately does <strong>not</strong> attempt tables, images,
/// columns, lists, footnotes, comments, fonts/styles, or layout — those are dropped. Fidelity is LOW and
/// inherently lossy: multi-column, RTL, table-heavy, or scanned (image-only, no text layer) PDFs degrade
/// badly, and there is no OCR. Because of this, PDF import is read-only — see <see cref="PdfFileAdapter"/>,
/// which refuses to save back to PDF.
/// </summary>
public static class PdfTextReader
{
    /// <summary>
    /// Extracts text from the PDF in <paramref name="stream"/> into a sparse <see cref="TextDocument"/>.
    /// The stream is fully read into memory (PdfPig needs random access) but is <em>not</em> disposed by this
    /// reader, matching the adapter stream-ownership contract. Each page's recovered text is split on line
    /// breaks into paragraphs; blank lines collapse to empty paragraphs so vertical spacing is roughly kept.
    /// An empty or text-less PDF yields a document with a single empty paragraph (never zero blocks), so the
    /// editor always has a caret position.
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
                var pageText = ExtractPageText(page);
                if (string.IsNullOrEmpty(pageText))
                    continue;

                var normalized = pageText.Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (var line in normalized.Split('\n'))
                    document.Blocks.Add(new Paragraph(line.TrimEnd()));
            }
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        return document;
    }

    private static string ExtractPageText(Page page) =>
        TryExtractColumnAwareText(page) ?? ContentOrderTextExtractor.GetText(page);

    private static string? TryExtractColumnAwareText(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new PositionedWord(
                word.Text,
                word.BoundingBox.Left,
                word.BoundingBox.Right,
                word.BoundingBox.Bottom,
                word.BoundingBox.Top))
            .OrderBy(word => word.Left)
            .ToList();
        if (words.Count < 4)
            return null;

        var pageWidth = Math.Max(1, page.Width);
        var gaps = words
            .Zip(words.Skip(1), (left, right) => new WordGap(left.Right, right.Left))
            .Where(gap => gap.Width > pageWidth * 0.10)
            .OrderByDescending(gap => gap.Width)
            .ToList();
        if (gaps.Count == 0)
            return null;

        var splitX = (gaps[0].LeftEdge + gaps[0].RightEdge) / 2;
        var leftColumn = words.Where(word => word.CenterX < splitX).ToList();
        var rightColumn = words.Where(word => word.CenterX >= splitX).ToList();
        if (leftColumn.Count < 2 || rightColumn.Count < 2)
            return null;

        var leftRight = leftColumn.Max(word => word.Right);
        var rightLeft = rightColumn.Min(word => word.Left);
        if (rightLeft - leftRight < pageWidth * 0.08)
            return null;

        return string.Join("\n", ColumnLines(leftColumn).Concat(ColumnLines(rightColumn)));
    }

    private static IEnumerable<string> ColumnLines(IReadOnlyList<PositionedWord> words)
    {
        var ordered = words
            .OrderByDescending(word => word.CenterY)
            .ThenBy(word => word.Left)
            .ToList();
        var medianHeight = ordered
            .Select(word => Math.Max(1, word.Height))
            .OrderBy(height => height)
            .ElementAt(ordered.Count / 2);
        var lineTolerance = Math.Max(2, medianHeight * 0.75);
        var lines = new List<List<PositionedWord>>();

        foreach (var word in ordered)
        {
            var line = lines.FirstOrDefault(existing =>
                Math.Abs(existing.Average(existingWord => existingWord.CenterY) - word.CenterY) <= lineTolerance);
            if (line is null)
                lines.Add([word]);
            else
                line.Add(word);
        }

        foreach (var line in lines.OrderByDescending(line => line.Average(word => word.CenterY)))
        {
            var builder = new StringBuilder();
            foreach (var word in line.OrderBy(word => word.Left))
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(word.Text);
            }
            yield return builder.ToString();
        }
    }

    private readonly record struct PositionedWord(string Text, double Left, double Right, double Bottom, double Top)
    {
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Bottom + Top) / 2;
        public double Height => Top - Bottom;
    }

    private readonly record struct WordGap(double LeftEdge, double RightEdge)
    {
        public double Width => RightEdge - LeftEdge;
    }
}
