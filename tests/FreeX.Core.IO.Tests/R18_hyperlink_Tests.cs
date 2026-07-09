using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 18 regression tests:
///  - R18-hyperlink-io-deep-1: the fast PATCH-save path must re-quote an internal hyperlink's
///    sheet-name portion (mirroring what ClosedXML does on the full-save path) instead of
///    writing the model's always-unquoted internal address verbatim into the "location"
///    attribute.
///  - R18-hyperlink-io-deep-3: a bounded (non whole-column/row) but huge multi-cell range
///    hyperlink ref (e.g. A1:Z100000) must be recognized and stripped before ClosedXML
///    materializes it cell-by-cell, the same way whole-column/row refs already are.
/// </summary>
public sealed class R18_hyperlink_Tests
{
    // --- R18-hyperlink-io-deep-1 -------------------------------------------------------------

    [Theory]
    [InlineData("My Sheet!A10", "'My Sheet'!A10")]
    [InlineData("Bob's Sheet!A1", "'Bob''s Sheet'!A1")]
    [InlineData("Sheet1!A1", "Sheet1!A1")]
    [InlineData(null, null)]
    public void QuoteInternalHyperlinkAddress_QuotesSheetNamePortionWhenNeeded(string? input, string? expected)
    {
        XlsxFileAdapter.QuoteInternalHyperlinkAddress(input).Should().Be(expected);
    }

    [Fact]
    public void QuoteInternalHyperlinkAddress_DoesNotDoubleQuoteAnAlreadyQuotedAddress()
    {
        // Defensive guard mirroring NormalizeInternalHyperlinkAddress's own detection: if an
        // address somehow already carries surrounding quotes, don't re-wrap it.
        XlsxFileAdapter.QuoteInternalHyperlinkAddress("'My Sheet'!A10").Should().Be("'My Sheet'!A10");
    }

    [Fact]
    public void Save_LoadedWorkbookWithInternalHyperlinkToSpacedSheetName_PatchSavesQuotedLocation()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage(
            sheetName: "My Sheet",
            locationAttr: "'My Sheet'!A10",
            tooltip: "Jump original");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        // The load-time normalizer must have already unescaped/unquoted the sheet name for the
        // model (this is the invariant NormalizeInternalHyperlinkAddress is responsible for).
        sheet.Hyperlinks[address].Should().Be("My Sheet!A10");

        // Trigger a patch-eligible change without altering the internal address itself, so the
        // regression is isolated to "re-quote on write" rather than "changed value happens to be
        // quoted already".
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "My Sheet!A10");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("'My Sheet'!A10", "an unquoted sheet reference to a name that needs quoting is invalid in Excel");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("My Sheet!A10");
    }

    [Fact]
    public void Save_LoadedWorkbookWithInternalHyperlinkToApostropheSheetName_PatchSavesDoubledApostrophe()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage(
            sheetName: "Bob's Sheet",
            locationAttr: "'Bob''s Sheet'!A1",
            tooltip: "Jump original");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address].Should().Be("Bob's Sheet!A1");

        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "Bob's Sheet!A1");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("'Bob''s Sheet'!A1", "an embedded apostrophe in a quoted sheet name must be doubled");
    }

    private static byte[] CreateInternalHyperlinkSourcePackage(string sheetName, string locationAttr, string tooltip)
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="{sheetName}" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                    <row r="2"><c r="B2"><v>1</v></c></row>
                    <row r="3"><c r="C3"><v>2</v></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="A1" location="{locationAttr}" tooltip="{tooltip}" display="Jump display"/>
                  </hyperlinks>
                </worksheet>
                """));

        return package.ToArray();
    }

    private static string? ReadHyperlinkAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var hyperlinks = document.Root.Element(ns + "hyperlinks");
        return hyperlinks
            ?.Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    // --- R18-hyperlink-io-deep-3 -------------------------------------------------------------

    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ContainsRangeHyperlinkRef_DetectsOversizedBoundedRange()
    {
        var root = BuildWorksheetRootWithHyperlinkRef("A1:Z100000");

        XlsxWorksheetHyperlinkNormalizer.ContainsRangeHyperlinkRef(root).Should().BeTrue(
            "a bounded range this large (2.6M cells) would otherwise be materialized cell-by-cell by ClosedXML");
    }

    [Fact]
    public void StripRangeHyperlinkRefs_RemovesOversizedBoundedRangeHyperlink()
    {
        var root = BuildWorksheetRootWithHyperlinkRef("A1:Z100000");

        var changed = XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs(root);

        changed.Should().BeTrue();
        root.Element(WorksheetNs + "hyperlinks").Should().BeNull();
    }

    [Fact]
    public void ContainsRangeHyperlinkRef_DoesNotFlagAnOrdinarySmallBoundedRange()
    {
        // Regression guard: a normal small multi-cell range hyperlink (well under the cap) must
        // continue to be preserved, not swept up by the new oversized-range detection.
        var root = BuildWorksheetRootWithHyperlinkRef("A1:B2");

        XlsxWorksheetHyperlinkNormalizer.ContainsRangeHyperlinkRef(root).Should().BeFalse();
    }

    [Fact]
    public void ContainsRangeHyperlinkRef_StillDetectsWholeColumnRef()
    {
        // Regression guard for the pre-existing whole-column/row detection this fix sits beside.
        var root = BuildWorksheetRootWithHyperlinkRef("A:A");

        XlsxWorksheetHyperlinkNormalizer.ContainsRangeHyperlinkRef(root).Should().BeTrue();
    }

    private static XElement BuildWorksheetRootWithHyperlinkRef(string reference) =>
        new(
            WorksheetNs + "worksheet",
            new XElement(
                WorksheetNs + "hyperlinks",
                new XElement(
                    WorksheetNs + "hyperlink",
                    new XAttribute("ref", reference),
                    new XAttribute("location", "Sheet1!A1"))));
}
