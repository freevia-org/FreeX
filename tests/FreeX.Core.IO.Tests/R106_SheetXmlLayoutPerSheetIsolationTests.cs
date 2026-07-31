using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 106 regression test:
/// R106-io-sheet-xml-layout-isolation-1 -- <c>XlsxFileAdapter.LoadSheetXmlLayout</c> wrapped its
/// ENTIRE per-sheet foreach loop in one outer try/catch, so a failure reading any single sheet's
/// hidden-layout metadata aborted the loop and left every subsequent sheet (in workbook document
/// order) with NO <c>sheetXmlLayout</c> entry at all -- not just the sheet that actually failed.
/// Downstream, <c>XlsxFileAdapter.cs</c> gates both <see cref="XlsxX14DataValidationReader"/>.Apply
/// (merges the real x14 List source formula for cross-sheet/over-length List sources) and
/// <see cref="XlsxDataValidationNativeMetadataMapper"/>.Apply (collapses ClosedXML's own
/// multi-area-rule split back into one rule with AdditionalRanges) behind
/// <c>xmlLayout is not null</c> -- so both silently stop running for every sheet after the first
/// one whose layout metadata failed to parse.
///
/// <para>
/// <b>Seam note:</b> the primary isolation assertion below calls the private
/// <c>XlsxFileAdapter.LoadSheetXmlLayout</c> method directly via reflection instead of the full
/// <see cref="XlsxFileAdapter.Load(Stream)"/> entry point. This is deliberate, not a shortcut: any
/// XML malformed enough to make one of <c>ReadHiddenSheetLayout</c>'s own sub-readers throw is ALSO
/// malformed enough that the underlying OpenXml SDK package layer ClosedXML sits on throws first,
/// during <c>new XLWorkbook(stream)</c> -- confirmed empirically (see history: corrupting the
/// worksheet's own XML tripped an unrelated unguarded exception in
/// <c>XlsxClosedXmlStyleOnlyCellStripper</c>; corrupting a sheet's own, otherwise-unreferenced
/// <c>.rels</c> part still made <c>DocumentFormat.OpenXml.Packaging.PartRelationshipsFeature</c>
/// throw while eagerly walking EVERY part's relationships package-wide, before FreeX's own
/// per-sheet reader ever runs). <c>LoadSheetXmlLayout</c> is the exact production method containing
/// the fix, invoked here against a real ZipArchive-backed package built the same way the real file
/// format is, through the same <c>ReadHiddenSheetLayout</c> code path -- it just does not
/// additionally require a third-party library's independent resilience to malformed input. The
/// sibling test below then proves, through the full real <see cref="XlsxFileAdapter.LoadWithWarnings"/>
/// entry point (with nothing corrupted), that the x14-merge and multi-area-dedup consumers are
/// correctly wired to whatever <c>LoadSheetXmlLayout</c> produces.
/// </para>
/// </summary>
public sealed class R106_SheetXmlLayoutPerSheetIsolationTests
{
    private static readonly MethodInfo LoadSheetXmlLayoutMethod = typeof(XlsxFileAdapter).GetMethod(
        "LoadSheetXmlLayout",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void LoadSheetXmlLayoutMethod_IsFound()
    {
        // Guards the reflection lookup above: if this ever fails, the other tests in this class
        // would throw a confusing NullReferenceException instead of a clear signal that the method
        // was renamed/removed.
        LoadSheetXmlLayoutMethod.Should().NotBeNull();
    }

    /// <summary>
    /// Sheet1's own relationship part (<c>xl/worksheets/_rels/sheet1.xml.rels</c>) is deliberately
    /// non-well-formed XML. <see cref="XlsxWorksheetCustomPropertyMapper"/>.Read (called
    /// unconditionally from <c>ReadHiddenSheetLayout</c> for every sheet, regardless of whether that
    /// sheet has any custom properties) loads that rels part via
    /// <see cref="XlsxRelationshipReader"/>.LoadTargets, which throws while parsing it -- exactly
    /// the "one sheet's XML-layout exception" from the finding.
    ///
    /// Sheet2 (later in document order) carries a List validation whose real source lives only in
    /// the x14 extLst block (a cross-sheet source, the classic "long/cross-sheet List source" case);
    /// Sheet3 carries a genuine multi-area rule (two space-separated ranges in one sqref). Before
    /// the fix, Sheet1's exception aborts the whole per-sheet loop and NEITHER Sheet2 nor Sheet3 get
    /// a <c>sheetXmlLayout</c> entry. After the fix, only Sheet1 is skipped.
    /// </summary>
    [Fact]
    public void LoadSheetXmlLayout_OneSheetsRelsPartFailsToParse_OnlyThatSheetIsMissing_OthersStillLoad()
    {
        using var stream = BuildThreeSheetPackage(corruptFirstSheetRels: true);
        var warnings = new List<string>();

        var parameters = new object?[]
        {
            stream,
            null, // stylesXml
            WorkbookTheme.Office,
            new WorkbookIndexedColorPalette(),
            false, // loadStructuredTableMetadata
            null, // out structuredTableMetadata
            warnings,
        };

        var resultObj = LoadSheetXmlLayoutMethod.Invoke(null, parameters);
        resultObj.Should().NotBeNull();
        var result = (System.Collections.IDictionary)resultObj!;

        warnings.Should().ContainSingle(
            w => w.Contains("[worksheet-xml-metadata]") && w.Contains("Sheet1"),
            "Sheet1's own parse failure must be reported by name, not swallowed into one generic message");

        result.Contains("Sheet1").Should().BeFalse(
            "Sheet1's own layout metadata failed to parse, so it must have no entry");
        result.Contains("Sheet2").Should().BeTrue(
            "Sheet2's layout metadata must still be loaded even though Sheet1 (earlier in document " +
            "order) failed to parse -- before the fix, Sheet1's exception aborted the WHOLE " +
            "dictionary build and left every later sheet with no entry at all");
        result.Contains("Sheet3").Should().BeTrue(
            "Sheet3's layout metadata must still be loaded even though Sheet1 failed to parse");
    }

    /// <summary>
    /// No-regression counterpart to the isolation test: with nothing corrupted, ALL three sheets
    /// must still get a <c>sheetXmlLayout</c> entry and there must be no warnings -- proving the
    /// per-sheet try/catch refactor did not disturb the ordinary success path.
    /// </summary>
    [Fact]
    public void LoadSheetXmlLayout_NoSheetFailsToParse_AllSheetsGetEntriesAndNoWarnings()
    {
        using var stream = BuildThreeSheetPackage(corruptFirstSheetRels: false);
        var warnings = new List<string>();

        var parameters = new object?[]
        {
            stream,
            null,
            WorkbookTheme.Office,
            new WorkbookIndexedColorPalette(),
            false,
            null,
            warnings,
        };

        var resultObj = LoadSheetXmlLayoutMethod.Invoke(null, parameters);
        var result = (System.Collections.IDictionary)resultObj!;

        warnings.Should().BeEmpty();
        result.Contains("Sheet1").Should().BeTrue();
        result.Contains("Sheet2").Should().BeTrue();
        result.Contains("Sheet3").Should().BeTrue();
    }

    /// <summary>
    /// Sibling/no-regression case through the FULL real <see cref="XlsxFileAdapter.LoadWithWarnings"/>
    /// entry point (nothing corrupted): proves the x14-merge and multi-area-dedup consumers
    /// (<see cref="XlsxX14DataValidationReader"/>.Apply and
    /// <see cref="XlsxDataValidationNativeMetadataMapper"/>.Apply) are correctly wired to whatever
    /// <c>LoadSheetXmlLayout</c> produces for Sheet2/Sheet3, end to end.
    /// </summary>
    [Fact]
    public void Load_NoSheetLayoutParseFailure_StillMergesX14AndDedupsMultiArea()
    {
        using var stream = BuildThreeSheetPackage(corruptFirstSheetRels: false);

        var result = new XlsxFileAdapter().LoadWithWarnings(stream);
        var workbook = result.Workbook;

        result.Warnings.Should().BeEmpty("no sheet's layout metadata should fail to parse here");

        var sheet2 = workbook.GetSheetAt(1)!;
        sheet2.DataValidations.Should().ContainSingle();
        sheet2.DataValidations[0].Type.Should().Be(DvType.List);
        sheet2.DataValidations[0].Formula1.Should().Be(
            "=Sheet1!$A$1:$A$5",
            "the real cross-sheet List source must be merged in from the x14 extLst block");

        var sheet3 = workbook.GetSheetAt(2)!;
        sheet3.DataValidations.Should().ContainSingle(
            "the two space-separated areas of Sheet3's rule must collapse into ONE rule");
        sheet3.DataValidations[0].Type.Should().Be(DvType.WholeNumber);
        sheet3.DataValidations[0].AdditionalRanges.Should().HaveCount(
            1,
            "the second area (C2:C5) must be folded into AdditionalRanges instead of staying a " +
            "separate duplicate DataValidation entry");
    }

    private static MemoryStream BuildThreeSheetPackage(bool corruptFirstSheetRels)
    {
        const string worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // Sheet1: plain, well-formed, feature-free worksheet XML (no drawings/hyperlinks/tables/
        // pictures). Only FreeX's own XlsxWorksheetCustomPropertyMapper.Read (called unconditionally
        // per sheet) ever opens its .rels part.
        var sheet1Xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{worksheetNs}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """;

        // Sheet2: no legacy <dataValidation> at all -- only the x14 extLst block, so the ONLY way
        // Sheet2 ends up with a DataValidation rule is via XlsxX14DataValidationReader.Apply.
        var sheet2Xml = $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{{worksheetNs}}">
              <sheetData/>
              <extLst>
                <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1">
                      <x14:formula1><xm:f>Sheet1!$A$1:$A$5</xm:f></x14:formula1>
                      <xm:sqref>B2</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """;

        // Sheet3: one Excel rule spanning two areas (A2:A5, C2:C5) via a space-separated sqref --
        // ClosedXML's own load surfaces this as two separate single-range DataValidation objects.
        var sheet3Xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{worksheetNs}">
              <sheetData>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
              <dataValidations count="1">
                <dataValidation type="whole" operator="between" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A2:A5 C2:C5">
                  <formula1>1</formula1>
                  <formula2>100</formula2>
                </dataValidation>
              </dataValidations>
            </worksheet>
            """;

        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                <sheet name="Sheet2" sheetId="2" r:id="rId2"/>
                <sheet name="Sheet3" sheetId="3" r:id="rId3"/>
              </sheets>
            </workbook>
            """;
        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet2.xml"/>
              <Relationship Id="rId3"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet3.xml"/>
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
              <Override PartName="/xl/worksheets/sheet2.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet3.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var entries = new List<(string, string)>
        {
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", packageRels),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRels),
            ("xl/worksheets/sheet1.xml", sheet1Xml),
            ("xl/worksheets/sheet2.xml", sheet2Xml),
            ("xl/worksheets/sheet3.xml", sheet3Xml),
        };

        if (corruptFirstSheetRels)
        {
            // Deliberately non-well-formed: a mismatched end tag.
            entries.Add((
                "xl/worksheets/_rels/sheet1.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><thisTagIsNeverClosed></Relationships>"""));
        }

        return XlsxPackageTestFixtures.CreatePackage(entries.ToArray());
    }
}
