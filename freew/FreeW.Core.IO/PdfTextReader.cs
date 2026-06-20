using System.IO;
using System.Linq;
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
                // ContentOrderTextExtractor orders glyphs into a best-effort reading order and inserts line
                // breaks; far better than the raw Page.Text (which is glyph-storage order) for plain prose.
                var pageText = ContentOrderTextExtractor.GetText(page);
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
}
