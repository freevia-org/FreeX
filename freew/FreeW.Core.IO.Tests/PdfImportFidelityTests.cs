using System.IO;
using System.Linq;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Exercises the improved structural fidelity of <see cref="PdfTextReader"/>:
/// <list type="bullet">
///   <item>Two body paragraphs separated by a blank vertical gap are recovered as ≥ 2 distinct
///   <see cref="Paragraph"/> objects (not one flat line-per-paragraph block).</item>
///   <item>A bold heading line (Helvetica-Bold, larger point size) maps to a
///   <see cref="Run"/> with <see cref="RunFormatting.Bold"/> == true and a
///   <see cref="RunFormatting.FontSizePt"/> larger than the body size.</item>
/// </list>
/// PDFs are synthesized in-test with <c>UglyToad.PdfPig.Writer.PdfDocumentBuilder</c>, so no
/// checked-in binaries are required.
/// </summary>
public sealed class PdfImportFidelityTests
{
    // ── paragraph-grouping tests ─────────────────────────────────────────────

    /// <summary>
    /// Two body paragraphs separated by a large vertical gap must come back as two separate
    /// <see cref="Paragraph"/> objects, not merged into one.
    /// </summary>
    [Fact]
    public void TwoBodyParagraphs_SeparatedByLargeGap_AreDistinctParagraphs()
    {
        // Arrange: build a PDF with two clearly separated text blocks.
        // Paragraph A: single line at y=700
        // Paragraph B: single line at y=580  (gap = 120pt >> 1.3 × 12pt threshold)
        var pdf = BuildFidelityPdf();

        // Act
        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdf);
        var doc = adapter.Load(stream);

        // Assert: we must have at least two paragraphs (the body paragraphs A and B, plus possibly
        // the heading as its own block — the assertion does not require exactly two).
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Count.Should().BeGreaterThanOrEqualTo(2,
            "two body blocks separated by a large gap must produce at least two Paragraph objects");

        // The total text recovered must include both body strings.
        var allText = string.Join(" ", paragraphs.Select(p => p.PlainText));
        allText.Should().Contain("FirstParagraph",
            "body paragraph A text must be present in the recovered document");
        allText.Should().Contain("SecondParagraph",
            "body paragraph B text must be present in the recovered document");
    }

    /// <summary>
    /// Two lines that belong to the same paragraph (small vertical gap, below 1.3× line-height
    /// threshold) must be merged into one <see cref="Paragraph"/>, not split into separate ones.
    /// </summary>
    [Fact]
    public void TwoCloseLines_WithSmallGap_AreMergedIntoOneParagraph()
    {
        // Arrange: two lines at y=700 and y=686 — gap = 14pt < 1.3×12 = 15.6pt threshold.
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("LineOne", 12, new PdfPoint(50, 700), font);
        page.AddText("LineTwo", 12, new PdfPoint(50, 686), font);   // gap = 14 < 15.6
        var pdf = builder.Build();

        // Act
        using var stream = new MemoryStream(pdf);
        var doc = new PdfFileAdapter().Load(stream);

        // Assert: the two lines must land in a single Paragraph.
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Count.Should().Be(1,
            "lines separated by a gap smaller than 1.3× line height must merge into one paragraph");

        var text = paragraphs[0].PlainText;
        text.Should().Contain("LineOne");
        text.Should().Contain("LineTwo");
    }

    // ── run-formatting tests ─────────────────────────────────────────────────

    /// <summary>
    /// The bold heading line (Helvetica-Bold 18pt) must produce a <see cref="Run"/> whose
    /// <see cref="RunFormatting.Bold"/> is true.
    /// </summary>
    [Fact]
    public void BoldHeadingLine_YieldsRunWithBoldTrue()
    {
        var pdf = BuildFidelityPdf();

        using var stream = new MemoryStream(pdf);
        var doc = new PdfFileAdapter().Load(stream);

        // Find the paragraph whose text contains the heading text.
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        var headingParagraph = paragraphs
            .FirstOrDefault(p => p.PlainText.Contains("BoldHeading"));

        headingParagraph.Should().NotBeNull("the bold heading text must be present in a paragraph");

        var headingRun = headingParagraph!.Runs.Should().ContainSingle().Subject;
        headingRun.Formatting.Bold.Should().BeTrue(
            "the heading was drawn with Helvetica-Bold, so the run must have Bold=true");
    }

    /// <summary>
    /// The bold heading line (18pt) must produce a <see cref="Run"/> whose
    /// <see cref="RunFormatting.FontSizePt"/> is larger than the body paragraphs' font size (12pt).
    /// </summary>
    [Fact]
    public void BoldHeadingLine_YieldsRunWithLargerFontSize()
    {
        var pdf = BuildFidelityPdf();

        using var stream = new MemoryStream(pdf);
        var doc = new PdfFileAdapter().Load(stream);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        var headingParagraph = paragraphs
            .FirstOrDefault(p => p.PlainText.Contains("BoldHeading"));
        headingParagraph.Should().NotBeNull("bold heading paragraph must be present");

        var bodyParagraph = paragraphs
            .FirstOrDefault(p => p.PlainText.Contains("FirstParagraph"));
        bodyParagraph.Should().NotBeNull("body paragraph must be present");

        var headingFontSize = headingParagraph!.Runs.First().Formatting.FontSizePt;
        var bodyFontSize = bodyParagraph!.Runs.First().Formatting.FontSizePt;

        headingFontSize.Should().HaveValue("heading run must have a FontSizePt");
        bodyFontSize.Should().HaveValue("body run must have a FontSizePt");

        headingFontSize!.Value.Should().BeGreaterThan(bodyFontSize!.Value,
            "heading at 18pt must have a larger FontSizePt than body at 12pt");
    }

    /// <summary>
    /// Normal (non-bold) body paragraphs must have <see cref="RunFormatting.Bold"/> == false.
    /// </summary>
    [Fact]
    public void BodyParagraph_YieldsRunWithBoldFalse()
    {
        var pdf = BuildFidelityPdf();

        using var stream = new MemoryStream(pdf);
        var doc = new PdfFileAdapter().Load(stream);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        var bodyParagraph = paragraphs
            .FirstOrDefault(p => p.PlainText.Contains("FirstParagraph"));

        bodyParagraph.Should().NotBeNull("body paragraph must be recoverable");
        var run = bodyParagraph!.Runs.Should().ContainSingle().Subject;
        run.Formatting.Bold.Should().BeFalse(
            "normal Helvetica body text must not be detected as bold");
    }

    // ── backward-compatibility: existing adapter capability tests ───────────

    /// <summary>
    /// Verifies that <see cref="PdfFileAdapter"/> remains open-only: CanOpen=true, CanSave=false,
    /// and Save throws <see cref="NotSupportedException"/>. This mirrors the existing test so we do
    /// not remove that coverage when adding the fidelity tests.
    /// </summary>
    [Fact]
    public void Adapter_Capabilities_RemainsOpenOnly()
    {
        var adapter = new PdfFileAdapter();
        adapter.Extension.Should().Be(".pdf");
        adapter.FormatName.Should().Be("PDF Document");

        var format = adapter.Formats.Should().ContainSingle().Subject;
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeFalse();

        using var ms = new MemoryStream();
        var act = () => adapter.Save(new TextDocument(), ms);
        act.Should().Throw<NotSupportedException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a one-page synthesised PDF that contains three distinct visual blocks:
    /// <list type="number">
    ///   <item>A bold heading "BoldHeading" at 18pt (Helvetica-Bold), y=750.</item>
    ///   <item>A normal-weight body paragraph "FirstParagraph" at 12pt, y=650.
    ///   The gap from heading to this line (100pt) is ≫ 1.3×18pt=23.4pt → separate paragraph.</item>
    ///   <item>A second body paragraph "SecondParagraph" at 12pt, y=540.
    ///   The gap from line 2 (110pt) is ≫ 1.3×12pt=15.6pt → separate paragraph.</item>
    /// </list>
    /// The large inter-block gaps ensure the paragraph-grouping heuristic splits them correctly.
    /// </summary>
    private static byte[] BuildFidelityPdf()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);

        var boldFont = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var normalFont = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Block 1: bold heading at 18pt.
        page.AddText("BoldHeading", 18, new PdfPoint(50, 750), boldFont);

        // Block 2: first body paragraph (gap from heading = 100pt >> threshold).
        page.AddText("FirstParagraph", 12, new PdfPoint(50, 650), normalFont);

        // Block 3: second body paragraph (gap from block 2 = 110pt >> threshold).
        page.AddText("SecondParagraph", 12, new PdfPoint(50, 540), normalFont);

        return builder.Build();
    }
}
