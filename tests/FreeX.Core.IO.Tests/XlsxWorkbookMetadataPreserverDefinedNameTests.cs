using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for XlsxWorkbookMetadataPreserver.MergeDefinedNames (the FULL-save
/// &lt;definedNames&gt; preservation path, sibling to
/// XlsxFileAdapter.SourcePackageSnapshot.RestorePatchWorkbookDefinedNames):
///  - Liveness gate: a model-representable defined name the user deleted from the Name Manager must
///    NOT be resurrected from the pristine source snapshot on the next full-save-triggering edit.
///    Before the fix, MergeDefinedNames unconditionally re-added ANY source defined name missing
///    from the freshly-written target, silently bringing a deleted named range back forever.
///  - Rename-vs-delete disambiguation for a sheet-scoped name FreeX cannot model: a pure sheet
///    RENAME must keep the name (re-scoped to the same sheet, now under its new name), while an
///    actual sheet DELETE must drop it. Before the fix, MergeDefinedNames' localSheetId remap did a
///    plain name-based lookup and dropped the name on ANY miss, conflating rename with delete
///    (mirrors R27-io-workbook-parts-deep-2 / R28-meta-3, previously fixed only in
///    RestorePatchWorkbookDefinedNames, which masked the drop for that one specific scenario).
/// </summary>
public sealed class XlsxWorkbookMetadataPreserverDefinedNameTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── Liveness gate (deleted named range must not reappear) ──────────────────────────────────

    [Fact]
    public void Save_AfterDeletingModelRepresentableNamedRange_AndFullSaveTriggeringEdit_DoesNotResurrectIt()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        // A plain, model-representable named range (a rectangle on Sheet1) - FreeX writes it into the
        // source package's <definedNames> and loads it back into the live model on open.
        workbook.DefineNamedRange(
            "MyRange",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2)));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        // Premise: the name really did round-trip into the live model (otherwise this would be
        // testing an unmodelable name that is intentionally resurrected, not the deletion path).
        loaded.NamedRanges.Should().ContainKey("MyRange");

        // The user deletes the name from the Name Manager.
        loaded.RemoveNamedRange("MyRange").Should().BeTrue();

        // Force a full (non-patch) save via a plain rename of an UNRELATED sheet, so the save runs
        // through XlsxWorkbookMetadataPreserver.MergeDefinedNames rather than a byte-copy fast path.
        loaded.Sheets.Single(s => s.Name == "Sheet2").Name = "Renamed";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "MyRange")
            .ToList() ?? [];

        resurrected.Should().BeEmpty(
            "a model-representable named range the user deleted from the Name Manager must not be " +
            "resurrected from the pristine source snapshot - MergeDefinedNames must gate on the live " +
            "model (GetLiveDefinedNameKeys), not re-add every source name missing from the target");
    }

    // ── Rename-vs-delete disambiguation (MergeDefinedNames in isolation) ────────────────────────

    // These exercise MergeDefinedNames directly (via Preserve) so the outcome is attributable to it
    // alone. End-to-end, RestorePatchWorkbookDefinedNames runs afterward and shares the same
    // disambiguation, which would mask a MergeDefinedNames-only regression for the plain-rename case.

    [Fact]
    public void Preserve_AfterSheetRename_UnmodelableSheetScopedName_KeptAndReScopedToRenamedSheet()
    {
        // Current (post-rename) model: 'Sheet1' was renamed to 'Data'; the SAME Sheet object (its
        // stable Sheet.Id) survives at ordinal 0.
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        // Pristine <sheets> order was [Sheet1, Other]; Sheet1's identity == the surviving 'Data' sheet.
        var sourceSheetIdsByLocalId = new[] { data.Id, other.Id };

        using var target = BuildTargetPackage(workbook);
        // R66-io-defined-names-scope-6-2: "#REF!" (a genuinely still-unmodelable refersTo) is used
        // here rather than a constant literal like "0.0825" - a bare numeric refersTo is now actually
        // loaded into ScopedNamedFormulas (see XlsxNamedRangeMapper.IsConstantLiteralRefersTo), so it
        // no longer exercises this test's "unmodelable name" premise; it would instead hit the
        // liveness gate above (isModelRepresentable=true) and correctly get dropped as a live-model
        // absence, not resurrected.
        using var sourcePackage = BuildSourcePackage(
            pristineSheetNames: ["Sheet1", "Other"],
            name: "LocalRate",
            refersTo: "#REF!",
            localSheetId: 0);

        RunPreserve(sourcePackage, target, workbook, sourceSheetIdsByLocalId);

        var resurrected = ReadDefinedNames(target, "LocalRate");
        resurrected.Should().ContainSingle(
            "a plain sheet rename must be treated as a rename by MergeDefinedNames itself - the " +
            "name's scope sheet is still there (same Sheet.Id), just renamed - not dropped on a " +
            "name-lookup miss");
        resurrected[0].Attribute("localSheetId")!.Value.Should().Be(
            "0",
            "the renamed sheet's ordinal position never changed, so localSheetId stays 0, still " +
            "scoping the name to the renamed 'Data' sheet");
    }

    [Fact]
    public void Preserve_AfterScopeSheetDelete_UnmodelableSheetScopedName_Dropped()
    {
        // Current model: the scope sheet ('Sheet1') was genuinely DELETED - only 'Other' remains.
        var workbook = new Workbook("Test");
        var other = workbook.AddSheet("Other");
        // Pristine <sheets> order was [Sheet1, Other]; index 0 is the deleted Sheet1's Id, which is
        // no longer present in the current model (a delete leaves no surviving Sheet with that Id).
        var sourceSheetIdsByLocalId = new[] { SheetId.New(), other.Id };

        using var target = BuildTargetPackage(workbook);
        // R66-io-defined-names-scope-6-2: kept in sync with the sibling rename test's "#REF!"
        // choice for the same reason (a bare numeric refersTo is now model-representable), even
        // though this delete scenario short-circuits (renamedSheetIndex < 0) before the
        // modelability check would matter either way.
        using var sourcePackage = BuildSourcePackage(
            pristineSheetNames: ["Sheet1", "Other"],
            name: "LocalRate",
            refersTo: "#REF!",
            localSheetId: 0);

        RunPreserve(sourcePackage, target, workbook, sourceSheetIdsByLocalId);

        ReadDefinedNames(target, "LocalRate").Should().BeEmpty(
            "the name's scope sheet was genuinely deleted (its Sheet.Id no longer exists in the " +
            "model), so MergeDefinedNames must drop it rather than reattach it to the surviving sheet");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static void RunPreserve(
        MemoryStream sourcePackage,
        MemoryStream target,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        sourcePackage.Position = 0;
        target.Position = 0;
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(target, ZipArchiveMode.Update, leaveOpen: true);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook, sourceSheetIdsByLocalId);
    }

    private static List<XElement> ReadDefinedNames(MemoryStream package, string name)
    {
        var root = ReadWorkbookRoot(package);
        return root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(element => element.Attribute("name")?.Value == name)
            .ToList() ?? [];
    }

    /// <summary>
    /// Builds the "target" package: a real, freshly-written package for the CURRENT model state,
    /// with no &lt;definedNames&gt; (an unmodelable sheet-scoped name is never written by the model
    /// round-trip), exactly the input MergeDefinedNames merges the preserved source names into.
    /// </summary>
    private static MemoryStream BuildTargetPackage(Workbook workbook) =>
        XlsxPackageTestHelper.SaveWorkbook(workbook);

    /// <summary>
    /// Builds the "source" package: a real package whose pristine &lt;sheets&gt; carries
    /// <paramref name="pristineSheetNames"/> (pre-edit names) plus a single injected sheet-scoped
    /// defined name FreeX cannot model (a constant-literal refersTo), matching the P112/R27 fixture
    /// convention.
    /// </summary>
    private static MemoryStream BuildSourcePackage(
        string[] pristineSheetNames,
        string name,
        string refersTo,
        int? localSheetId)
    {
        var pristine = new Workbook("Test");
        foreach (var sheetName in pristineSheetNames)
            pristine.AddSheet(sheetName);

        var package = XlsxPackageTestHelper.SaveWorkbook(pristine);
        AddDefinedName(package, name, refersTo, localSheetId);
        return package;
    }

    private static XElement ReadWorkbookRoot(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    /// <summary>
    /// Adds a defined name to a package's pristine workbook.xml (matching the P112/R27/R28 fixture
    /// convention). A constant-literal <paramref name="refersTo"/> such as "0.0825" is one FreeX
    /// cannot model, so it exercises the preservation/resurrection path rather than the live-model
    /// round-trip.
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
}
