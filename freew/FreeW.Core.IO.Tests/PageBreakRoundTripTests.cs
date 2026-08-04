using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;
using FluentAssertions;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for manual page breaks (<c>w:br w:type="page"</c>). Previously the reader dropped
/// break-only runs entirely, so a Ctrl+Enter page break was lost on open and FreeW under-paginated.
/// </summary>
public class PageBreakRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadDocumentWithMixedRun(params object[] children)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(TextDocument.CreateEmpty(), stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("word/document.xml")!;
            XDocument xml;
            using (var input = entry.Open())
                xml = XDocument.Load(input);

            var paragraph = xml.Descendants(W + "p").First();
            paragraph.Elements(W + "r").Remove();
            paragraph.Add(new XElement(W + "r", children));

            entry.Delete();
            var replacement = archive.CreateEntry("word/document.xml");
            using var output = replacement.Open();
            xml.Save(output);
        }

        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument PageBreakDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("before"));
        p.Runs.Add(Run.PageBreak());
        p.Runs.Add(new Run("after"));
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument ColumnBreakDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("before"));
        p.Runs.Add(Run.ColumnBreak());
        p.Runs.Add(new Run("after"));
        doc.Blocks.Add(p);
        return doc;
    }

    [Fact]
    public void PageBreakRun_SurvivesRoundTrip()
    {
        var result = RoundTrip(PageBreakDocument());
        var paragraph = result.Blocks.OfType<Paragraph>().First();

        // The break is preserved, positioned between the two text runs.
        var kinds = paragraph.Runs.Select(r => r.IsPageBreak ? "break" : r.Text).ToList();
        Assert.Equal(new[] { "before", "break", "after" }, kinds);
    }

    [Fact]
    public void PageBreak_EmitsBrTypePage()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(PageBreakDocument(), stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        var br = xml.Descendants(W + "br").FirstOrDefault(b => b.Attribute(W + "type")?.Value == "page");
        Assert.NotNull(br);
    }

    [Fact]
    public void ColumnBreakRun_SurvivesRoundTripWithoutBecomingPageBreak()
    {
        var paragraph = RoundTrip(ColumnBreakDocument()).Blocks.OfType<Paragraph>().First();

        var kinds = paragraph.Runs.Select(r => r.IsColumnBreak ? "column" : r.Text).ToList();
        Assert.Equal(new[] { "before", "column", "after" }, kinds);
        Assert.DoesNotContain(paragraph.Runs, run => run.IsPageBreak);
    }

    [Fact]
    public void ColumnBreak_EmitsBrTypeColumn()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(ColumnBreakDocument(), stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        Assert.Single(xml.Descendants(W + "br"),
            element => element.Attribute(W + "type")?.Value == "column");
        Assert.DoesNotContain(xml.Descendants(W + "br"),
            element => element.Attribute(W + "type")?.Value == "page");
    }

    [Theory]
    [InlineData("page")]
    [InlineData("column")]
    public void MixedTextAndBreakChildren_PreserveAuthoredOrder(string breakType)
    {
        var document = ReadDocumentWithMixedRun(
            new XElement(W + "rPr", new XElement(W + "b")),
            new XElement(W + "t", "before"),
            new XElement(W + "br", new XAttribute(W + "type", breakType)),
            new XElement(W + "t", "after"));

        var runs = document.Blocks.OfType<Paragraph>().Single().Runs;
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("before");
        runs[1].IsPageBreak.Should().Be(breakType == "page");
        runs[1].IsColumnBreak.Should().Be(breakType == "column");
        runs[2].Text.Should().Be("after");
        runs.Should().OnlyContain(run => run.Formatting.Bold);

        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        using var entry = archive.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);
        var ordered = xml.Descendants(W + "p").Single().Elements(W + "r")
            .SelectMany(run => run.Elements().Where(element => element.Name != W + "rPr"))
            .Select(element => element.Name == W + "br"
                ? $"break:{element.Attribute(W + "type")?.Value}"
                : element.Value)
            .ToList();
        ordered.Should().Equal("before", $"break:{breakType}", "after");

        saved.Position = 0;
        var reopened = DocxReader.Read(saved).Blocks.OfType<Paragraph>().Single().Runs;
        reopened.Select(run => run.IsPageBreak
                ? "break:page"
                : run.IsColumnBreak
                    ? "break:column"
                    : run.Text)
            .Should().Equal("before", $"break:{breakType}", "after");
    }

    [Fact]
    public void MixedTextWrappingBreakAndSoftHyphen_PreserveInlinePositions()
    {
        var document = ReadDocumentWithMixedRun(
            new XElement(W + "t", "first"),
            new XElement(W + "br"),
            new XElement(W + "t", "sec"),
            new XElement(W + "softHyphen"),
            new XElement(W + "t", "ond"));

        var run = document.Blocks.OfType<Paragraph>().Single().Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be($"first\nsec{Hyphenator.SoftHyphen}ond");

        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        using var entry = archive.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);
        var children = xml.Descendants(W + "r").Single().Elements()
            .Where(element => element.Name != W + "rPr")
            .Select(element => element.Name.LocalName == "t" ? element.Value : element.Name.LocalName)
            .ToList();
        children.Should().Equal("first", "br", "sec", "softHyphen", "ond");

        saved.Position = 0;
        DocxReader.Read(saved).Blocks.OfType<Paragraph>().Single().Runs
            .Should().ContainSingle().Which.Text
            .Should().Be($"first\nsec{Hyphenator.SoftHyphen}ond");
    }
}
