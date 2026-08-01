using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

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
}
