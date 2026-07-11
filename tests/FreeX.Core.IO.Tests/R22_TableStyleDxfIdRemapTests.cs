using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R22-io-sharedstrings-names-tables-1 regression test.
///
/// A custom table style's <c>tableStyleElement/@dxfId</c> is captured verbatim as raw text at load
/// time (<c>XlsxStructuredTableStyleMetadataReader.NativeXml</c>), tied to the SOURCE file's
/// <c>&lt;dxfs&gt;</c> array. On a full save, ClosedXML (plus the advanced conditional-format writer
/// that runs before <see cref="XlsxStructuredTableStyleMetadataWriter"/>) rebuilds
/// <c>xl/styles.xml</c>'s <c>&lt;dxfs&gt;</c> from scratch, containing only CF-tracked differential
/// styles. Re-emitting the table style's stale dxfId against that freshly rebuilt array silently
/// repoints the table's color at an unrelated CF color. The fix remaps each table-style element's
/// dxfId against the CURRENT <c>&lt;dxfs&gt;</c> array (using the <see cref="StyleDiff"/> the reader
/// already captured per-element at load time), appending a fresh dxf and pointing at it instead of
/// trusting the stale index.
/// </summary>
public sealed class R22_TableStyleDxfIdRemapTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void TableStyleDxfId_RemapsAgainstCurrentDxfs_PreservingOriginalColor()
    {
        // 1. Build a minimal valid xlsx package to post-process.
        var seedWorkbook = new Workbook("TableStyleDxfRemapSeed");
        seedWorkbook.AddSheet("Data");
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(seedWorkbook, stream);

        // 2. Simulate the state immediately BEFORE XlsxStructuredTableStyleMetadataWriter runs on a
        //    full save that added a second conditional-format rule: xl/styles.xml's <dxfs> has already
        //    been rebuilt by ClosedXML + XlsxAdvancedConditionalFormatWriter, containing two CF-owned
        //    dxfs (red at index 0, blue at index 1) that have nothing to do with the table style.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml")!;
            XDocument stylesXml;
            using (var s = stylesEntry.Open())
                stylesXml = XDocument.Load(s);

            stylesXml.Root!.Add(new XElement(
                MainNs + "dxfs",
                new XAttribute("count", "2"),
                new XElement(
                    MainNs + "dxf",
                    new XElement(
                        MainNs + "fill",
                        new XElement(
                            MainNs + "patternFill",
                            new XElement(MainNs + "fgColor", new XAttribute("rgb", "FFFF0000"))))),
                new XElement(
                    MainNs + "dxf",
                    new XElement(
                        MainNs + "fill",
                        new XElement(
                            MainNs + "patternFill",
                            new XElement(MainNs + "fgColor", new XAttribute("rgb", "FF0000FF")))))));

            stylesEntry.Delete();
            var newEntry = archive.CreateEntry("xl/styles.xml", CompressionLevel.Optimal);
            using var ws = newEntry.Open();
            stylesXml.Save(ws, SaveOptions.DisableFormatting);
        }

        // 3. Model the table style exactly as XlsxStructuredTableStyleMetadataReader would have
        //    captured it from the ORIGINAL source file: NativeXml still carries the stale dxfId="1"
        //    (the table's own yellow dxf position in the source file), plus the StyleDiff the reader
        //    derives from that differential style at load time (independent of dxfId reindexing).
        var style = new StructuredTableStyleModel
        {
            Name = "CustomStyle",
            AppliesToTables = true,
            NativeXml =
                "<tableStyle xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "name=\"CustomStyle\" pivot=\"0\" table=\"1\" count=\"1\">" +
                "<tableStyleElement type=\"wholeTable\" dxfId=\"1\"/></tableStyle>"
        };
        style.Elements.Add(new StructuredTableStyleElementModel(
            "wholeTable",
            DifferentialFormatId: 1,
            Size: null,
            Format: new StyleDiff(FillColor: CellColor.FromArgb(0xFF, 0xCC, 0x00))));

        var workbook = new Workbook("TableStyleDxfRemap");
        workbook.StructuredTableStyles.Add(style);

        // 4. Run the writer under test directly against the already-rebuilt-dxfs package.
        stream.Position = 0;
        XlsxStructuredTableStyleMetadataWriter.Save(stream, workbook);

        // 5. Resolve the written tableStyleElement's dxfId against the resulting package's CURRENT
        //    <dxfs> array and assert it resolves to the table's ORIGINAL yellow color, not the
        //    pre-existing unrelated CF blue that already occupies dxf index 1.
        stream.Position = 0;
        using var resultArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var resultStylesEntry = resultArchive.GetEntry("xl/styles.xml")!;
        XDocument resultStylesXml;
        using (var s = resultStylesEntry.Open())
            resultStylesXml = XDocument.Load(s);

        var tableStyleElement = resultStylesXml.Root!
            .Element(MainNs + "tableStyles")!
            .Elements(MainNs + "tableStyle")
            .Single(e => e.Attribute("name")?.Value == "CustomStyle")
            .Elements(MainNs + "tableStyleElement")
            .Single(e => e.Attribute("type")?.Value == "wholeTable");

        var dxfIdAttribute = tableStyleElement.Attribute("dxfId");
        dxfIdAttribute.Should().NotBeNull("the table's differential format must survive a full save");

        var dxfId = int.Parse(dxfIdAttribute!.Value);
        var dxfs = resultStylesXml.Root.Element(MainNs + "dxfs")!.Elements(MainNs + "dxf").ToList();
        dxfId.Should().BeLessThan(dxfs.Count, "the remapped dxfId must point at a real dxf entry");

        var resolvedFgColor = dxfs[dxfId]
            .Element(MainNs + "fill")?
            .Element(MainNs + "patternFill")?
            .Element(MainNs + "fgColor")?
            .Attribute("rgb")?.Value;

        resolvedFgColor.Should().Be("FFFFCC00",
            "the table style's whole-table dxfId must resolve to its ORIGINAL yellow fill, not the " +
            "unrelated CF blue fill that already occupies dxf index 1 in the freshly regenerated <dxfs>");
    }
}
