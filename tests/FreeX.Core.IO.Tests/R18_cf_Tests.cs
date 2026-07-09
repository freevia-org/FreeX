using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 18 regression tests:
/// <list type="bullet">
///   <item>
///     R18-cf-dxf-extlst-io-1 — an x14 extended icon-set rule (classic iconSet cfRule fallback +
///     x14 extLst rule sharing the same extLst id) must load as ONE ConditionalFormat (the merged
///     x14 style), not two duplicate rules, and must re-save without duplicating.
///   </item>
///   <item>
///     R18-cf-dxf-extlst-io-2 — a classic cellIs conditional format with Strikethrough must
///     round-trip the strikethrough flag through save/load (previously dropped by
///     <c>XlsxConditionalFormatClosedXmlMapper.ApplyStyle</c>).
///   </item>
/// </list>
/// </summary>
public sealed class R18_cf_Tests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

    // ── R18-cf-dxf-extlst-io-1 ──────────────────────────────────────────────

    [Fact]
    public void Load_X14IconSetWithClassicFallback_MergesIntoSingleConditionalFormat()
    {
        // A real-Excel-shaped worksheet: a classic iconSet cfRule (legacy-reader fallback, 3 icons)
        // whose extLst carries an x14 id, plus the "real" extended x14 iconSet rule (5Boxes, 5
        // thresholds) in the worksheet-root extLst, sharing that same id.
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
                  <x14:conditionalFormatting xmlns:xm="{{XmNs}}">
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
        var sheet = workbook.GetSheetAt(0);

        // Must load as ONE format (the x14/extended style), not two duplicate icon-set rules.
        sheet.ConditionalFormats.Should().ContainSingle(
            "the classic fallback rule and the x14 extended rule share the same extLst id and " +
            "describe the SAME conditional format, so they must be merged, not duplicated");

        var merged = sheet.ConditionalFormats[0];
        merged.RuleType.Should().Be(CfRuleType.IconSet);
        merged.IconSetStyle.Should().Be("5Boxes", "the x14 extended icon style must win over the classic fallback's 3TrafficLights1");
        merged.IconSetThresholds.Should().HaveCount(5, "5Boxes has 5 thresholds, not the classic fallback's 3");

        // Re-saving must not duplicate the rule either.
        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, resaved);
        resaved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resaved);
        reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle(
            "re-saving and reloading must not reintroduce a duplicate icon-set rule");
    }

    // ── R18-cf-dxf-extlst-io-2 ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ClassicCellIsConditionalFormat_PreservesStrikethrough()
    {
        var workbook = new Workbook("CfStrikethroughRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = new CellStyle { Strikethrough = true }
        });

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var loaded = new XlsxFileAdapter().Load(ms);

        loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle();
        var rule = loaded.GetSheetAt(0).ConditionalFormats[0];
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.Strikethrough.Should().BeTrue(
            "a classic cellIs conditional format's Strikethrough dxf property must survive save/load, " +
            "just like Bold/Italic already do");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml",        contentTypes);
            WriteEntry(archive, "_rels/.rels",                packageRels);
            WriteEntry(archive, "xl/workbook.xml",            workbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            WriteEntry(archive, "xl/worksheets/sheet1.xml",   worksheetXml);
        }

        ms.Position = 0;
        return ms;

        static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
