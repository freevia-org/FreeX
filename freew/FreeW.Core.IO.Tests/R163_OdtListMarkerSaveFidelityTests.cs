using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// meta F2 (round 163): round-162 wave A taught <see cref="OdtFileAdapter"/>'s READER to capture a foreign
/// list's real bullet glyph (<c>text:bullet-char</c>) / number format (<c>style:num-format</c>) instead of
/// silently normalizing it to FreeW's own default -- but <c>OdtStyleWriter.BuildListStyle</c> still hardcoded
/// '•'/num-format "1" for every list on WRITE, regardless of what was just read. That flips the defect from
/// "visibly wrong the instant you open the file" (harmless because it's obvious) into "silently destroyed the
/// moment you press Ctrl+S with no edits" (a real, invisible data-loss regression this round introduced).
///
/// As the round-163 directive requires, these tests exercise the ONLY shape that can see this bug:
/// build a foreign .odt by hand, load it, save it back with zero edits, reload the saved bytes, and assert
/// the marker is still there. A read-only test (like R162's) cannot catch this; a round-trip test against
/// FreeW's own writer output alone cannot either, because before this fix the writer was uniformly wrong in
/// both directions and a naive round-trip would look "consistent".
/// </summary>
public class R163_OdtListMarkerSaveFidelityTests
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    private static byte[] BuildForeignOdt(string contentXml)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var es = mimeEntry.Open())
            {
                var bytes = Encoding.ASCII.GetBytes(OdtFileAdapter.MimeType);
                es.Write(bytes, 0, bytes.Length);
            }
            var contentEntry = archive.CreateEntry("content.xml", CompressionLevel.Optimal);
            using (var es = contentEntry.Open())
            using (var writer = new StreamWriter(es))
                writer.Write(contentXml);
        }
        return ms.ToArray();
    }

    private static string ReadContentXml(byte[] odtBytes)
    {
        using var ms = new MemoryStream(odtBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("content.xml")!;
        using var es = entry.Open();
        using var reader = new StreamReader(es);
        return reader.ReadToEnd();
    }

    /// <summary>Load foreign bytes, save with zero edits, and return the reloaded document plus the raw
    /// saved content.xml (so a test can assert on both the model AND the literal emitted markup).</summary>
    private static (TextDocument Reloaded, string SavedContentXml) RoundTripUnedited(byte[] foreignOdtBytes)
    {
        var adapter = OdtFileAdapter.Odt();
        using var loadStream = new MemoryStream(foreignOdtBytes);
        var document = adapter.Load(loadStream);

        using var saveStream = new MemoryStream();
        adapter.Save(document, saveStream);
        var savedBytes = saveStream.ToArray();

        using var reloadStream = new MemoryStream(savedBytes);
        var reloaded = adapter.Load(reloadStream);
        return (reloaded, ReadContentXml(savedBytes));
    }

    private static readonly string SquareBulletAndLowerRomanContentXml =
        $"""
        <office:document-content xmlns:office="{OfficeNs}" xmlns:text="{TextNs}" xmlns:style="{StyleNs}" office:version="1.3">
          <office:automatic-styles>
            <text:list-style style:name="L1">
              <text:list-level-style-bullet text:level="1" text:bullet-char="▪"/>
            </text:list-style>
            <text:list-style style:name="L2">
              <text:list-level-style-number text:level="1" style:num-format="i" style:num-suffix="."/>
            </text:list-style>
          </office:automatic-styles>
          <office:body>
            <office:text>
              <text:list text:style-name="L1">
                <text:list-item><text:p>Alpha</text:p></text:list-item>
                <text:list-item><text:p>Beta</text:p></text:list-item>
              </text:list>
              <text:list text:style-name="L2">
                <text:list-item><text:p>One</text:p></text:list-item>
                <text:list-item><text:p>Two</text:p></text:list-item>
              </text:list>
            </office:text>
          </office:body>
        </office:document-content>
        """;

    [Fact]
    public void SaveAndReload_ForeignSquareBulletSurvivesUnedited()
    {
        var foreignBytes = BuildForeignOdt(SquareBulletAndLowerRomanContentXml);

        var (reloaded, savedContentXml) = RoundTripUnedited(foreignBytes);

        var bulletItems = reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .ToList();
        bulletItems.Should().HaveCount(2);
        bulletItems.Should().OnlyContain(p => p.Formatting.ListMarkerText == "▪",
            because: "a save with zero edits must re-emit the foreign document's own bullet-char, not FreeW's default '•'");

        // Assert on the literal emitted markup too (not just the re-parsed model) -- this is the exact
        // regression shape: the writer used to hardcode bullet-char="•" regardless of what was read.
        savedContentXml.Should().Contain("bullet-char=\"▪\"");
        savedContentXml.Should().NotContain("bullet-char=\"•\"",
            because: "the saved file must not contain FreeW's default bullet glyph when the source list never used it");
    }

    [Fact]
    public void SaveAndReload_ForeignLowerRomanNumFormatSurvivesUnedited()
    {
        var foreignBytes = BuildForeignOdt(SquareBulletAndLowerRomanContentXml);

        var (reloaded, savedContentXml) = RoundTripUnedited(foreignBytes);

        var numberItems = reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .ToList();
        numberItems.Should().HaveCount(2);
        numberItems.Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            because: "a save with zero edits must re-emit the foreign document's own lower-roman numbering, not FreeW's decimal default");

        savedContentXml.Should().Contain("num-format=\"i\"");
    }

    /// <summary>
    /// Sibling no-regression: a FreeW-authored (or plain default-marker) list must keep saving as FreeW's
    /// own default bullet/decimal numbering exactly as before this fix -- the cache-key change must not make
    /// every list distinct or otherwise disturb the default path.
    /// </summary>
    [Fact]
    public void SaveAndReload_DefaultMarkerListsStillRoundTripToFreeWDefaults()
    {
        var document = new TextDocument();
        var bulletParagraph = new Paragraph { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 } };
        bulletParagraph.Runs.Add(new Run("Alpha"));
        document.Blocks.Add(bulletParagraph);
        var numberParagraph = new Paragraph { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 } };
        numberParagraph.Runs.Add(new Run("One"));
        document.Blocks.Add(numberParagraph);

        var adapter = OdtFileAdapter.Odt();
        using var saveStream = new MemoryStream();
        adapter.Save(document, saveStream);
        var savedBytes = saveStream.ToArray();
        var savedContentXml = ReadContentXml(savedBytes);

        using var reloadStream = new MemoryStream(savedBytes);
        var reloaded = adapter.Load(reloadStream);

        reloaded.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Bullet)
            .Formatting.ListMarkerText.Should().BeNull();
        reloaded.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Number)
            .Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);

        savedContentXml.Should().Contain("bullet-char=\"•\"");
        savedContentXml.Should().Contain("num-format=\"1\"");
    }
}
