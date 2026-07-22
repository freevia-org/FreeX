using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the M-styles review group:
///  - H22: conditional-format dxf font/fill/border colors must resolve theme (with tint) and indexed
///    colors, not silently collapse to black/None.
///  - H23: XLSX cell font/fill/border colors expressed as a legacy indexed palette color must resolve
///    through <see cref="WorkbookIndexedColorPalette"/> instead of collapsing to black.
///  - H24: double-underline must round-trip as a distinct state from single underline.
///  - H61: per-cell alignment readingOrder must round-trip.
/// </summary>
public sealed class XlsxStylesThemeIndexedUnderlineReadingOrderTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---- H22: dxf (conditional format) theme + indexed color resolution ----

    [Fact]
    public void DifferentialStyleReader_ResolvesThemeFontColorWithTint()
    {
        // Elements must be in the spreadsheetml namespace (matching WorkbookNs below) for
        // XlsxDifferentialStyleReader's `dxf.Element(workbookNs + "font")` lookups to find them.
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><color theme="5" tint="0.4"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        style.FontColor.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent2, 0.4),
            "a dxf font color expressed as a theme reference must resolve against the workbook theme, not collapse to black");
    }

    [Fact]
    public void DifferentialStyleReader_ResolvesIndexedFillColor()
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><fill><patternFill><bgColor indexed="10"/></patternFill></fill></dxf>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs, WorkbookTheme.Office, indexedColors);

        // OOXML indexed="10" maps to WorkbookIndexedColorPalette's 1-based ColorIndex via
        // index-7 (see XlsxColorReader.TryReadIndexedColor), i.e. palette entry 3 (Red).
        indexedColors.TryResolveColor(3, out var expected).Should().BeTrue();
        style.FillColor.Should().Be(expected,
            "a dxf fill color expressed as a legacy indexed color must resolve through the indexed palette, not be left null");
    }

    [Fact]
    public void DifferentialStyleReader_ResolvesIndexedBorderColor_HonoringCustomOverride()
    {
        var indexedColors = new WorkbookIndexedColorPalette();
        indexedColors.SetColor(3, new CellColor(0x12, 0x34, 0x56)); // authored override for OOXML indexed="10" (palette index 10-7=3)
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><border><left style="thin"><color indexed="10"/></left></border></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs, WorkbookTheme.Office, indexedColors);

        style.BorderLeft.Style.Should().Be(BorderStyle.Thin);
        style.BorderLeft.Color.Should().Be(new CellColor(0x12, 0x34, 0x56),
            "an authored indexedColors override must be honored, not overridden by the built-in legacy palette");
    }

    [Fact]
    public void DifferentialStyleReader_WithoutThemeContext_FallsBackToRgbOnlyReading()
    {
        // The stylesheet metadata preserver compares two dxfs read without a theme/indexedColors context
        // (it only needs both sides read identically). A theme-only color must not throw and must not be
        // resolved without that context — this documents/pins that fallback behavior.
        var dxf = XElement.Parse("""<dxf><font><color theme="5" tint="0.4"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.FontColor.Should().Be(CellColor.Black);
    }

    [Fact]
    public void XlsxFileAdapter_ConditionalFormatWithThemeDxfColor_ResolvesThemeColorAfterReload()
    {
        // End-to-end: a real workbook whose CF rule's dxf uses a theme font color (as Excel's CF dialog
        // authors by default) must resolve to the theme color on load, not black.
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml),
            ("_rels/.rels", PackageRelsXml),
            ("xl/workbook.xml", WorkbookXml),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml),
            ("xl/worksheets/sheet1.xml", WorksheetWithCfXml),
            ("xl/styles.xml", StylesWithThemeDxfXml));

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);

        var sheet = workbook.GetSheetAt(0);
        sheet.Should().NotBeNull();
        var cf = sheet!.ConditionalFormats.Should().ContainSingle().Subject;
        cf.FormatIfTrue.Should().NotBeNull();
        cf.FormatIfTrue!.FontColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, 0d),
            "the CF rule's dxf font color is a theme reference and must resolve against the workbook theme");
    }

    // ---- H23: regular (non-CF) cell indexed colors ----

    [Fact]
    public void MapColor_ResolvesIndexedColor_InsteadOfCollapsingToBlack()
    {
        var xlColor = XLColor.FromIndex(10); // raw OOXML indexed="10" (0-based)
        var indexedColors = new WorkbookIndexedColorPalette();
        indexedColors.TryResolveColor(11, out var expected).Should().BeTrue();

        var color = XlsxClosedXmlCellMapper.MapColor(xlColor, WorkbookTheme.Office, indexedColors);

        color.Should().Be(expected);
        color.Should().NotBe(CellColor.Black);
    }

    [Fact]
    public void MapColor_ResolvesIndexedColor_HonoringAuthoredOverride()
    {
        var xlColor = XLColor.FromIndex(10);
        var indexedColors = new WorkbookIndexedColorPalette();
        indexedColors.SetColor(11, new CellColor(0xAA, 0xBB, 0xCC));

        var color = XlsxClosedXmlCellMapper.MapColor(xlColor, WorkbookTheme.Office, indexedColors);

        color.Should().Be(new CellColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void MapColor_WithoutIndexedPalette_FallsBackToDefaultLegacyPalette()
    {
        // The 2-arg overload (no explicit palette in scope) must still resolve indexed colors through
        // the built-in legacy palette rather than collapsing to black.
        var xlColor = XLColor.FromIndex(10);

        var color = XlsxClosedXmlCellMapper.MapColor(xlColor, WorkbookTheme.Office);

        color.Should().NotBe(CellColor.Black);
    }

    [Fact]
    public void XlsxFileAdapter_CellWithIndexedFontColor_RoundTripsWithoutCollapsingToBlack()
    {
        using var buildStream = new MemoryStream();
        using (var xlWorkbook = new XLWorkbook())
        {
            var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
            // ClosedXML cell addresses are 1-based, same as FreeX's CellAddress/Sheet.GetCell convention
            // (see XlsxFileAdapter, which copies xlCell.Address.RowNumber/ColumnNumber verbatim) — so A1
            // here must be read back via GetCell(1, 1) below, not a different cell.
            var cell = xlSheet.Cell(1, 1);
            cell.Value = "hi";
            cell.Style.Font.FontColor = XLColor.FromIndex(10);
            xlWorkbook.SaveAs(buildStream);
        }
        buildStream.Position = 0;

        var adapter = new XlsxFileAdapter();
        var reloaded = adapter.Load(buildStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Should().NotBeNull();
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        reloadedCell.Should().NotBeNull();
        var styleId = reloadedCell!.StyleId;
        styleId.Should().NotBe(StyleId.Default, "an indexed font color must register a non-default style");
        var style = reloaded.GetStyle(styleId);
        style.FontColor.Should().NotBe(CellColor.Black,
            "an indexed font color must resolve through the legacy palette, not collapse to black");
    }

    // ---- H24: double underline ----

    [Theory]
    [InlineData(XLFontUnderlineValues.Double)]
    [InlineData(XLFontUnderlineValues.DoubleAccounting)]
    public void MapStyle_ReadsDoubleUnderlineAsDistinctFromSingleUnderline(XLFontUnderlineValues underlineValue)
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.Underline = underlineValue;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.DoubleUnderline.Should().BeTrue($"{underlineValue} must be modeled as double underline");
    }

    [Fact]
    public void MapStyle_ReadsSingleUnderline_WithoutSettingDoubleUnderline()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.Underline.Should().BeTrue();
        style.DoubleUnderline.Should().BeFalse();
    }

    [Fact]
    public void ApplyStyle_WritesDoubleUnderline_AsDistinctXlsxValue()
    {
        var style = new CellStyle { DoubleUnderline = true };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Font.Underline.Should().Be(XLFontUnderlineValues.Double,
            "DoubleUnderline must write a distinct OOXML underline value, not plain Single");
    }

    [Fact]
    public void XlsxFileAdapter_DoubleUnderlineCell_RoundTripsAsDoubleUnderline()
    {
        var workbook = new Workbook("DoubleUnderlineRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("Total")));
        var styleId = workbook.RegisterStyle(new CellStyle { DoubleUnderline = true });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.DoubleUnderline.Should().BeTrue(
            "double underline must survive a full save/reload round trip through XLSX");
    }

    // ---- H61: per-cell readingOrder ----

    [Theory]
    [InlineData(XLAlignmentReadingOrderValues.LeftToRight, CellReadingOrder.LeftToRight)]
    [InlineData(XLAlignmentReadingOrderValues.RightToLeft, CellReadingOrder.RightToLeft)]
    [InlineData(XLAlignmentReadingOrderValues.ContextDependent, CellReadingOrder.Context)]
    public void MapStyle_ReadsPerCellReadingOrder(XLAlignmentReadingOrderValues xlValue, CellReadingOrder expected)
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Alignment.ReadingOrder = xlValue;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.ReadingOrder.Should().Be(expected);
    }

    [Fact]
    public void ApplyStyle_WritesRightToLeftReadingOrder()
    {
        var style = new CellStyle { ReadingOrder = CellReadingOrder.RightToLeft };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Alignment.ReadingOrder.Should().Be(XLAlignmentReadingOrderValues.RightToLeft);
    }

    [Fact]
    public void XlsxFileAdapter_RightToLeftReadingOrderCell_RoundTrips()
    {
        var workbook = new Workbook("ReadingOrderRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("שלום")));
        var styleId = workbook.RegisterStyle(new CellStyle { ReadingOrder = CellReadingOrder.RightToLeft });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.ReadingOrder.Should().Be(CellReadingOrder.RightToLeft,
            "a per-cell RTL readingOrder override must survive a full save/reload round trip through XLSX");
    }

    // ---- Fixtures for the end-to-end dxf theme-color test ----

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string PackageRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private const string WorkbookRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    // Uses a "top10" rule (rather than cellIs/expression) because those two classic rule types are read
    // through ClosedXML's own object model (XlsxConditionalFormatClosedXmlMapper), not through
    // XlsxDifferentialStyleReader's dxfId lookup — top10 is one of the "long-tail" rule types that DOES
    // go through ReadAdvancedConditionalFormats -> differentialStyles[dxfId], the exact path H22 covers.
    private const string WorksheetWithCfXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1"><v>10</v></c>
            </row>
          </sheetData>
          <conditionalFormatting sqref="A1">
            <cfRule type="top10" dxfId="0" priority="1" rank="10"/>
          </conditionalFormatting>
        </worksheet>
        """;

    private const string StylesWithThemeDxfXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
          </fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
          <dxfs count="1">
            <dxf>
              <font><color theme="5" tint="0"/></font>
            </dxf>
          </dxfs>
        </styleSheet>
        """;
}
