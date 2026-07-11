using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-27 findings in XlsxFileAdapter.SourcePackageSnapshot.cs's workbook
/// defined-name resurrection path (RestorePatchWorkbookDefinedNames):
///  - R27-io-workbook-parts-deep-1: when the full-save model has nothing representable to write
///    into &lt;definedNames&gt;, a freshly-created &lt;definedNames&gt; element used to resurrect a
///    workbook-scoped name FreeX cannot model must be inserted in CT_Workbook schema order (after
///    sheets/functionGroups/externalReferences), not unconditionally right after &lt;sheets&gt;
///    where it can land BEFORE an already-restored &lt;externalReferences&gt; and produce a
///    schema-invalid workbook.xml.
///  - R27-io-workbook-parts-deep-2: resurrecting a sheet-scoped defined name FreeX cannot model
///    must distinguish a plain sheet RENAME (the scope sheet still exists, just under a new name -
///    keep the name, remapped to the same ordinal position) from an actual sheet DELETE (the scope
///    sheet is genuinely gone - drop the name, per P112), instead of treating any scope-sheet
///    name-lookup miss as a delete.
/// </summary>
public sealed class R27_WorkbookPartsDefinedNameResurrectionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ── R27-io-workbook-parts-deep-1 ────────────────────────────────────────────────────────

    // These two tests trigger the element-creation branch the same way finding-2's rename case
    // does: XlsxWorkbookMetadataPreserver.MergeDefinedNames runs FIRST (during full-save package
    // post-processing) and attempts its own (separately buggy, name-based) localSheetId remap for
    // the sheet-scoped name; on a plain rename that lookup also misses there, so it leaves
    // &lt;definedNames&gt; uncreated - which is exactly the precondition
    // RestorePatchWorkbookDefinedNames's element-creation branch needs. (A workbook-scoped name
    // with no rename involved never reaches the "create" branch at all: MergeDefinedNames already
    // creates and correctly positions the element - via the schema normalizer that runs right
    // after it - before RestorePatchWorkbookDefinedNames ever sees it.)
    [Fact]
    public void Save_AfterSheetRenameWithExternalReferences_ResurrectedDefinedNames_InsertedAfterExternalReferences()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(source);
        AddDefinedName(source, "LocalRate", "0.0825", localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        // Confirm the premise: the constant-literal name never made it into the model, matching
        // the never-loaded-in-the-first-place reasoning P112/this finding both rely on.
        loaded.NamedRanges.Should().NotContainKey("LocalRate");
        loaded.NamedFormulas.Should().NotContainKey("LocalRate");

        // Force a full (non-patch) save via a plain rename of the name's scope sheet.
        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var childNames = root.Elements().Select(e => e.Name.LocalName).ToList();
        var sheetsIndex = childNames.IndexOf("sheets");
        var externalReferencesIndex = childNames.IndexOf("externalReferences");
        var definedNamesIndex = childNames.IndexOf("definedNames");

        sheetsIndex.Should().BeGreaterThanOrEqualTo(0);
        externalReferencesIndex.Should().BeGreaterThan(
            sheetsIndex,
            "the externalReferences element preserved from the source package must still be present");
        definedNamesIndex.Should().BeGreaterThan(
            externalReferencesIndex,
            "the resurrected <definedNames> must be inserted AFTER <externalReferences> per the CT_Workbook " +
            "schema sequence, not unconditionally right after <sheets> where it would precede " +
            "externalReferences and produce a schema-invalid workbook.xml (R27-io-workbook-parts-deep-1)");

        var definedName = root.Element(WorkbookNs + "definedNames")!
            .Elements(WorkbookNs + "definedName")
            .Should().ContainSingle(name => name.Attribute("name") != null && name.Attribute("name")!.Value == "LocalRate")
            .Subject;
        definedName.Value.Should().Be("0.0825");
    }

    [Fact]
    public void Save_AfterSheetRenameWithoutExternalReferences_ResurrectedDefinedNames_StillInsertedRightAfterSheets()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "LocalRate", "0.0825", localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var childNames = root.Elements().Select(e => e.Name.LocalName).ToList();
        var sheetsIndex = childNames.IndexOf("sheets");
        var definedNamesIndex = childNames.IndexOf("definedNames");

        definedNamesIndex.Should().Be(
            sheetsIndex + 1,
            "with no functionGroups/externalReferences present, the resurrected <definedNames> still " +
            "belongs directly after <sheets> - the ordinary (already-working) case must be unaffected " +
            "by the schema-order fix");
    }

    // ── R27-io-workbook-parts-deep-2 ────────────────────────────────────────────────────────

    [Fact]
    public void Save_AfterSheetRename_ResurrectedUnmodelableSheetScopedName_KeepsSameOrdinalPosition()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "LocalRate", "0.0825", localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        loaded.NamedRanges.Should().NotContainKey("LocalRate");
        loaded.NamedFormulas.Should().NotContainKey("LocalRate");

        // Plain rename, no other structural change: Sheet1 -> Data. Ordinal position 0 and the
        // overall sheet count (2) are both unchanged.
        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "LocalRate")
            .ToList() ?? [];

        resurrected.Should().ContainSingle(
            "a plain sheet rename must not be treated as a delete - the name's scope sheet is still " +
            "there, just renamed (R27-io-workbook-parts-deep-2)");
        var localSheetIdAttr = resurrected[0].Attribute("localSheetId");
        localSheetIdAttr.Should().NotBeNull();
        localSheetIdAttr!.Value.Should().Be(
            "0",
            "the scope sheet's ordinal position never changed (a rename doesn't reorder or remove " +
            "sheets), so localSheetId must stay 0, still scoping the name to the renamed 'Data' sheet");
    }

    [Fact]
    public void Save_AfterScopeSheetDelete_UnmodelableSheetScopedName_StillDropped()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "LocalRate", "0.0825", localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        // Delete the SCOPE sheet itself (not merely rename it) - the name's scope is genuinely gone.
        var sheet1 = loaded.Sheets.Single(s => s.Name == "Sheet1");
        loaded.RemoveSheet(sheet1.Id).Should().BeTrue();
        loaded.Sheets.Should().ContainSingle(s => s.Name == "Other");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "LocalRate")
            .ToList() ?? [];

        resurrected.Should().BeEmpty(
            "the name's scope sheet was genuinely deleted (not renamed), so it must still be dropped " +
            "instead of being resurrected onto the wrong (surviving) sheet");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static XElement ReadWorkbookRoot(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    /// <summary>
    /// Adds a defined name to the SOURCE package's pristine workbook.xml that FreeX cannot model
    /// (a constant-literal refersTo, e.g. "0.0825") - so it round-trips only through
    /// RestorePatchWorkbookDefinedNames's unconditional-resurrection path, never through the live
    /// model, mirroring the existing P112 fixture convention.
    /// </summary>
    private static void AddDefinedName(MemoryStream package, string name, string refersTo, int? localSheetId)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var root = workbookXml.Root!;
            var definedNames = root.Element(WorkbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(WorkbookNs + "definedNames");
                // Correct CT_Workbook position for the SOURCE fixture: after externalReferences (if
                // present), otherwise right after sheets.
                var precedingSibling = root.Element(WorkbookNs + "externalReferences")
                    ?? root.Element(WorkbookNs + "sheets");
                if (precedingSibling is not null)
                    precedingSibling.AddAfterSelf(definedNames);
                else
                    root.Add(definedNames);
            }

            var definedName = new XElement(WorkbookNs + "definedName", new XAttribute("name", name), refersTo);
            if (localSheetId is { } id)
                definedName.SetAttributeValue("localSheetId", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            definedNames.Add(definedName);

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }

    /// <summary>
    /// Adds a minimal, real external-link package (matching what Excel writes for a formula like
    /// ='[Book2.xlsx]Sheet1'!A1) to the source package: xl/externalLinks/externalLink1.xml + its
    /// rels, the workbook.xml.rels relationship, the workbook.xml &lt;externalReferences&gt;
    /// element, and the [Content_Types].xml override - mirroring
    /// XlsxNonChartSchemaValidationTests.ExternalLinks.cs's AddExternalLinkPackage fixture.
    /// </summary>
    private static void AddExternalLinkPackage(MemoryStream package)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddContentTypeOverride(
                archive,
                "/xl/externalLinks/externalLink1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");

            var workbookEntry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = workbookEntry.Open())
                workbookXml = XDocument.Load(entryStream);
            var root = workbookXml.Root!;
            root.Elements(WorkbookNs + "externalReferences").Remove();
            var externalReferences = new XElement(
                WorkbookNs + "externalReferences",
                new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLink")));
            // Correct CT_Workbook position for the SOURCE fixture: right after <sheets>.
            root.Element(WorkbookNs + "sheets")!.AddAfterSelf(externalReferences);
            workbookEntry.Delete();
            var workbookReplacement = archive.CreateEntry("xl/workbook.xml");
            using (var replacementStream = workbookReplacement.Open())
                workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);

            const string relsPath = "xl/_rels/workbook.xml.rels";
            var relsEntry = archive.GetEntry(relsPath)!;
            XDocument relsXml;
            using (var entryStream = relsEntry.Open())
                relsXml = XDocument.Load(entryStream);
            relsXml.Root!.Add(new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXExternalLink"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
                new XAttribute("Target", "externalLinks/externalLink1.xml")));
            relsEntry.Delete();
            var relsReplacement = archive.CreateEntry(relsPath);
            using (var replacementStream = relsReplacement.Open())
                relsXml.Save(replacementStream, SaveOptions.DisableFormatting);

            var externalLinkXml = new XDocument(
                new XElement(
                    WorkbookNs + "externalLink",
                    new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(
                        WorkbookNs + "externalBook",
                        new XAttribute(RelNs + "id", "rIdFreeXExternalBook"),
                        new XElement(
                            WorkbookNs + "sheetNames",
                            new XElement(WorkbookNs + "sheetName", new XAttribute("val", "LinkedSheet"))))));
            var externalLinkEntry = archive.CreateEntry("xl/externalLinks/externalLink1.xml");
            using (var writer = new StreamWriter(externalLinkEntry.Open()))
                externalLinkXml.Save(writer, SaveOptions.DisableFormatting);

            var externalLinkRelsXml = new XDocument(
                new XElement(
                    PackageRelNs + "Relationships",
                    new XElement(
                        PackageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreeXExternalBook"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                        new XAttribute("Target", "linked-workbook.xlsx"),
                        new XAttribute("TargetMode", "External"))));
            var externalLinkRelsEntry = archive.CreateEntry("xl/externalLinks/_rels/externalLink1.xml.rels");
            using (var writer = new StreamWriter(externalLinkRelsEntry.Open()))
                externalLinkRelsXml.Save(writer, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var entry = archive.GetEntry("[Content_Types].xml")!;
        XDocument contentTypesXml;
        using (var entryStream = entry.Open())
            contentTypesXml = XDocument.Load(entryStream);

        contentTypesXml.Root!.Add(new XElement(
            contentTypesNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));

        entry.Delete();
        var replacement = archive.CreateEntry("[Content_Types].xml");
        using var replacementStream = replacement.Open();
        contentTypesXml.Save(replacementStream, SaveOptions.DisableFormatting);
    }
}
