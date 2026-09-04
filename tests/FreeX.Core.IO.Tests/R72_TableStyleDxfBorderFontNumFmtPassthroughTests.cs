using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R72-io-theme-cellstyles-4-1 regression test.
///
/// A custom tableStyle's per-element dxf (e.g. a headerRow with a bold-blue font PLUS a medium
/// bottom border) used to lose its border/numFmt/font-name/size/italic/underline on save:
/// <see cref="XlsxStructuredTableStyleMetadataReader"/> only captured Bold/FontColor/Fill into the
/// <see cref="StyleDiff"/>, and <see cref="XlsxStructuredTableStyleMetadataWriter"/> rebuilt the dxf
/// from that lossy diff, unconditionally, via <c>RemapDifferentialFormatIds</c>. The fix reads and
/// writes the dxf's &lt;border&gt;, &lt;numFmt&gt;, and full &lt;font&gt; (name/size/italic/underline)
/// using the <see cref="StyleDiff"/> fields that already model them (the same fields the CF dxf path
/// in <see cref="XlsxDifferentialStyleReader"/> models), so a round-tripped tableStyle keeps its
/// border byte-equivalent instead of silently dropping it.
/// </summary>
public sealed class R72_TableStyleDxfBorderFontNumFmtPassthroughTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string StylesXmlWithBorderFontNumFmtDxf =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<dxfs count=\"1\">" +
        "<dxf>" +
        "<font><b/><color rgb=\"FF0000FF\"/></font>" +
        "<numFmt numFmtId=\"200\" formatCode=\"0.00%\"/>" +
        "<border><bottom style=\"medium\"><color rgb=\"FF000000\"/></bottom></border>" +
        "</dxf>" +
        "</dxfs>" +
        "<tableStyles count=\"1\">" +
        "<tableStyle name=\"CustomHeaderBorderStyle\" pivot=\"0\" table=\"1\" count=\"1\">" +
        "<tableStyleElement type=\"headerRow\" dxfId=\"0\"/>" +
        "</tableStyle>" +
        "</tableStyles>" +
        "</styleSheet>";

    private const string StylesXmlWithFillOnlyDxf =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<dxfs count=\"1\">" +
        "<dxf>" +
        "<fill><patternFill><fgColor rgb=\"FFCCFFCC\"/></patternFill></fill>" +
        "</dxf>" +
        "</dxfs>" +
        "<tableStyles count=\"1\">" +
        "<tableStyle name=\"CustomFillOnlyStyle\" pivot=\"0\" table=\"1\" count=\"1\">" +
        "<tableStyleElement type=\"headerRow\" dxfId=\"0\"/>" +
        "</tableStyle>" +
        "</tableStyles>" +
        "</styleSheet>";

    [Fact]
    public void HeaderRowDxf_WithBoldFontAndBorderAndNumFmt_RoundTripsAllThree()
    {
        // 1. Read the custom tableStyle's headerRow dxf exactly as the source file models it: a
        //    bold-blue font, a medium bottom border, and a percent number format.
        var stylesXml = XDocument.Parse(StylesXmlWithBorderFontNumFmtDxf);
        var models = XlsxStructuredTableStyleMetadataReader.Load(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        models.Should().ContainSingle();
        var headerRow = models[0].Elements.Should().ContainSingle(e => e.Type == "headerRow").Subject;
        headerRow.Format.Should().NotBeNull("the reader must capture the dxf's border/font/numFmt content, not just Bold/FontColor/Fill");
        headerRow.Format!.Bold.Should().BeTrue();
        headerRow.Format.FontColor.Should().Be(CellColor.FromArgb(0x00, 0x00, 0xFF));
        headerRow.Format.NumberFormat.Should().Be("0.00%");
        headerRow.Format.BorderBottom.Should().NotBeNull();
        headerRow.Format.BorderBottom!.Value.Style.Should().Be(BorderStyle.Medium);
        headerRow.Format.BorderBottom.Value.Color.Should().Be(CellColor.Black);

        // 2. Save the captured model into a fresh package and confirm the written dxf still carries
        //    the border/numFmt/font, not just the fill/bold/color subset.
        var workbook = new Workbook("TableStyleBorderFontNumFmtRoundTrip");
        workbook.AddSheet("Data");
        workbook.StructuredTableStyles.Add(models[0]);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        XlsxStructuredTableStyleMetadataWriter.Save(stream, workbook);

        stream.Position = 0;
        using var resultArchive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var resultStylesEntry = resultArchive.GetEntry("xl/styles.xml")!;
        XDocument resultStylesXml;
        using (var s = resultStylesEntry.Open())
            resultStylesXml = XDocument.Load(s);

        var tableStyleElement = resultStylesXml.Root!
            .Element(MainNs + "tableStyles")!
            .Elements(MainNs + "tableStyle")
            .Single(e => e.Attribute("name")?.Value == "CustomHeaderBorderStyle")
            .Elements(MainNs + "tableStyleElement")
            .Single(e => e.Attribute("type")?.Value == "headerRow");

        var dxfId = int.Parse(tableStyleElement.Attribute("dxfId")!.Value);
        var dxf = resultStylesXml.Root.Element(MainNs + "dxfs")!.Elements(MainNs + "dxf").ElementAt(dxfId);

        dxf.Element(MainNs + "font")?.Element(MainNs + "b").Should().NotBeNull("bold must survive the round trip");
        dxf.Element(MainNs + "font")?.Element(MainNs + "color")?.Attribute("rgb")!.Value.Should().Be("FF0000FF");

        var bottomBorder = dxf.Element(MainNs + "border")?.Element(MainNs + "bottom");
        bottomBorder.Should().NotBeNull("the border must survive the round trip, not just fill/bold/color");
        bottomBorder!.Attribute("style")!.Value.Should().Be("medium");
        bottomBorder.Element(MainNs + "color")?.Attribute("rgb")!.Value.Should().Be("FF000000");

        dxf.Element(MainNs + "numFmt")?.Attribute("formatCode")!.Value.Should().Be("0.00%",
            "the numFmt must survive the round trip alongside the border and font");
    }

    [Fact]
    public void HeaderRowDxf_WithFillOnly_StillRoundTrips_NoRegression()
    {
        var stylesXml = XDocument.Parse(StylesXmlWithFillOnlyDxf);
        var models = XlsxStructuredTableStyleMetadataReader.Load(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        models.Should().ContainSingle();
        var headerRow = models[0].Elements.Should().ContainSingle(e => e.Type == "headerRow").Subject;
        headerRow.Format.Should().NotBeNull();
        headerRow.Format!.FillColor.Should().Be(CellColor.FromArgb(0xCC, 0xFF, 0xCC));
        headerRow.Format.BorderBottom.Should().BeNull();
        headerRow.Format.NumberFormat.Should().BeNull();

        var workbook = new Workbook("TableStyleFillOnlyRoundTrip");
        workbook.AddSheet("Data");
        workbook.StructuredTableStyles.Add(models[0]);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        XlsxStructuredTableStyleMetadataWriter.Save(stream, workbook);

        stream.Position = 0;
        using var resultArchive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var resultStylesEntry = resultArchive.GetEntry("xl/styles.xml")!;
        XDocument resultStylesXml;
        using (var s = resultStylesEntry.Open())
            resultStylesXml = XDocument.Load(s);

        var tableStyleElement = resultStylesXml.Root!
            .Element(MainNs + "tableStyles")!
            .Elements(MainNs + "tableStyle")
            .Single(e => e.Attribute("name")?.Value == "CustomFillOnlyStyle")
            .Elements(MainNs + "tableStyleElement")
            .Single(e => e.Attribute("type")?.Value == "headerRow");

        var dxfId = int.Parse(tableStyleElement.Attribute("dxfId")!.Value);
        var dxf = resultStylesXml.Root.Element(MainNs + "dxfs")!.Elements(MainNs + "dxf").ElementAt(dxfId);

        dxf.Element(MainNs + "fill")?
            .Element(MainNs + "patternFill")?
            .Element(MainNs + "fgColor")?
            .Attribute("rgb")!.Value.Should().Be("FFCCFFCC", "a fill-only dxf must still round-trip its fill color");
        dxf.Element(MainNs + "border").Should().BeNull("no border must be fabricated for a fill-only dxf");
        dxf.Element(MainNs + "numFmt").Should().BeNull("no numFmt must be fabricated for a fill-only dxf");
    }
}
