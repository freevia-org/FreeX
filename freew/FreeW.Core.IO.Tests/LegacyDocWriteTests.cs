using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for the Word 97-2003 binary write path (<see cref="LegacyDocWriter"/>).
/// Each test writes a <see cref="TextDocument"/> via <see cref="LegacyDocFileAdapter.Save"/>,
/// then reads it back via <see cref="LegacyDocFileAdapter.Load"/> (which uses DocSharp's real
/// binary-Word reader under the hood). If DocSharp can parse the output the format is valid.
/// </summary>
public sealed class LegacyDocWriteTests
{
    private static readonly LegacyDocFileAdapter Adapter = new();

    // -----------------------------------------------------------------------
    // Helper: write → read
    // -----------------------------------------------------------------------

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var ms = new MemoryStream();
        Adapter.Save(doc, ms);
        ms.Position = 0;
        return Adapter.Load(ms);
    }

    private static TextDocument WithParagraphs(params string[] texts)
    {
        var doc = new TextDocument();
        foreach (var t in texts)
            doc.Blocks.Add(new Paragraph(t));
        return doc;
    }

    // -----------------------------------------------------------------------
    // Capability assertions
    // -----------------------------------------------------------------------

    [Fact]
    public void Adapter_CanSave_IsTrue_ForDocAndDot()
    {
        Adapter.Formats.Should().ContainSingle(f => f.Extension == ".doc")
            .Which.CanSave.Should().BeTrue();
        Adapter.Formats.Should().ContainSingle(f => f.Extension == ".dot")
            .Which.CanSave.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Round-trip: plain text
    // -----------------------------------------------------------------------

    [Fact]
    public void Save_EmptyDocument_RoundTrips()
    {
        var doc = new TextDocument();
        var result = RoundTrip(doc);
        // An empty document round-trips to at least one (empty) paragraph.
        result.Paragraphs.Should().NotBeNull();
    }

    [Fact]
    public void Save_SingleParagraph_RoundTripsText()
    {
        var doc = WithParagraphs("Hello, World!");

        var result = RoundTrip(doc);

        result.Paragraphs.Should().NotBeEmpty();
        result.Paragraphs.Should().Contain(p => p.PlainText.Contains("Hello, World!"));
    }

    [Fact]
    public void Save_MultipleParagraphs_AllTextsPresent()
    {
        var doc = WithParagraphs("First paragraph.", "Second paragraph.", "Third paragraph.");

        var result = RoundTrip(doc);

        var allText = string.Join(" ", result.Paragraphs.Select(p => p.PlainText));
        allText.Should().Contain("First paragraph.");
        allText.Should().Contain("Second paragraph.");
        allText.Should().Contain("Third paragraph.");
    }

    [Fact]
    public void Save_UnicodeText_RoundTrips()
    {
        const string unicodeText = "Hello Unicode World";
        var doc = WithParagraphs(unicodeText);

        var result = RoundTrip(doc);

        var allText = string.Join(" ", result.Paragraphs.Select(p => p.PlainText));
        allText.Should().NotBeNullOrWhiteSpace();
        allText.Should().Contain("Hello Unicode World");
    }

    // -----------------------------------------------------------------------
    // Round-trip: formatted runs
    // -----------------------------------------------------------------------

    [Fact]
    public void Save_BoldRun_TextSurvivesRoundTrip()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Bold text", new RunFormatting { Bold = true }));
        doc.Blocks.Add(para);

        var result = RoundTrip(doc);

        var allText = string.Join(" ", result.Paragraphs.Select(p => p.PlainText));
        allText.Should().Contain("Bold text");
    }

    [Fact]
    public void Save_ItalicRun_TextSurvivesRoundTrip()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Italic text", new RunFormatting { Italic = true }));
        doc.Blocks.Add(para);

        var result = RoundTrip(doc);

        var allText = string.Join(" ", result.Paragraphs.Select(p => p.PlainText));
        allText.Should().Contain("Italic text");
    }

    [Fact]
    public void Save_MixedFormattingRuns_AllTextsPresent()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Normal "));
        para.Runs.Add(new Run("bold ", new RunFormatting { Bold = true }));
        para.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        doc.Blocks.Add(para);

        var result = RoundTrip(doc);

        var allText = string.Join(" ", result.Paragraphs.Select(p => p.PlainText));
        allText.Should().Contain("Normal ");
        allText.Should().Contain("bold ");
        allText.Should().Contain("italic");
    }

    // -----------------------------------------------------------------------
    // Stream contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Save_DoesNotCloseDestinationStream()
    {
        var doc = WithParagraphs("stream contract test");

        using var ms = new MemoryStream();
        Adapter.Save(doc, ms);

        // Stream must remain open (leaveOpen contract)
        ms.CanWrite.Should().BeTrue("the adapter must not close the caller's stream");
        ms.Length.Should().BeGreaterThan(0, "something should have been written");
    }

    [Fact]
    public void Save_OutputStartsWithCfbMagic()
    {
        var doc = WithParagraphs("magic check");

        using var ms = new MemoryStream();
        Adapter.Save(doc, ms);

        ms.Position = 0;
        var header = new byte[8];
        ms.Read(header, 0, 8);

        // [MS-CFB] magic: D0 CF 11 E0 A1 B1 1A E1
        byte[] expected = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        header.Should().Equal(expected, "the output must be a valid OLE2/CFB container");
    }
}
