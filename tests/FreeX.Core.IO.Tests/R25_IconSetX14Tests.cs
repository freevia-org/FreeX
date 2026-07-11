using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 25 regression test:
/// <list type="bullet">
///   <item>
///     R25-io-validation-cf-extlst-2 — x14-only icon-set styles (e.g. "3Stars", "3Triangles") have
///     no member in the base spreadsheetml ST_IconSetType enum. <see cref="XlsxAdvancedConditionalFormatWriter"/>
///     must not write them straight into the legacy &lt;iconSet iconSet="..."&gt; attribute (schema-invalid);
///     it must fall back to a valid base style there and carry the real style through an x14
///     extLst/x14:id link plus a matching x14:conditionalFormattings/x14:iconSet block, mirroring the
///     DataBar case in the same writer. Ordinary base-gallery icon-set styles (with or without custom
///     icon overrides) must keep writing directly into the legacy element with no x14 involvement at
///     all, exactly as before.
///   </item>
/// </list>
/// </summary>
public sealed class R25_IconSetX14Tests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

    [Fact]
    public void Save_X14OnlyIconSetStyle_FallsBackLegacyStyleAndEmitsX14Block()
    {
        var workbook = new Workbook("CfX14OnlyIconSet");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3Stars",
            IconSetShowValue = true,
            IconSetReverse = false
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;
        XNamespace x14Ns = X14Ns;

        // The legacy (base-schema) cfRule must NOT carry the x14-only style value: "3Stars" is not a
        // member of ST_IconSetType, and writing it there is schema-invalid OOXML.
        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet").Should().ContainSingle().Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().NotBe("3Stars");

        // It must instead carry an extLst/x14:id link so a real x14 reader can find the extended rule.
        var legacyCfRule = legacyIconSet.Parent!;
        legacyCfRule.Name.LocalName.Should().Be("cfRule");
        var extLst = legacyCfRule.Element(worksheetNs + "extLst");
        extLst.Should().NotBeNull("the legacy cfRule must link to the extended x14 rule via extLst/x14:id");
        var x14IdValue = extLst!.Descendants(x14Ns + "id").Should().ContainSingle().Subject.Value.Trim();

        // The worksheet-root extLst must carry a matching x14:conditionalFormattings entry with the
        // REAL style, sharing the same id.
        var x14CfRule = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Should().ContainSingle("the x14 icon-set block must be generated, not omitted")
            .Subject;
        x14CfRule.Attribute("id")!.Value.Should().Be(x14IdValue, "the legacy link and the x14 rule must share the same id");
        var x14IconSet = x14CfRule.Element(x14Ns + "iconSet");
        x14IconSet.Should().NotBeNull();
        x14IconSet!.Attribute("iconSet")!.Value.Should().Be("3Stars", "the real x14-only style must be preserved in the extension");

        // Round-tripping must recover the real style, not the legacy fallback.
        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var reloaded = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        reloaded.RuleType.Should().Be(CfRuleType.IconSet);
        reloaded.IconSetStyle.Should().Be("3Stars");
    }

    [Fact]
    public void Save_BaseGalleryIconSetStyleWithOverrides_StaysLegacyOnlyNoX14Regression()
    {
        // Sibling/already-working case: an ordinary base-gallery style (valid ST_IconSetType member)
        // with a custom icon override must keep writing straight into the legacy element, with no
        // extLst/x14 involvement at all -- the fix above must not regress this existing behavior.
        var workbook = new Workbook("CfBaseIconSetOverride");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3TrafficLights1"
        };
        format.IconOverrides.Add(new CfIconOverride("3Arrows", 2));
        sheet.ConditionalFormats.Add(format);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;

        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet").Should().ContainSingle().Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        legacyIconSet.Element(worksheetNs + "cfIcon")?.Attribute("iconSet")?.Value.Should().Be("3Arrows");

        // No x14 machinery should be involved for an ordinary base style.
        legacyIconSet.Parent!.Element(worksheetNs + "extLst").Should().BeNull(
            "an ordinary base-gallery icon-set style must not gain an x14 link");
        worksheetXml.Descendants(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Should().BeEmpty("no x14 conditionalFormattings block should be generated for a base-only style");

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var reloaded = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        reloaded.IconSetStyle.Should().Be("3TrafficLights1");
        reloaded.IconOverrides.Should().ContainSingle().Which.Should().Be(new CfIconOverride("3Arrows", 2));
    }

    [Fact]
    public void RoundTrip_RealExcelShapedX14IconSet_RegeneratesX14BlockOnResave()
    {
        // Real-Excel-shaped worksheet: a classic iconSet cfRule (legacy-reader fallback, 3 icons)
        // whose extLst carries an x14 id, plus the "real" extended x14 iconSet rule (5Boxes, 5
        // thresholds) in the worksheet-root extLst, sharing that same id -- same shape as the R18
        // regression fixture, but here we assert the RESAVE actually regenerates the x14 block
        // (previously dropped silently, per R25-io-validation-cf-extlst-2).
        const string x14Id = "{DA7ABA51-1111-2222-3333-123456789012}";
        var worksheetBody = $$"""
            <conditionalFormatting sqref="A1:A3">
              <cfRule type="iconSet" priority="1">
                <iconSet iconSet="3TrafficLights1">
                  <cfvo type="percent" val="0"/>
                  <cfvo type="percent" val="33"/>
                  <cfvo type="percent" val="67"/>
                </iconSet>
                <extLst>
                  <ext uri="{B025F937-C7B1-47D3-B67F-A62EFF666E3E}" xmlns:x14="{{X14Ns}}">
                    <x14:id>{{x14Id}}</x14:id>
                  </ext>
                </extLst>
              </cfRule>
            </conditionalFormatting>
            <extLst>
              <ext uri="{{X14CfUri}}" xmlns:x14="{{X14Ns}}">
                <x14:conditionalFormattings>
                  <x14:conditionalFormatting xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                    <x14:cfRule type="iconSet" id="{{x14Id}}" priority="1">
                      <x14:iconSet iconSet="5Boxes" showValue="1" reverse="0">
                        <x14:cfvo type="percent"><xm:f>0</xm:f></x14:cfvo>
                        <x14:cfvo type="percent"><xm:f>20</xm:f></x14:cfvo>
                        <x14:cfvo type="percent"><xm:f>40</xm:f></x14:cfvo>
                        <x14:cfvo type="percent"><xm:f>60</xm:f></x14:cfvo>
                        <x14:cfvo type="percent"><xm:f>80</xm:f></x14:cfvo>
                      </x14:iconSet>
                    </x14:cfRule>
                    <xm:sqref>A1:A3</xm:sqref>
                  </x14:conditionalFormatting>
                </x14:conditionalFormattings>
              </ext>
            </extLst>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var merged = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        merged.IconSetStyle.Should().Be("5Boxes");

        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, resaved);
        resaved.Position = 0;

        using var archive = new ZipArchive(resaved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;
        XNamespace x14Ns = X14Ns;

        var x14IconSet = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Elements(x14Ns + "iconSet")
            .Should().ContainSingle("re-saving must regenerate the x14 icon-set block, not drop it")
            .Subject;
        x14IconSet.Attribute("iconSet")!.Value.Should().Be("5Boxes");

        resaved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resaved);
        reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle()
            .Which.IconSetStyle.Should().Be("5Boxes", "the extended style must survive a second round trip now that the x14 block is regenerated");
    }

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet with three
    /// numeric cells in column A and the given extra worksheet-root XML (conditionalFormatting /
    /// extLst elements) appended after &lt;sheetData&gt;.
    /// </summary>
    private static MemoryStream BuildMinimalXlsx(string worksheetBodyXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
              {worksheetBodyXml}
            </worksheet>
            """;
        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;
        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;
        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml"  ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", packageRels),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRels),
            ("xl/worksheets/sheet1.xml", worksheetXml));
        return ms;
    }
}
