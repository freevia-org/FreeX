using System.IO;
using System.Linq;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Covers the read-only PDF text import adapter (design §5.8): its capability shape (open-only; Save throws)
/// and a synthesize-then-extract round-trip. The PDF is built in-test with PdfPig's
/// <see cref="PdfDocumentBuilder"/> so the test owns its input and never depends on a checked-in binary.
/// </summary>
public sealed class PdfFileAdapterTests
{
    [Fact]
    public void Capabilities_AreOpenOnly()
    {
        var adapter = new PdfFileAdapter();

        adapter.Extension.Should().Be(".pdf");
        adapter.FormatName.Should().Be("PDF Document");

        var format = adapter.Formats.Should().ContainSingle().Subject;
        format.Extension.Should().Be(".pdf");
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeFalse();
        format.OpensAsTemplate.Should().BeFalse();
    }

    [Fact]
    public void Save_Throws_NotSupported()
    {
        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream();

        var act = () => adapter.Save(new TextDocument(), stream);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Save As .docx*");
    }

    [Fact]
    public void Load_ExtractsText_FromSynthesizedPdf()
    {
        var pdfBytes = BuildPdf("Hello PDF", "Second line");

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        var document = adapter.Load(stream);

        // Each recovered text block is a paragraph with a single default run and no style.
        document.Blocks.Should().NotBeEmpty();
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            paragraph.StyleId.Should().BeNull();
            paragraph.Runs.Count.Should().BeLessThanOrEqualTo(1);
        }

        var allText = string.Join("\n", document.Blocks.OfType<Paragraph>().Select(p => p.PlainText));
        allText.Should().Contain("Hello PDF");
        allText.Should().Contain("Second line");
    }

    [Fact]
    public void Load_DoesNotDisposeCallerStream()
    {
        var pdfBytes = BuildPdf("Stream stays open");

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        adapter.Load(stream);

        // Adapter contract: the caller owns the stream. Accessing Length after Load proves it is not disposed.
        var act = () => _ = stream.Length;
        act.Should().NotThrow();
    }

    // NOTE (documented poor-input case, no assertion): multi-column PDFs extract poorly. PdfPig's
    // reading-order detection streams glyphs left-to-right/top-to-bottom and cannot reliably tell that two
    // side-by-side columns are separate flows, so a two-column page typically interleaves the columns
    // line-by-line ("col1-line1 col2-line1 col1-line2 ..."). This is an inherent limitation of text-only PDF
    // import (no layout/column model, no OCR) and is called out in §5.8 rather than worked around here.

    /// <summary>
    /// Builds a one-page PDF whose <paramref name="lines"/> are drawn top-to-bottom with a standard font, and
    /// returns its bytes. Each line is placed on a separate baseline so the extractor recovers line breaks.
    /// </summary>
    private static byte[] BuildPdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var y = 700;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(50, y), font);
            y -= 30;
        }

        return builder.Build();
    }
}
