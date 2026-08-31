using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// freex-table-style-theme-color-1.
///
/// A custom tableStyle's per-element dxf can reference a THEME colour
/// (<c>&lt;color theme="4" tint="0.4"/&gt;</c>) exactly like a conditional-formatting dxf can. The CF
/// path captures that link on read (<see cref="XlsxDifferentialStyleReader"/>, R120-cf-theme-color-1)
/// and re-emits it on write, but the table-style path did neither: the reader used the plain
/// <c>TryReadCellColor</c> overload, which returns only the RGB the link resolves to, and the writer
/// emitted <c>rgb</c> unconditionally.
///
/// The user-visible effect was that a themed Excel table style was permanently baked to RGB on the
/// first open→save, after which its header/banding stopped following Theme Colors while the ordinary
/// cells around it still did.
/// </summary>
public sealed class TableStyleThemeColorRoundTripTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Accent1 is theme index 4 in the OOXML ordering XlsxColorReader.ThemeColorIndex uses.</summary>
    private const string ThemedStylesXml =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<dxfs count=\"1\">" +
        "<dxf>" +
        "<font><color theme=\"4\"/></font>" +
        "<fill><patternFill><fgColor theme=\"4\"/></patternFill></fill>" +
        "<border><bottom style=\"medium\"><color theme=\"4\"/></bottom></border>" +
        "</dxf>" +
        "</dxfs>" +
        "<tableStyles count=\"1\">" +
        "<tableStyle name=\"ThemedHeaderStyle\" pivot=\"0\" table=\"1\" count=\"1\">" +
        "<tableStyleElement type=\"headerRow\" dxfId=\"0\"/>" +
        "</tableStyle>" +
        "</tableStyles>" +
        "</styleSheet>";

    private const string LiteralRgbStylesXml =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<dxfs count=\"1\">" +
        "<dxf>" +
        "<font><color rgb=\"FF0000FF\"/></font>" +
        "<border><bottom style=\"medium\"><color rgb=\"FF112233\"/></bottom></border>" +
        "</dxf>" +
        "</dxfs>" +
        "<tableStyles count=\"1\">" +
        "<tableStyle name=\"LiteralRgbStyle\" pivot=\"0\" table=\"1\" count=\"1\">" +
        "<tableStyleElement type=\"headerRow\" dxfId=\"0\"/>" +
        "</tableStyle>" +
        "</tableStyles>" +
        "</styleSheet>";

    private static StyleDiff ReadHeaderRowFormat(string stylesXml, WorkbookTheme theme)
    {
        var models = XlsxStructuredTableStyleMetadataReader.Load(
            XDocument.Parse(stylesXml), theme, new WorkbookIndexedColorPalette());

        models.Should().ContainSingle();
        var headerRow = models[0].Elements.Should().ContainSingle(e => e.Type == "headerRow").Subject;
        headerRow.Format.Should().NotBeNull();
        return headerRow.Format!;
    }

    private static XElement SaveAndReadBackHeaderRowDxf(string stylesXml, string styleName, WorkbookTheme theme)
    {
        var models = XlsxStructuredTableStyleMetadataReader.Load(
            XDocument.Parse(stylesXml), theme, new WorkbookIndexedColorPalette());

        var workbook = new Workbook("TableStyleThemeColorRoundTrip") { Theme = theme };
        workbook.AddSheet("Data");
        workbook.StructuredTableStyles.Add(models[0]);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        XlsxStructuredTableStyleMetadataWriter.Save(stream, workbook);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XDocument resultXml;
        using (var entry = archive.GetEntry("xl/styles.xml")!.Open())
            resultXml = XDocument.Load(entry);

        var element = resultXml.Root!
            .Element(MainNs + "tableStyles")!
            .Elements(MainNs + "tableStyle")
            .Single(e => e.Attribute("name")?.Value == styleName)
            .Elements(MainNs + "tableStyleElement")
            .Single(e => e.Attribute("type")?.Value == "headerRow");

        var dxfId = int.Parse(element.Attribute("dxfId")!.Value);
        return resultXml.Root.Element(MainNs + "dxfs")!.Elements(MainNs + "dxf").ElementAt(dxfId);
    }

    [Fact]
    public void Reader_CapturesTheThemeLinkRatherThanOnlyTheResolvedRgb()
    {
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));

        var format = ReadHeaderRowFormat(ThemedStylesXml, theme);

        format.FontThemeColor.Should().NotBeNull("a <color theme=\"4\"/> font must keep its link");
        format.FontThemeColor!.Value.Slot.Should().Be(WorkbookThemeColorSlot.Accent1);
        format.FillThemeColor.Should().NotBeNull("a <color theme=\"4\"/> fill must keep its link");
        format.FillThemeColor!.Value.Slot.Should().Be(WorkbookThemeColorSlot.Accent1);
        format.BorderBottom.Should().NotBeNull();
        format.BorderBottom!.Value.ThemeColor.Should().NotBeNull("a themed edge must keep its link");
        format.BorderBottom.Value.ThemeColor!.Value.Slot.Should().Be(WorkbookThemeColorSlot.Accent1);
    }

    [Fact]
    public void ReadThemedStyle_FollowsALaterThemeSwap()
    {
        // The point of keeping the link: the captured style must re-resolve under a NEW theme.
        var themeA = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));
        var themeB = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(20, 200, 10));

        var format = ReadHeaderRowFormat(ThemedStylesXml, themeA);

        format.FontThemeColor!.Value.Resolve(themeA).Should().Be(new CellColor(200, 10, 20));
        format.FontThemeColor.Value.Resolve(themeB).Should().Be(new CellColor(20, 200, 10));
        format.BorderBottom!.Value.ResolveColor(themeB).Should().Be(new CellColor(20, 200, 10));
    }

    [Fact]
    public void Writer_ReEmitsTheThemeLinkInsteadOfBakingItToRgb()
    {
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));

        var dxf = SaveAndReadBackHeaderRowDxf(ThemedStylesXml, "ThemedHeaderStyle", theme);

        var fontColor = dxf.Element(MainNs + "font")?.Element(MainNs + "color");
        fontColor.Should().NotBeNull();
        fontColor!.Attribute("theme")?.Value.Should().Be("4");
        fontColor.Attribute("rgb").Should().BeNull("a themed colour must not be flattened to rgb");

        var bottomColor = dxf.Element(MainNs + "border")?.Element(MainNs + "bottom")?.Element(MainNs + "color");
        bottomColor.Should().NotBeNull();
        bottomColor!.Attribute("theme")?.Value.Should().Be("4");
        bottomColor.Attribute("rgb").Should().BeNull();

        var fgColor = dxf.Element(MainNs + "fill")?.Element(MainNs + "patternFill")?.Element(MainNs + "fgColor");
        fgColor.Should().NotBeNull();
        fgColor!.Attribute("theme")?.Value.Should().Be("4");
        fgColor.Attribute("rgb").Should().BeNull();
    }

    [Fact]
    public void LiteralRgbStyle_StillRoundTripsAsRgbWithNoThemeLink()
    {
        // No-regression: an explicitly-coloured table style must be untouched by the theme handling.
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));

        var format = ReadHeaderRowFormat(LiteralRgbStylesXml, theme);
        format.FontThemeColor.Should().BeNull();
        format.BorderBottom!.Value.ThemeColor.Should().BeNull();
        format.BorderBottom.Value.Color.Should().Be(CellColor.FromArgb(0x11, 0x22, 0x33));

        var dxf = SaveAndReadBackHeaderRowDxf(LiteralRgbStylesXml, "LiteralRgbStyle", theme);
        dxf.Element(MainNs + "font")?.Element(MainNs + "color")?.Attribute("rgb")?.Value
            .Should().Be("FF0000FF");
        dxf.Element(MainNs + "border")?.Element(MainNs + "bottom")?.Element(MainNs + "color")?
            .Attribute("rgb")?.Value.Should().Be("FF112233");
    }
}
