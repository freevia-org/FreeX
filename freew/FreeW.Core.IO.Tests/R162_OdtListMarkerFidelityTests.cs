using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// meta F3 (round 162): the ODT reader captured only <see cref="ListKind"/>/<see cref="ParagraphFormatting.ListLevel"/>
/// for a list paragraph, never the actual bullet glyph (<c>text:bullet-char</c>) or number format
/// (<c>style:num-format</c>) the source document's <c>text:list-style</c> carries -- so a foreign .odt with a
/// custom bullet or a non-decimal numbered list silently opened showing FreeW's generic round-bullet/decimal
/// marker. These tests build a raw ODT package by hand (a real ODF producer's shape, not FreeW's own writer
/// output, so the sweep-99 "our writer only emits the default" trap can't hide the bug) and load it directly.
/// </summary>
public class R162_OdtListMarkerFidelityTests
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    private static readonly string ContentXml =
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

    private static TextDocument LoadForeignOdt()
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
                writer.Write(ContentXml);
        }
        ms.Position = 0;
        return OdtFileAdapter.Odt().Load(ms);
    }

    [Fact]
    public void Load_CapturesForeignSquareBulletChar()
    {
        var document = LoadForeignOdt();
        var bulletItems = document.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .ToList();

        bulletItems.Should().HaveCount(2);
        bulletItems.Should().OnlyContain(p => p.Formatting.ListMarkerText == "▪",
            because: "a text:bullet-char of '▪' is a different glyph than FreeW's default round '•' and must not be silently normalized to it");
    }

    [Fact]
    public void Load_CapturesForeignLowerRomanNumFormat()
    {
        var document = LoadForeignOdt();
        var numberItems = document.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .ToList();

        numberItems.Should().HaveCount(2);
        numberItems.Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            because: "style:num-format=\"i\" is lower-roman, not FreeW's decimal default, and must not be silently normalized to it");
    }

    /// <summary>
    /// Sibling no-regression: a plain default bullet/number list (bullet-char '•', num-format "1" -- what
    /// FreeW's own writer already emits, and what an ODF producer emits for an unstyled list) must keep
    /// resolving to null/Decimal exactly as before this fix.
    /// </summary>
    [Fact]
    public void DefaultBulletAndNumberStyles_StillResolveToNullMarkerAndDecimalFormat()
    {
        const string contentXml =
            $"""
            <office:document-content xmlns:office="{OfficeNs}" xmlns:text="{TextNs}" xmlns:style="{StyleNs}" office:version="1.3">
              <office:automatic-styles>
                <text:list-style style:name="LB1">
                  <text:list-level-style-bullet text:level="1" text:bullet-char="•"/>
                </text:list-style>
                <text:list-style style:name="LN1">
                  <text:list-level-style-number text:level="1" style:num-format="1" style:num-suffix="."/>
                </text:list-style>
              </office:automatic-styles>
              <office:body>
                <office:text>
                  <text:list text:style-name="LB1">
                    <text:list-item><text:p>Alpha</text:p></text:list-item>
                  </text:list>
                  <text:list text:style-name="LN1">
                    <text:list-item><text:p>One</text:p></text:list-item>
                  </text:list>
                </office:text>
              </office:body>
            </office:document-content>
            """;

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
        ms.Position = 0;
        var document = OdtFileAdapter.Odt().Load(ms);

        document.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Bullet)
            .Formatting.ListMarkerText.Should().BeNull();
        document.Blocks.OfType<Paragraph>().First(p => p.Formatting.ListKind == ListKind.Number)
            .Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);
    }
}
