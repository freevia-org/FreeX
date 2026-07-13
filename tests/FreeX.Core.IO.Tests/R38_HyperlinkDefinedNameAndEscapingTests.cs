using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 38 regression tests for src/FreeX.Core.IO/XlsxFileAdapter.Hyperlinks.cs:
///  - R38-io-hyperlink-2-1: an internal hyperlink whose target is a workbook-scoped DEFINED NAME
///    (stored bang-less, e.g. location="MyDefinedName") must not be silently rewritten into a
///    sheet-qualified reference (e.g. "Sheet1!MyDefinedName") on load/save -- that changes what
///    the hyperlink actually points at.
///  - R38-io-hyperlink-2-3: an external hyperlink target containing a space (or other
///    percent-encodable character) must be percent-encoded when written to the relationship's
///    Target, not written raw (which produces an invalid Target per the URI rules Excel itself
///    follows).
/// </summary>
public sealed class R38_HyperlinkDefinedNameAndEscapingTests
{
    // --- R38-io-hyperlink-2-1 -------------------------------------------------------------

    [Fact]
    public void Load_InternalHyperlinkToDefinedName_PreservesBareNameNotSheetQualifiedRef()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage(
            locationAttr: "MyDefinedName",
            tooltip: "Jump to name");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        // Pre-fix, ClosedXML's XLHyperlink.InternalAddress getter unconditionally prepends the
        // current worksheet name to a bang-less internal address, so this would incorrectly read
        // back as "Sheet1!MyDefinedName" -- a fabricated cell-style reference instead of the
        // workbook-scoped defined name the hyperlink actually targets.
        sheet.Hyperlinks[address].Should().Be("MyDefinedName");
        sheet.HyperlinkMetadata[address].Bookmark.Should().Be("MyDefinedName");
        sheet.HyperlinkMetadata[address].LinkType.Should().Be(HyperlinkTargetKind.PlaceInThisDocument);
    }

    [Fact]
    public void Save_LoadedWorkbookWithInternalHyperlinkToDefinedName_PatchSavesBareNameLocation()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage(
            locationAttr: "MyDefinedName",
            tooltip: "Jump to name");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        // Trigger a patch-eligible change (tooltip only) without altering the target itself, so
        // the regression is isolated to "the defined-name target survives a save" rather than
        // "the new value happens to already be correct".
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to name (patched)",
            "MyDefinedName");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("MyDefinedName", "a defined-name hyperlink target must never be resolved/rewritten into a sheet-qualified cell reference");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("MyDefinedName");
    }

    [Fact]
    public void Save_LoadedWorkbookWithNormalInternalCellReferenceHyperlink_StillPatchSavesSheetQualifiedRef()
    {
        // Sibling no-regression case: an ordinary same-sheet cell-reference internal hyperlink
        // (unquoted, sheet-qualified) must keep round-tripping exactly as before -- the new
        // defined-name detection must not strip a legitimate sheet-qualified cell/range ref.
        var sourceBytes = CreateInternalHyperlinkSourcePackage(
            locationAttr: "Sheet1!B5",
            tooltip: "Jump to cell");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address].Should().Be("Sheet1!B5");

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to cell (patched)",
            "Sheet1!B5");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("Sheet1!B5");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("Sheet1!B5");
    }

    private static byte[] CreateInternalHyperlinkSourcePackage(string locationAttr, string tooltip)
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
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <definedNames>
                    <definedName name="MyDefinedName">Sheet1!$B$2</definedName>
                  </definedNames>
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

    // --- R38-io-hyperlink-2-3 -------------------------------------------------------------

    [Fact]
    public void Save_ExternalHyperlinkTargetWithSpace_WritesPercentEncodedTarget()
    {
        var workbook = new Workbook("HyperlinkEscapeTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Link"));
        sheet.Hyperlinks[address] = "https://example.com/my report.pdf";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        var savedBytes = ms.ToArray();

        var target = ReadHyperlinkRelationshipTarget(savedBytes, "xl/worksheets/sheet1.xml", "A1");
        target.Should().Be(
            "https://example.com/my%20report.pdf",
            "an un-escaped space in a relationship Target is not a valid URI reference");
        target.Should().NotContain(" ");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/my report.pdf");
    }

    [Fact]
    public void Save_ExternalHyperlinkTargetAlreadyPercentEncoded_DoesNotDoubleEncode()
    {
        var workbook = new Workbook("HyperlinkEscapeTest2");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Link"));
        sheet.Hyperlinks[address] = "https://example.com/a%20b.pdf";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        var savedBytes = ms.ToArray();

        ReadHyperlinkRelationshipTarget(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("https://example.com/a%20b.pdf", "an already-escaped target must not become %2520");
    }

    [Fact]
    public void Save_PlainExternalUrlHyperlink_RoundTripsUnchanged()
    {
        // Sibling no-regression case: a normal URL with no characters needing escaping must be
        // written and read back byte-for-byte identical.
        var workbook = new Workbook("HyperlinkEscapeTest3");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Link"));
        sheet.Hyperlinks[address] = "https://example.com/report";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        var savedBytes = ms.ToArray();

        ReadHyperlinkRelationshipTarget(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("https://example.com/report");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/report");
    }

    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static string? ReadHyperlinkRelationshipTarget(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var worksheetDocument = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var ns = worksheetDocument.Root!.Name.Namespace;
        var hyperlinks = worksheetDocument.Root.Element(ns + "hyperlinks");
        var relationshipId = hyperlinks
            ?.Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(RelationshipNs + "id")
            ?.Value;
        relationshipId.Should().NotBeNullOrEmpty("the external hyperlink must be wired through a relationship id");

        var lastSlash = worksheetPath.LastIndexOf('/');
        var worksheetDir = worksheetPath[..lastSlash];
        var worksheetFileName = worksheetPath[(lastSlash + 1)..];
        var relsPath = $"{worksheetDir}/_rels/{worksheetFileName}.rels";

        var relsDocument = XlsxPackageTestFixtures.LoadPackageXml(archive, relsPath);
        var relsNs = relsDocument.Root!.Name.Namespace;
        return relsDocument.Root
            .Elements(relsNs + "Relationship")
            .SingleOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")
            ?.Value;
    }
}
