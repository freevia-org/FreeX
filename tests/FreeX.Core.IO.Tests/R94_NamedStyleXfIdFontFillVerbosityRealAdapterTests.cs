using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R94 named-style-xfId font/fill-verbosity backlog probe, through the REAL
/// <see cref="XlsxFileAdapter"/> Load/Save entry points (mirroring
/// <see cref="R93_NamedStyleXfIdRealAdapterTests"/>).
///
/// R93 fixed the alignment/protection/border axis of
/// <see cref="XlsxStylesheetMetadataPreserver.BuildXfStyleSignature"/> but deliberately left its own
/// regression fixture's font XML hand-tuned to byte-match ClosedXML's own rebuilt font shape (see
/// R93's class doc comment), so it never exercised the font-verbosity axis at all. This fixture
/// instead uses a genuinely MINIMAL/differently-shaped font representation that real Excel (and
/// third-party writers) commonly produce and ClosedXML's rebuild never reproduces verbatim:
///  - no explicit &lt;vertAlign val="baseline"/&gt; (real Excel omits it when not super/subscript;
///    ClosedXML's own rebuild always writes it explicitly)
///  - a boolean toggle written as &lt;b val="1"/&gt; instead of ClosedXML's bare &lt;b/&gt;
///  - element order that does not match ClosedXML's fixed rebuild order (vertAlign, sz, color, name,
///    family) at all -- one font reorders name/family before sz/b/color entirely
///  - a trailing &lt;scheme val="minor"/&gt; that ClosedXML's rebuild never carries through
/// A plain (non-pattern) fill and a solid fill using the schema-default-omitted
/// &lt;patternFill patternType="solid"&gt; shape (no explicit bgColor) round out the fill axis.
/// </summary>
public sealed class R94_NamedStyleXfIdFontFillVerbosityRealAdapterTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void NamedStyles_WithVerboseFontShapes_SurviveRealLoadThenSave()
    {
        using var sourcePackage = CreateFixturePackage();

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        // Touch an unrelated cell so the "model unchanged" fast path (a verbatim source-bytes copy)
        // is not taken -- this forces the real ClosedXML rebuild + preserve pass under test, exactly
        // as R93_NamedStyleXfIdRealAdapterTests does.
        var sheetToTouch = workbook.GetSheetAt(0);
        sheetToTouch.SetCell(new CellAddress(sheetToTouch.Id, 10, 10), new TextValue("touch"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml");
        var root = stylesXml.Root!;

        var cellStyles = root.Element(MainNs + "cellStyles")!.Elements(MainNs + "cellStyle").ToList();
        var goodStyle = cellStyles.SingleOrDefault(s => s.Attribute("name")?.Value == "Good");
        var customStyle = cellStyles.SingleOrDefault(s => s.Attribute("name")?.Value == "MyReportHeader");

        goodStyle.Should().NotBeNull("the built-in 'Good' named style definition must survive the rebuild");
        customStyle.Should().NotBeNull("the user-defined 'MyReportHeader' named style definition must survive the rebuild");

        var cellStyleXfs = root.Element(MainNs + "cellStyleXfs")!.Elements(MainNs + "xf").ToList();
        var cellXfs = root.Element(MainNs + "cellXfs")!.Elements(MainNs + "xf").ToList();

        var goodXfId = int.Parse(goodStyle!.Attribute("xfId")!.Value);
        var customXfId = int.Parse(customStyle!.Attribute("xfId")!.Value);
        goodXfId.Should().BeInRange(0, cellStyleXfs.Count - 1);
        customXfId.Should().BeInRange(0, cellStyleXfs.Count - 1);

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        // B2: bound to "Good" (verbose/reordered font, no vertAlign, b val="1", trailing scheme) --
        // must reconnect via xfId to the recovered "Good" cellStyleXfs record despite the font's
        // serialization differing in shape from ClosedXML's own rebuilt form.
        var goodCellXf = FindCellXfForCell(worksheetXml, cellXfs, "B2");
        goodCellXf.Attribute("xfId")?.Value.Should().Be(goodXfId.ToString(),
            "a cell styled with the plain named style 'Good' must reconnect to the recovered cellStyleXfs record even when its source font XML is more verbose/differently-ordered/differently-shaped than ClosedXML's own rebuild output");

        // D4: bound to "MyReportHeader" (font children entirely reordered: name/family before sz/b/color).
        var customCellXf = FindCellXfForCell(worksheetXml, cellXfs, "D4");
        customCellXf.Attribute("xfId")?.Value.Should().Be(customXfId.ToString(),
            "a cell styled with the user-defined named style must reconnect to its recovered cellStyleXfs record even when its source font XML reorders every child element relative to ClosedXML's own rebuild output");

        // Sibling/no-regression: a genuinely plain cell (no named style at all) must stay at xfId 0
        // (or absent), never spuriously bound to either recovered named style, exactly mirroring
        // R93_NamedStyleXfIdRealAdapterTests's E5 assertion.
        var plainCellXf = FindCellXfForCell(worksheetXml, cellXfs, "E5");
        (plainCellXf.Attribute("xfId")?.Value is null or "0").Should().BeTrue(
            "an unrelated plain cell must not be spuriously reconnected to a recovered named style");
    }

    private static XElement FindCellXfForCell(XDocument worksheetXml, List<XElement> cellXfs, string cellRef)
    {
        var cellElement = worksheetXml.Root!
            .Element(MainNs + "sheetData")!
            .Elements(MainNs + "row")
            .SelectMany(row => row.Elements(MainNs + "c"))
            .Single(c => c.Attribute("r")?.Value == cellRef);
        var styleIndex = int.Parse(cellElement.Attribute("s")?.Value ?? "0");
        return cellXfs[styleIndex];
    }

    private static MemoryStream CreateFixturePackage()
    {
        var stylesXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="0"/>
              <fonts count="3">
                <font><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font>
                <font><b val="1"/><sz val="11"/><color rgb="FF006100"/><name val="Calibri"/><family val="2"/></font>
                <font><name val="Calibri"/><family val="2"/><sz val="14"/><b/><color rgb="FF1F4E78"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFC6EFCE"/></patternFill></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="3">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0"/>
                <xf numFmtId="0" fontId="2" fillId="0" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="5">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="1" applyFont="1" applyFill="1"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="2" applyFont="1"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              </cellXfs>
              <cellStyles count="3">
                <cellStyle name="Normal" xfId="0" builtinId="0"/>
                <cellStyle name="Good" xfId="1" builtinId="26"/>
                <cellStyle name="MyReportHeader" xfId="2"/>
              </cellStyles>
              <dxfs count="0"/>
              <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
            </styleSheet>
            """;

        var worksheetXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <dimension ref="A1:E11"/>
              <sheetData>
                <row r="2"><c r="B2" s="1" t="inlineStr"><is><t>good</t></is></c></row>
                <row r="4"><c r="D4" s="3" t="inlineStr"><is><t>custom</t></is></c></row>
                <row r="5"><c r="E5" s="4" t="inlineStr"><is><t>plain</t></is></c></row>
              </sheetData>
            </worksheet>
            """;

        var contentTypesXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var rootRelsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Data" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;

        var workbookRelsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", contentTypesXml),
            ("_rels/.rels", rootRelsXml),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRelsXml),
            ("xl/styles.xml", stylesXml),
            ("xl/worksheets/sheet1.xml", worksheetXml));
    }
}
