using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-28 finding R28-meta-3 in
/// XlsxFileAdapter.SourcePackageSnapshot.cs's workbook defined-name resurrection path
/// (RestorePatchWorkbookDefinedNames): when a sheet-scoped defined name FreeX cannot model loses
/// its scope-sheet name lookup (the old scope sheet name is no longer present anywhere in the
/// saved sheet list), the count+ordinal-only heuristic used to assume this was always a plain
/// RENAME of the scope sheet. That is wrong when the scope sheet was instead DELETED and a
/// different, brand-new sheet was added at the same ordinal (net sheet count unchanged) - the
/// name would get silently reattached to the unrelated new sheet. The fix disambiguates by the
/// model's stable per-sheet identity (Sheet.Id) instead of by count+ordinal alone: only keep/
/// re-scope the name when the ORIGINAL scope sheet (same Sheet.Id) still exists in the model
/// (a genuine rename); otherwise drop the name (a genuine delete+add-different-sheet).
/// </summary>
public sealed class R28_MetaDefinedNameRenameHeuristicTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_AfterScopeSheetDeleteAndDifferentSheetAddedAtSameOrdinal_UnmodelableSheetScopedName_Dropped()
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

        // Delete the scope sheet (Sheet1) and insert a brand-new, UNRELATED sheet at the same
        // ordinal position 0. Net sheet count is unchanged (still 2: 'Sheet3', 'Other') and the
        // new sheet lands at the exact ordinal the old count+ordinal-only heuristic would have
        // matched against - this is the count-preserving delete+add-different-sheet sequence
        // that must NOT be treated as a rename.
        var sheet1 = loaded.Sheets.Single(s => s.Name == "Sheet1");
        loaded.RemoveSheet(sheet1.Id).Should().BeTrue();
        loaded.InsertSheet(0, "Sheet3");
        loaded.Sheets.Select(s => s.Name).Should().Equal("Sheet3", "Other");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "LocalRate")
            .ToList() ?? [];

        resurrected.Should().BeEmpty(
            "the name's scope sheet (Sheet1) was genuinely deleted and replaced by an unrelated " +
            "new sheet at the same ordinal - the name must be dropped, not silently reattached to " +
            "'Sheet3' just because the sheet count and ordinal position happen to match (R28-meta-3)");
    }

    [Fact]
    public void Save_AfterSheetRename_UnmodelableSheetScopedName_KeptAndReScoped()
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

        // Plain rename, no other structural change: the SAME sheet object survives (same
        // Sheet.Id), just under a new name and at the same ordinal position 0.
        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "LocalRate")
            .ToList() ?? [];

        resurrected.Should().ContainSingle(
            "a plain sheet rename must still be treated as a rename - the sibling already-working " +
            "case the identity-based fix must not regress (R28-meta-3)");
        var localSheetIdAttr = resurrected[0].Attribute("localSheetId");
        localSheetIdAttr.Should().NotBeNull();
        localSheetIdAttr!.Value.Should().Be(
            "0",
            "the renamed sheet's ordinal position never changed, so localSheetId must stay 0, still " +
            "scoping the name to the renamed 'Data' sheet");
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
    /// model, mirroring the existing P112/R27 fixture convention.
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
