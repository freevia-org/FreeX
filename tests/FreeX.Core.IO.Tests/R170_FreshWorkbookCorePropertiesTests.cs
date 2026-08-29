using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// shared-document-properties F2: a brand-new, never-loaded-from-.xlsx workbook
/// (<c>new Workbook(...)</c> saved directly, without ever calling <see cref="XlsxFileAdapter.Load"/>
/// first) has no <c>SourcePackages</c> entry at all, so
/// <c>XlsxFileAdapter.SavePostProcessing.cs</c>'s own <c>!hasSourcePackage</c> branch returns before
/// ever reaching <c>PreserveSourcePackageParts</c> (in <c>XlsxFileAdapter.SourcePackage.cs</c>) --
/// and ClosedXML's own <c>SaveAs</c> never writes <c>docProps/core.xml</c> on its own. Before this
/// fix the saved package carried no <c>docProps/core.xml</c> part at all: no dcterms:created, no
/// dcterms:modified, ever, for the life of the file, and every later save of that same
/// never-loaded workbook hit the identical gap (repeated <c>Save()</c> calls never populate
/// <c>SourcePackages</c> either).
/// </summary>
public sealed class R170_FreshWorkbookCorePropertiesTests
{
    private static readonly XNamespace CorePropertiesNs =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DcTermsNs = "http://purl.org/dc/terms/";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string CorePropertiesRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";

    [Fact]
    public void Save_BrandNewNeverLoadedWorkbook_WritesWiredUpCorePropertiesPart()
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // The core defect: the part must exist at all.
        var coreEntry = archive.GetEntry("docProps/core.xml");
        coreEntry.Should().NotBeNull(
            "a brand-new FreeX workbook must get a docProps/core.xml part, just like a real " +
            "Excel-authored workbook does, so Created/Modified are never permanently absent");

        var coreRoot = XDocument.Load(coreEntry!.Open()).Root!;
        var created = coreRoot.Element(DcTermsNs + "created")?.Value;
        var modified = coreRoot.Element(DcTermsNs + "modified")?.Value;
        created.Should().NotBeNullOrWhiteSpace("Created must be stamped on the very first save");
        modified.Should().NotBeNullOrWhiteSpace("Modified must be stamped on the very first save");
        DateTimeOffset.TryParse(created, out var createdValue).Should().BeTrue("must be a valid W3CDTF timestamp");
        DateTimeOffset.TryParse(modified, out var modifiedValue).Should().BeTrue("must be a valid W3CDTF timestamp");
        createdValue.Should().Be(modifiedValue, "Excel itself sets Created == Modified on a document's first save");
        (DateTimeOffset.UtcNow - createdValue).Should().BeLessThan(
            TimeSpan.FromMinutes(2),
            "the stamped timestamp must be the actual save instant, not some frozen/default value");

        // The part must be correctly wired into the package graph, or real Excel/other OPC
        // readers may reject or silently ignore it.
        var contentTypesRoot = XDocument.Load(archive.GetEntry("[Content_Types].xml")!.Open()).Root!;
        contentTypesRoot.Elements(ContentTypesNs + "Override")
            .Where(e => e.Attribute("PartName")?.Value == "/docProps/core.xml" &&
                        e.Attribute("ContentType")?.Value == CorePropertiesContentType)
            .Should().ContainSingle(
                "docProps/core.xml must have its own Content-Types Override -- this package's " +
                "generic xml Default maps to the WORKBOOK's own content type, not core-properties");

        var relsRoot = XDocument.Load(archive.GetEntry("_rels/.rels")!.Open()).Root!;
        relsRoot.Elements(PackageRelNs + "Relationship")
            .Where(e => e.Attribute("Type")?.Value == CorePropertiesRelationshipType &&
                        e.Attribute("Target")?.Value?.TrimStart('/') == "docProps/core.xml")
            .Should().ContainSingle("the root relationship for the core-properties part must be present");

        // Must still be a schema-valid, loadable package.
        saved.Position = 0;
        var act = () => new ClosedXML.Excel.XLWorkbook(saved);
        act.Should().NotThrow("the new part must not corrupt the package");
    }

    [Fact]
    public void Save_BrandNewWorkbookSavedRepeatedlyWithoutReload_WritesCorePropertiesEveryTime()
    {
        // The user gesture from the finding: "File > New, enter data, save as X.xlsx (repeat Save
        // as many times as you like)". A workbook that is only ever Saved (never Loaded) never
        // populates SourcePackages, so each independent Save() call must go through the same
        // !hasSourcePackage path and must not regress on repeat saves.
        var workbook = new Workbook("Untitled");
        workbook.AddSheet("Sheet1");
        var adapter = new XlsxFileAdapter();

        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);
        firstSave.Position = 0;
        using (var firstArchive = new ZipArchive(firstSave, ZipArchiveMode.Read, leaveOpen: true))
            firstArchive.GetEntry("docProps/core.xml").Should().NotBeNull();

        using var secondSave = new MemoryStream();
        adapter.Save(workbook, secondSave);
        secondSave.Position = 0;
        using (var secondArchive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true))
            secondArchive.GetEntry("docProps/core.xml").Should().NotBeNull(
                "repeating Save() on the same never-loaded workbook must not regress -- FreeX has " +
                "no SourcePackages entry to remember the first save's part by, so each save takes " +
                "the same fresh-workbook path and must independently write the part again");
    }

    /// <summary>
    /// Sibling no-regression case: a workbook that WAS loaded from a real source package whose
    /// docProps/core.xml was deliberately stripped (a malformed/stripped real-world file, or the
    /// R40 patch-save scenario) must keep behaving exactly as before -- this fix only touches the
    /// "no source package at all" path (XlsxFileAdapter.SavePostProcessing.cs's
    /// !hasSourcePackage branch); it must never resurrect a part that a genuine loaded source
    /// package intentionally lacks via the full-rebuild (PreserveSourcePackageParts) path either.
    /// </summary>
    [Fact]
    public void Save_LoadedWorkbookWhoseSourceLacksCoreProperties_FullRebuildDoesNotFabricateOne()
    {
        var workbook = new Workbook("HasSourcePackage");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var adapter = new XlsxFileAdapter();
        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);
        initialSave.Position = 0;

        // Strip docProps/core.xml so the RELOADED source package genuinely has none -- exactly
        // the R40 sibling fixture shape, but this time forcing a FULL ClosedXML rebuild (rename a
        // sheet, which VBA/patch-save cannot handle) so PreserveSourcePackageParts itself runs.
        using (var archive = new ZipArchive(initialSave, ZipArchiveMode.Update, leaveOpen: true))
            archive.GetEntry("docProps/core.xml")?.Delete();
        initialSave.Position = 0;

        var reloaded = adapter.Load(initialSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(reloaded, out _);
        reloaded.GetSheetAt(0).Name = "Renamed";

        using var saved = new MemoryStream();
        adapter.Save(reloaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "renaming a sheet must force the full ClosedXML-rebuild path (PreserveSourcePackageParts), " +
            "not the fresh-workbook !hasSourcePackage path this fix changed");

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("docProps/core.xml").Should().BeNull(
            "a loaded source package that genuinely has no docProps/core.xml must not have one " +
            "fabricated by the full-rebuild save path -- only the never-loaded-workbook path does " +
            "that, per the sibling R40 contract");
    }
}
