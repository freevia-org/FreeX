using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Equivalence tests for the O(1)-per-sheet refactoring of GetExcludedWorksheetPackagePartPaths.
/// The second loop in that method computes "retained targets outside this sheet" to determine
/// whether a legacyDrawingHF VML dependency should be excluded from the saved package. The
/// refactored version precomputes per-sheet dep sets once (O(N) total) and uses reference-count
/// data instead of re-traversing all other retained sheets per sheet (was O(N²)).
///
/// NOTE on test expectations: FreeX runs RemoveClearedPictures after PreserveSourcePackageParts.
/// For every retained sheet that has no HF pictures in the workbook model, RemoveClearedPictures
/// will delete its legacyDrawingHF VML dependency from the archive. Therefore, in these tests
/// (where no sheet has model HF pictures), no VML file survives to the final output regardless
/// of whether it was excluded by GetExcludedWorksheetPackagePartPaths.
///
/// The key regression being guarded: when Sheet3 is deleted, the unique VML (referenced only
/// by Sheet3) must NOT be copied into the output at all. If the exclusion logic has a bug and
/// copies it, it would remain as an unreferenced orphan because RemoveClearedPictures only cleans
/// up VML for retained sheets. Test 2 verifies this invariant.
/// </summary>
public sealed partial class XlsxBroaderRetentionChecksTests
{
    /// <summary>
    /// Three retained sheets, all with no HF pictures in the model.
    /// Sheet1 and Sheet2 both reference the shared VML via legacyDrawingHF.
    /// Sheet3 references a unique VML via legacyDrawingHF.
    /// When all sheets are retained, GetExcludedWorksheetPackagePartPaths has no removed
    /// worksheets so its second loop does not run. Both VMLs are copied by
    /// CopyUnknownPackageParts, then deleted by RemoveClearedPictures (no HF pictures).
    /// Neither VML should appear in the output.
    /// </summary>
    [Fact]
    public void LegacyDrawingHfExclusion_NoVmlOrphans_WhenAllSheetsRetained()
    {
        using var source = CreateThreeSheetPackageWithLegacyDrawingHf(
            out var sharedVmlPath,
            out var uniqueVmlPath);

        using var saved = LoadEditSaveThreeSheets(source);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var allPaths = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Both VMLs are removed by RemoveClearedPictures (sheets have no model HF pictures).
        allPaths.Should().NotContain(sharedVmlPath,
            "RemoveClearedPictures must remove the shared VML when no retained sheet has model HF pictures");
        allPaths.Should().NotContain(uniqueVmlPath,
            "RemoveClearedPictures must remove the unique VML when no retained sheet has model HF pictures");
    }

    /// <summary>
    /// Sheet3 is deleted before save. Sheet1 and Sheet2 are retained.
    /// GetExcludedWorksheetPackagePartPaths second loop runs for the removed sheet(s).
    /// The unique VML (referenced only by deleted Sheet3) must be excluded (not copied into
    /// the output). The shared VML is not excluded (referenced by two retained sheets) but
    /// is then deleted by RemoveClearedPictures (no model HF pictures on retained sheets).
    ///
    /// Regression guarded: if the O(N) exclusion logic has a bug that fails to add the
    /// unique VML to excludedSourceParts, CopyUnknownPackageParts would copy it. Since no
    /// retained sheet references it via legacyDrawingHF, RemoveClearedPictures would not
    /// remove it — leaving it as an orphan in the archive. The NotContain assertion catches this.
    /// </summary>
    [Fact]
    public void LegacyDrawingHfExclusion_UniqueVmlNotOrphaned_AfterDeletingThirdSheet()
    {
        using var source = CreateThreeSheetPackageWithLegacyDrawingHf(
            out var sharedVmlPath,
            out var uniqueVmlPath);

        using var saved = LoadEditSaveThreeSheets(source, deleteSheet3: true);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var allPaths = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Unique VML must not be present: either excluded before copy (correct behaviour) or
        // would be an orphan if the exclusion logic had a bug.
        allPaths.Should().NotContain(uniqueVmlPath,
            "the VML referenced only by the deleted sheet must not appear in the output " +
            "(excluded by GetExcludedWorksheetPackagePartPaths before CopyUnknownPackageParts runs)");

        // Shared VML is also absent: not excluded by exclusion logic (refCount = 2 across retained
        // sheets) but deleted by RemoveClearedPictures (retained sheets have no model HF pictures).
        allPaths.Should().NotContain(sharedVmlPath,
            "RemoveClearedPictures must remove the shared VML when retained sheets have no model HF pictures");
    }

    // ---- helpers ----

    private static MemoryStream CreateThreeSheetPackageWithLegacyDrawingHf(
        out string sharedVmlPath,
        out string uniqueVmlPath)
    {
        sharedVmlPath = "xl/drawings/vmlDrawingShared.vml";
        uniqueVmlPath = "xl/drawings/vmlDrawingUnique.vml";

        // Build a minimal 3-sheet workbook; save to get the base package structure.
        var workbook = new Workbook("LegacyDrawingHfTest");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.AddSheet("Sheet3");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        // Patch the package with legacyDrawingHF elements and VML parts.
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Add VML content stubs.
            var vmlStub = "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\"/>"u8.ToArray();
            WriteEntry(archive, sharedVmlPath, vmlStub);
            WriteEntry(archive, uniqueVmlPath, vmlStub);

            // Add content-type defaults for .vml (if not already present via Default).
            var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
            EnsureDefaultContentType(contentTypesXml, "vml", "application/vnd.openxmlformats-officedocument.vmlDrawing");
            ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

            // Detect sheet paths from the workbook rels.
            var (sheet1Path, sheet2Path, sheet3Path) = DetectSheetPaths(archive);

            // Wire Sheet1 → shared VML via legacyDrawingHF.
            AddLegacyDrawingHfToSheet(archive, sheet1Path, "../drawings/vmlDrawingShared.vml", "rIdVmlShared1");

            // Wire Sheet2 → shared VML via legacyDrawingHF (same target).
            AddLegacyDrawingHfToSheet(archive, sheet2Path, "../drawings/vmlDrawingShared.vml", "rIdVmlShared2");

            // Wire Sheet3 → unique VML via legacyDrawingHF.
            AddLegacyDrawingHfToSheet(archive, sheet3Path, "../drawings/vmlDrawingUnique.vml", "rIdVmlUnique");
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream LoadEditSaveThreeSheets(MemoryStream source, bool deleteSheet3 = false)
    {
        // Verify source archive contains the VML stubs before loading.
        source.Position = 0;
        using (var preCheck = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        {
            preCheck.GetEntry("xl/drawings/vmlDrawingShared.vml").Should().NotBeNull("source must have sharedVml before load");
            preCheck.GetEntry("xl/drawings/vmlDrawingUnique.vml").Should().NotBeNull("source must have uniqueVml before load");
        }

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        // Structural edit: add then immediately remove a temp sheet to force the full
        // ClosedXML save path, bypassing the patch-save shortcut. A simple cell-value
        // change can trigger the patch path which skips PreserveSourcePackageParts.
        var tempSheet = workbook.AddSheet("__TempForFullSave__");
        workbook.RemoveSheet(tempSheet.Id);

        var sheet1 = workbook.GetSheetAt(0);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("edit"));

        if (deleteSheet3)
            workbook.RemoveSheet(workbook.GetSheetAt(2).Id);

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static (string Sheet1Path, string Sheet2Path, string Sheet3Path) DetectSheetPaths(ZipArchive archive)
    {
        XNamespace packageRelNs = PackageRelNs;
        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sheetRels = workbookRels.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(r => r.Attribute("Type")?.Value
                .EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(r => r.Attribute("Id")?.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sheetRels.Should().HaveCountGreaterThanOrEqualTo(3, "test package must have at least 3 sheets");

        return (
            ResolveWorkbookRelTarget(sheetRels[0].Attribute("Target")!.Value),
            ResolveWorkbookRelTarget(sheetRels[1].Attribute("Target")!.Value),
            ResolveWorkbookRelTarget(sheetRels[2].Attribute("Target")!.Value)
        );
    }

    /// <summary>Resolve a target from xl/_rels/workbook.xml.rels to a package-root-relative path.</summary>
    private static string ResolveWorkbookRelTarget(string target)
    {
        // Relationship targets are relative to xl/ (the workbook part's base).
        // e.g. "worksheets/sheet1.xml" → "xl/worksheets/sheet1.xml"
        if (target.StartsWith('/'))
            return target.TrimStart('/');
        return "xl/" + target;
    }

    private static void AddLegacyDrawingHfToSheet(
        ZipArchive archive,
        string sheetPath,
        string vmlRelativeTarget,
        string relId)
    {
        XNamespace mainNs = MainNs;
        XNamespace packageRelNs = PackageRelNs;
        XNamespace relNs = RelNs;

        // Add legacyDrawingHF element to the worksheet XML.
        var worksheetXml = LoadXml(archive, sheetPath);
        worksheetXml.Root!.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        worksheetXml.Root.Add(new XElement(
            mainNs + "legacyDrawingHF",
            new XAttribute(relNs + "id", relId)));
        ReplaceXml(archive, sheetPath, worksheetXml);

        // Add the relationship entry for the worksheet.
        var relsPath = GetRelsPath(sheetPath);
        XDocument relsXml;
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is not null)
        {
            relsXml = LoadXml(relsEntry);
        }
        else
        {
            relsXml = new XDocument(new XElement(packageRelNs + "Relationships",
                new XAttribute("xmlns", packageRelNs.NamespaceName)));
        }

        relsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", relId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
            new XAttribute("Target", vmlRelativeTarget)));
        ReplaceXml(archive, relsPath, relsXml);
    }

    private static string GetRelsPath(string partPath)
    {
        var dir = Path.GetDirectoryName(partPath)?.Replace('\\', '/') ?? "";
        var file = Path.GetFileName(partPath);
        return string.IsNullOrEmpty(dir) ? $"_rels/{file}.rels" : $"{dir}/_rels/{file}.rels";
    }

    private static void EnsureDefaultContentType(XDocument contentTypesXml, string extension, string contentType)
    {
        XNamespace contentTypeNs = ContentTypeNs;
        var existing = contentTypesXml.Root!
            .Elements(contentTypeNs + "Default")
            .Any(e => string.Equals(e.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase));
        if (!existing)
            contentTypesXml.Root.AddFirst(new XElement(
                contentTypeNs + "Default",
                new XAttribute("Extension", extension),
                new XAttribute("ContentType", contentType)));
    }
}
