using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxStylesheetMetadataPreserverTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // A rebuilt package reorders the dxf list relative to the source (ClosedXML's dxfs plus FreeX's
    // appended advanced-conditional-format dxfs land in a different order than the original). Merging the
    // source dxfs into the rebuilt dxfs by raw index therefore lands native font/fill content on an
    // unrelated dxf. The preserver must only merge when the two dxfs render the same visible style.
    [Fact]
    public void Preserve_DoesNotInjectFontColorIntoPositionallyMismatchedDifferentialStyle()
    {
        using var sourcePackage = CreatePackage(("xl/styles.xml", StyleSheet(
            // Source dxf[0]: red font on a pink fill, carrying a native attribute so the source counts as
            // preservable stylesheet metadata.
            """
            <dxf nativeDxfAttr="kept">
              <font><color rgb="FFCC0000"/></font>
              <fill><patternFill patternType="solid"><fgColor rgb="FFF4CCCC"/></patternFill></fill>
            </dxf>
            """)));
        using var targetPackage = CreatePackage(("xl/styles.xml", StyleSheet(
            // Rebuilt dxf[0] at the same index is an unrelated rule: green fill, no font.
            """
            <dxf>
              <fill><patternFill patternType="solid"><fgColor rgb="FFD9EAD3"/></patternFill></fill>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.ToString(SaveOptions.DisableFormatting).Should().NotContain("FFCC0000",
            "a positionally mismatched source dxf must not inject its red font into an unrelated rebuilt dxf");
        targetDxf.Descendants(WorkbookNs + "fgColor").Single().Attribute("rgb")!.Value.Should().Be("FFD9EAD3",
            "the rebuilt dxf's own green fill must be preserved unchanged");
    }

    [Fact]
    public void Preserve_MergesNativeMetadataIntoPositionallyMatchedDifferentialStyle()
    {
        XNamespace fx = "urn:freex-test";
        using var sourcePackage = CreatePackage(("xl/styles.xml", StyleSheet(
            $"""
            <dxf nativeDxfAttr="kept">
              <font><color rgb="FF112233"/></font>
              <dxfNativeChild xmlns="{fx}" id="dxf-child"/>
            </dxf>
            """)));
        using var targetPackage = CreatePackage(("xl/styles.xml", StyleSheet(
            // Same visible style (font color FF112233) but missing the native attribute and child.
            """
            <dxf>
              <font><color rgb="FF112233"/></font>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.Attribute("nativeDxfAttr")!.Value.Should().Be("kept",
            "native metadata must still be recovered when the source and rebuilt dxf render the same style");
        targetDxf.Descendants(fx + "dxfNativeChild").Should().ContainSingle();
    }

    private static void Preserve(MemoryStream sourcePackage, MemoryStream targetPackage)
    {
        sourcePackage.Position = 0;
        targetPackage.Position = 0;
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, targetArchive);
        }
    }

    private static XDocument LoadStylesheet(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = archive.GetEntry("xl/styles.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static string StyleSheet(string dxfXml) =>
        $"""
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dxfs count="1">{dxfXml}</dxfs>
        </styleSheet>
        """;

    private static MemoryStream CreatePackage(params (string Path, string Xml)[] entries)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(xml);
            }
        }

        package.Position = 0;
        return package;
    }
}
