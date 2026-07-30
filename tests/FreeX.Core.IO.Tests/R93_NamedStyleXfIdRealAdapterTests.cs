using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R93 named-style-xfId backlog probe, through the REAL <see cref="XlsxFileAdapter"/> Load/Save
/// entry points rather than calling <see cref="XlsxStylesheetMetadataPreserver.Preserve"/> directly
/// on hand-built stylesheet fragments (the existing coverage in
/// <c>XlsxStylesheetCellXfNamedStyleLinkTests</c>/<c>...AmbiguityTests</c>/<c>...ExclusivityTests</c>
/// only ever calls Preserve directly -- exactly the "test bypassed the real entry point" risk
/// class this round is meant to guard against).
///
/// The source stylesheet's font/fill/border records deliberately use the MINIMAL, schema-legal
/// representation a genuine Excel-authored (or third-party-generated) file typically uses --
/// e.g. no explicit &lt;alignment&gt;/&lt;protection&gt; on a cellXfs entry that never asked for
/// non-default alignment/protection, and a bare &lt;left/&gt;/&lt;right/&gt;/... border side with
/// no explicit style="none" -- rather than ClosedXML's own fully-expanded rebuilt form. Font
/// child element order/attributes are matched to ClosedXML's own canonical rebuild output (the
/// one piece of real Excel output this preserver does NOT attempt to normalize) so the test
/// isolates the fixed alignment/protection/border default-normalization gap without also tripping
/// over that separate, schema-default-less font-verbosity difference.
///
/// Enumerates: a built-in named style ("Good"), a genuinely user-defined named style
/// ("MyReportHeader"), a cell bound to a named style with NO overlay, and a cell bound to a named
/// style that ALSO carries direct formatting layered on top (a different fill than the style's own).
/// </summary>
public sealed class R93_NamedStyleXfIdRealAdapterTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void NamedStyles_BuiltinUserDefinedAndDirectFormattingOverlay_SurviveRealLoadThenSave()
    {
        using var sourcePackage = CreateFixturePackage();

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        // Touch an unrelated cell so XlsxFileAdapter.SaveCoreUnlocked's "model unchanged" fast path
        // (a verbatim source-bytes copy that never invokes ClosedXML at all) is NOT taken -- this
        // forces the actual full ClosedXML rebuild + XlsxStylesheetMetadataPreserver.Preserve pass
        // this test exists to exercise. Without this edit the save would just copy the source
        // bytes back out unchanged, silently passing regardless of whether the reconnect logic
        // works at all.
        var sheetToTouch = workbook.GetSheetAt(0);
        sheetToTouch.SetCell(new CellAddress(sheetToTouch.Id, 10, 10), new TextValue("touch"));

        // Act: save through the real entry point -- this is what exercises
        // XlsxStylesheetMetadataPreserver.MergeStylesheetNamedCellStyles/ReconnectCellXfNamedStyleLinks
        // for real, via XlsxFileAdapter's actual save pipeline (ClosedXML rebuild + preserve pass).
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
        goodStyle!.Attribute("builtinId")?.Value.Should().Be("26");
        customStyle.Should().NotBeNull("the user-defined 'MyReportHeader' named style definition must survive the rebuild");

        var cellStyleXfs = root.Element(MainNs + "cellStyleXfs")!.Elements(MainNs + "xf").ToList();
        var cellXfs = root.Element(MainNs + "cellXfs")!.Elements(MainNs + "xf").ToList();

        var goodXfId = int.Parse(goodStyle.Attribute("xfId")!.Value);
        var customXfId = int.Parse(customStyle!.Attribute("xfId")!.Value);
        goodXfId.Should().BeInRange(0, cellStyleXfs.Count - 1);
        customXfId.Should().BeInRange(0, cellStyleXfs.Count - 1);

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        // B2: bound to "Good" with no direct-formatting overlay -- its rebuilt cellXfs xf must link
        // back via xfId to the recovered "Good" cellStyleXfs record.
        var plainGoodCellXf = FindCellXfForCell(worksheetXml, cellXfs, "B2");
        plainGoodCellXf.Attribute("xfId")?.Value.Should().Be(goodXfId.ToString(),
            "a cell styled with the plain named style 'Good' must reconnect to the recovered cellStyleXfs record");

        // C3: bound to "Good" too, but with a DIRECT fill override on top -- must ALSO reconnect to
        // "Good" via xfId (the named-style link is independent of direct formatting layered on it),
        // while its OWN direct fill must still render as the overridden color, not "Good"'s own fill.
        var overlaidCellXf = FindCellXfForCell(worksheetXml, cellXfs, "C3");
        overlaidCellXf.Attribute("xfId")?.Value.Should().Be(goodXfId.ToString(),
            "direct formatting layered on top of a named style must not break the cell's own xfId link back to that named style");

        var fills = root.Element(MainNs + "fills")!.Elements(MainNs + "fill").ToList();
        var overlaidFillId = int.Parse(overlaidCellXf.Attribute("fillId")!.Value);
        var overlaidFillColor = fills[overlaidFillId].Element(MainNs + "patternFill")?.Element(MainNs + "fgColor")?.Attribute("rgb")?.Value;
        overlaidFillColor.Should().Be("FFFF0000", "the cell's own direct fill override must survive, not silently collapse back to the named style's fill");

        // D4: bound to the user-defined "MyReportHeader" style, no overlay.
        var customCellXf = FindCellXfForCell(worksheetXml, cellXfs, "D4");
        customCellXf.Attribute("xfId")?.Value.Should().Be(customXfId.ToString(),
            "a cell styled with the user-defined named style must reconnect to its recovered cellStyleXfs record");

        // Sibling/no-regression: a genuinely plain cell (no named style at all) must stay at xfId 0
        // (or absent), never spuriously bound to either recovered named style.
        var plainCellXf = FindCellXfForCell(worksheetXml, cellXfs, "E5");
        (plainCellXf.Attribute("xfId")?.Value is null or "0").Should().BeTrue(
            "an unrelated plain cell must not be spuriously reconnected to a recovered named style");

        // Reload the resaved package and confirm it is schema-stable on a second round trip.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        using var savedAgain = new MemoryStream();
        adapter.Save(reloaded, savedAgain);
        savedAgain.Position = 0;
        using var archive2 = new ZipArchive(savedAgain, ZipArchiveMode.Read, leaveOpen: true);
        var stylesXml2 = XlsxPackageTestFixtures.LoadPackageXml(archive2, "xl/styles.xml");
        stylesXml2.Root!.Element(MainNs + "cellStyles")!.Elements(MainNs + "cellStyle")
            .Any(s => s.Attribute("name")?.Value == "Good").Should().BeTrue(
                "the named style must still survive a SECOND load-then-save round trip");
        stylesXml2.Root!.Element(MainNs + "cellStyles")!.Elements(MainNs + "cellStyle")
            .Any(s => s.Attribute("name")?.Value == "MyReportHeader").Should().BeTrue();
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
        // Font child element order/attributes (vertAlign, sz, color, name, family) mirror
        // ClosedXML's own rebuild output exactly -- the one axis this preserver does not attempt
        // to normalize (there is no ECMA-376 schema default for sz/family the way there is for
        // <alignment>/<protection> attributes). Fills, borders, and the absence of <alignment>/
        // <protection> on every cellXfs entry are left in their realistic MINIMAL Excel-authored
        // form, which is exactly what previously broke the byte-level signature match.
        var stylesXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="0"/>
              <fonts count="3">
                <font><vertAlign val="baseline"/><sz val="11"/><color rgb="FF000000"/><name val="Calibri"/><family val="2"/></font>
                <font><b/><vertAlign val="baseline"/><sz val="11"/><color rgb="FF006100"/><name val="Calibri"/><family val="2"/></font>
                <font><b/><vertAlign val="baseline"/><sz val="14"/><color rgb="FF1F4E78"/><name val="Calibri"/><family val="2"/></font>
              </fonts>
              <fills count="5">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFC6EFCE"/></patternFill></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/></patternFill></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFFF0000"/></patternFill></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="3">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0"/>
                <xf numFmtId="0" fontId="2" fillId="3" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="5">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="1" applyFont="1" applyFill="1"/>
                <xf numFmtId="0" fontId="1" fillId="4" borderId="0" xfId="1" applyFont="1" applyFill="1"/>
                <xf numFmtId="0" fontId="2" fillId="3" borderId="0" xfId="2" applyFont="1" applyFill="1"/>
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
                <row r="3"><c r="C3" s="2" t="inlineStr"><is><t>good-overlay</t></is></c></row>
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
