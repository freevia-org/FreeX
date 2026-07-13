using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 40 (R40-meta-2): r39's docProps dcterms:modified/cp:revision bump
/// (XlsxDocumentPropertiesPreserver.UpdateModifiedAndRevisionOnSave) was only wired into the
/// full-ClosedXML-rebuild save path (XlsxFileAdapter.SourcePackage.cs's Preserve call). The fast
/// cell-patch save path (XlsxFileAdapter.SourcePackageSnapshot.cs's TrySavePatchedCellValues),
/// which is what handles the common everyday "edit a cell, hit Ctrl+S" save, never touched
/// docProps/core.xml at all, so the workbook's Last-Modified timestamp stayed frozen and
/// cp:revision never incremented on an ordinary patch-eligible save.
/// </summary>
public sealed class R40_PatchSaveDocPropsModifiedRevisionTests
{
    private const string CoreXmlNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string DcTermsNs = "http://purl.org/dc/terms/";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    [Fact]
    public void Save_SingleCellPatchEdit_UpdatesModifiedTimestampAndIncrementsRevision()
    {
        var sourceBytes = CreateSourcePackageWithKnownCoreProperties(
            createdValue: "2018-03-04T12:00:00Z",
            modifiedValue: "2019-06-01T00:00:00Z",
            revisionValue: "7");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        // A single existing-cell literal-value edit is the canonical fast cell-patch scenario.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.PathLabel.Should().Be(
            "source_patch",
            "this save must go through the fast cell-patch path, not a full ClosedXML rebuild, " +
            "for this regression to be meaningful");

        var core = ReadCoreProperties(saved.ToArray());

        core.Element((XNamespace)DcTermsNs + "modified")!.Value.Should().NotBe(
            "2019-06-01T00:00:00Z",
            "an ordinary patch-eligible save must bump dcterms:modified to the actual save time, " +
            "not leave the source workbook's frozen stamp in place");

        core.Element((XNamespace)CoreXmlNs + "revision")!.Value.Should().Be(
            "8",
            "cp:revision must increment on a patch-eligible save exactly as it does on a full-rebuild save");

        // dcterms:created is a stable/frozen fact about the document and must be preserved verbatim.
        core.Element((XNamespace)DcTermsNs + "created")!.Value.Should().Be("2018-03-04T12:00:00Z");
    }

    [Fact]
    public void Save_SingleCellPatchEdit_WhenCorePropertiesPartMissing_DoesNotThrow_NoRegression()
    {
        // Sibling no-regression case: a source package with no docProps/core.xml part at all
        // (malformed/stripped real-world file) must not make the patch-save path throw -- the
        // new update logic must no-op gracefully, same as the full-rebuild Preserve() does.
        var workbook = new Workbook("NoCoreProps");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var adapter = new XlsxFileAdapter();
        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);
        initialSave.Position = 0;

        using (var archive = new ZipArchive(initialSave, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("docProps/core.xml")?.Delete();
        }

        initialSave.Position = 0;
        var sourceBytes = initialSave.ToArray();

        Workbook reloaded;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            reloaded = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(reloaded, out _);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 1, 1), new NumberValue(2));

        using var saved = new MemoryStream();
        var act = () => adapter.Save(reloaded, saved);
        act.Should().NotThrow();

        using var savedArchive = new ZipArchive(new MemoryStream(saved.ToArray(), writable: false), ZipArchiveMode.Read);
        savedArchive.GetEntry("docProps/core.xml").Should().BeNull();
    }

    private static byte[] CreateSourcePackageWithKnownCoreProperties(
        string createdValue,
        string modifiedValue,
        string revisionValue)
    {
        var workbook = new Workbook("PatchSaveDocPropsRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var coreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
               <dcterms:created xsi:type="dcterms:W3CDTF">{createdValue}</dcterms:created>
               <dcterms:modified xsi:type="dcterms:W3CDTF">{modifiedValue}</dcterms:modified>
               <cp:lastModifiedBy>John Smith</cp:lastModifiedBy>
               <cp:revision>{revisionValue}</cp:revision>
             </cp:coreProperties>
             """;

        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("docProps/core.xml")?.Delete();
            var entry = archive.CreateEntry("docProps/core.xml");
            using (var writer = new StreamWriter(entry.Open(), System.Text.Encoding.UTF8))
                writer.Write(coreXml);

            // A brand-new FreeX-authored workbook has no docProps/core.xml part at all (see the
            // sibling missing-part test below), so this fixture must register the part exactly
            // as a real Excel-authored file would: a [Content_Types].xml Override and a root
            // _rels/.rels relationship, or ClosedXML's own re-load of this fixture will reject it.
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypesEntry = archive.GetEntry("[Content_Types].xml")!;
            XDocument contentTypesXml;
            using (var s = contentTypesEntry.Open())
                contentTypesXml = XDocument.Load(s);
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/docProps/core.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
            contentTypesEntry.Delete();
            var newContentTypesEntry = archive.CreateEntry("[Content_Types].xml");
            using (var s = newContentTypesEntry.Open())
                contentTypesXml.Save(s);

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsEntry = archive.GetEntry("_rels/.rels")!;
            XDocument relsXml;
            using (var s = relsEntry.Open())
                relsXml = XDocument.Load(s);
            relsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXCoreProperties"),
                new XAttribute(
                    "Type",
                    "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
                new XAttribute("Target", "docProps/core.xml")));
            relsEntry.Delete();
            var newRelsEntry = archive.CreateEntry("_rels/.rels");
            using (var s = newRelsEntry.Open())
                relsXml.Save(s);
        }

        source.Position = 0;
        return source.ToArray();
    }

    private static XElement ReadCoreProperties(byte[] packageBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        using var entryStream = archive.GetEntry("docProps/core.xml")!.Open();
        return XDocument.Load(entryStream).Root!;
    }
}
