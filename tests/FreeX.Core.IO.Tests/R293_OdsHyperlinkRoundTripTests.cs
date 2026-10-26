using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r293: the ODS adapter had no hyperlink handling on either side, so every link was lost.
///
/// <para>Found by measurement while extending r291/r292's save-loss survey: a workbook with a chart,
/// a shape and a hyperlink was round-tripped through every adapter, and ODS -- a rich format that
/// keeps sheets, styles, formulas and named ranges -- returned zero hyperlinks. Unlike CSV, this was
/// not the format's limit: ODF carries a link as <c>text:p/text:a/@xlink:href</c>. The adapter simply
/// never read or wrote one.</para>
///
/// <para>The loss was invisible in a way that matters: the link's visible TEXT survived, because the
/// reader flattens the paragraph. A user saw the words and only discovered the link was gone by
/// clicking it.</para>
/// </summary>
public sealed class R293_OdsHyperlinkRoundTripTests
{
    private const string Target = "https://example.com/report?q=1&x=2";

    private static Workbook WorkbookWithHyperlink(string text = "Annual report")
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue(text));
        sheet.Hyperlinks[address] = Target;
        return workbook;
    }

    private static Sheet RoundTrip(Workbook workbook)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets.First();
    }

    [Fact]
    public void AHyperlinkSurvivesTheRoundTrip()
    {
        var sheet = RoundTrip(WorkbookWithHyperlink());
        var address = new CellAddress(sheet.Id, 2, 3);

        sheet.Hyperlinks.Should().ContainKey(address);
        sheet.Hyperlinks[address].Should().Be(Target,
            "ODF represents a hyperlink and the adapter now writes and reads it; before r293 the "
            + "target was dropped while the link text stayed, so the loss was invisible until "
            + "someone clicked");
    }

    /// <summary>
    /// The visible text must not regress while gaining the link -- wrapping it in text:a moves it a
    /// level deeper in the XML, which is exactly the kind of change that quietly empties a cell.
    /// </summary>
    [Fact]
    public void TheLinkTextIsStillTheCellsValue()
    {
        var sheet = RoundTrip(WorkbookWithHyperlink("Annual report"));

        sheet.GetValue(new CellAddress(sheet.Id, 2, 3))
            .Should().Be(new TextValue("Annual report"));
    }

    /// <summary>
    /// A URL with characters that are special in XML must survive as itself; ampersands in query
    /// strings are the common case and the one a naive string-concatenating writer corrupts.
    /// </summary>
    [Fact]
    public void AUrlWithXmlSpecialCharactersIsNotCorrupted()
    {
        var sheet = RoundTrip(WorkbookWithHyperlink());

        sheet.Hyperlinks[new CellAddress(sheet.Id, 2, 3)]
            .Should().Be(Target, "the target contains '&' and must not arrive entity-mangled");
    }

    [Fact]
    public void ACellWithoutAHyperlinkGainsNone()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("plain"));

        var loaded = RoundTrip(workbook);

        loaded.Hyperlinks.Should().BeEmpty("a plain text cell must still write a plain paragraph");
        loaded.GetValue(new CellAddress(loaded.Id, 1, 1)).Should().Be(new TextValue("plain"));
    }

    /// <summary>
    /// Reading is the half that also fixes files this adapter did not write: LibreOffice nests a
    /// formatted link inside a styled span, so the target is not a direct child of the paragraph.
    /// </summary>
    [Fact]
    public void AHyperlinkNestedInsideASpanIsStillFound()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        // Rebuild the package content with the link wrapped in a span, as LibreOffice writes it.
        var rewritten = RewriteContentXml(stream.ToArray(), content =>
        {
            var paragraph = content.Descendants()
                .First(element => element.Name.LocalName == "p");
            paragraph.RemoveNodes();
            paragraph.Add(new System.Xml.Linq.XElement(
                OdsFileAdapter.TextNs + "span",
                new System.Xml.Linq.XElement(
                    OdsFileAdapter.TextNs + "a",
                    new System.Xml.Linq.XAttribute(OdsFileAdapter.XlinkNs + "href", Target),
                    "x")));
        });

        using var reopened = new MemoryStream(rewritten);
        var loaded = adapter.Load(reopened).Sheets.First();

        loaded.Hyperlinks.Should().ContainKey(new CellAddress(loaded.Id, 1, 1),
            "a link inside a styled span is what a real ODF producer writes, so a fixed "
            + "paragraph/anchor path would read our own files and no one else's");
    }

    /// <summary>
    /// Rewrites content.xml inside an ODS package, so a test can present the adapter with markup a
    /// DIFFERENT producer would write rather than only the shape this adapter emits.
    /// </summary>
    private static byte[] RewriteContentXml(byte[] package, Action<System.Xml.Linq.XDocument> edit)
    {
        using var source = new MemoryStream(package);
        using var archive = new System.IO.Compression.ZipArchive(
            source, System.IO.Compression.ZipArchiveMode.Read);

        var entries = new List<(string Name, byte[] Bytes)>();
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            entries.Add((entry.FullName, buffer.ToArray()));
        }

        var contentIndex = entries.FindIndex(entry =>
            string.Equals(entry.Name, "content.xml", StringComparison.Ordinal));
        contentIndex.Should().BeGreaterThanOrEqualTo(0, "an ODS package always carries content.xml");

        var document = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(entries[contentIndex].Bytes));
        edit(document);
        entries[contentIndex] = (
            entries[contentIndex].Name,
            System.Text.Encoding.UTF8.GetBytes(document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)));

        using var target = new MemoryStream();
        using (var rebuilt = new System.IO.Compression.ZipArchive(
            target, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = rebuilt.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }

        return target.ToArray();
    }
}
