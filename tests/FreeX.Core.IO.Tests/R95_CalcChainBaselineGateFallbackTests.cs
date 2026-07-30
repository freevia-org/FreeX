using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for the baseline-gate sibling of round-39 finding R39-io-calcchain-dependency-1
/// (fixed in XlsxFileAdapter.SourcePackageSnapshot.cs, TrySavePatchedCellValues).
///
/// Round 39 fixed the cell-patch ELIGIBILITY gate (AllowsCellPatchSaveForPackage ->
/// WorkbookRequiresFullSavePostProcessing): that gate runs BEFORE any cell diff, so a Fail() there
/// must conservatively assume the stale source calcChain.xml is invalidated (it cannot know
/// whether the triggering edit touched a formula).
///
/// The BASELINE gate (TryEnsureCellPatchBaseline failing, or CellPatchBaseline still null after
/// that) is structurally identical -- it also runs strictly before TryGetPatchableValueChanges
/// (the actual diff) -- but its two Fail() call sites omitted invalidatesCalcChain, silently
/// taking the unsafe default of false. Baseline creation can fail for reasons entirely orthogonal
/// to what the user edited (cell-count limit, unreadable worksheet-path map, chart/pivot
/// source-range indexing, a missing sheet path, ambiguous source cell styles, or an unexpected
/// exception), so a formula edit landing on a baseline-unavailable full-save fallback would ship a
/// stale calcChain.xml next to freshly recalculated formula cells -- exactly the corruption class
/// R39 eliminated for the sibling gate.
/// </summary>
public sealed class R95_CalcChainBaselineGateFallbackTests
{
    [Fact]
    public void Save_FormulaEditWithPivotCacheReferencingUnknownSourceSheet_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateSingleSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        // Add a pivot cache directly to the in-memory model, referencing a source sheet name that
        // does not exist in this workbook. Its shape satisfies every condition
        // WorkbookHasPatchUnsafePivotFeatures / IsPatchUnsafePivotCache checks (WorksheetRange
        // source, non-empty sheet/reference, no table name, no connection, not OLAP) -- these two
        // methods use IDENTICAL conditions -- so the cell-patch ELIGIBILITY gate does NOT reject
        // it (WorkbookRequiresFullSavePostProcessing returns false; no sheet has a PivotTable, so
        // PackageAllowsCellPatchSave's own pivot-package-path check is skipped too). Only the
        // BASELINE builder (XlsxPivotSourceRangeIndex.TryCreate -> TryGetPivotSourceSheetId) looks
        // up SourceSheetName against the CURRENT workbook's sheets and fails to find it, tripping
        // "baseline_pivot_source_model" -- a block reason wholly unrelated to the formula edit
        // below. This isolates the baseline gate (not the already-fixed eligibility gate) as the
        // one that trips first, reproducing the finding's "gate trips before any cell diff runs"
        // scenario for TryEnsureCellPatchBaseline specifically.
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "GhostSheetThatDoesNotExist",
            SourceReference = "A1:B2"
        });

        // An ordinary edit that changes a formula -- exactly the kind of change that, on the
        // normal (post-diff) block-reason path, is already recognized as calc-chain invalidating.
        var sheet = workbook.Sheets[0];
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "2+2");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("baseline_pivot_source_model");

        // The core regression: the baseline-gate Fail() must not silently default
        // InvalidatesCalcChain to false just because the block happened before any diff ran.
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue(
            "the baseline gate can't know whether formulas changed since it runs before any diff " +
            "(TryGetPatchableValueChanges), so it must assume the stale source calcChain.xml is no " +
            "longer valid, exactly like the sibling eligibility gate");

        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "a baseline-gate full-save fallback must strip the stale source calcChain, just like " +
            "the eligibility-gate fallback and a post-diff structural block reason both do");
    }

    [Fact]
    public void Save_PlainValueEditWithPivotCacheReferencingUnknownSourceSheet_FullSaveStripsStaleSourceCalcChain()
    {
        // Sibling no-regression case: the fix must apply uniformly regardless of what kind of edit
        // triggered the save -- even a plain (non-formula) literal value edit, which the post-diff
        // ChangesInvalidateCalcChain heuristic would NOT flag as calc-chain invalidating on its own,
        // must still be treated as invalidating when it's the baseline gate (not the diff) that
        // blocked patch-save. The gate can't tell what changed, so it must stay conservative.
        var sourceBytes = CreateSingleSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "GhostSheetThatDoesNotExist",
            SourceReference = "A1:B2"
        });

        var sheet = workbook.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("baseline_pivot_source_model");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue(
            "the baseline gate must conservatively invalidate the stale calcChain even for a plain " +
            "value edit, since it runs before any diff can classify the change");
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
    }

    private static byte[] CreateSingleSheetSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainBaselineGateRegression");
        var sheet1 = workbook.AddSheet("Data");
        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 1), "1+1");

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        AddMinimalCalcChainPackage(source);
        source.Position = 0;
        return source.ToArray();
    }

    private static void AddMinimalCalcChainPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace calcNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/calcChain.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            ReplacePackageXml(archive, "xl/calcChain.xml", new XDocument(
                new XElement(
                    calcNs + "calcChain",
                    new XElement(calcNs + "c", new XAttribute("r", "A1"), new XAttribute("i", "1")))));

            const string workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive, workbookRelsPath);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXCalcChain"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                new XAttribute("Target", "calcChain.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
        }

        packageStream.Position = 0;
    }

    private static bool PackageHasEntry(byte[] packageBytes, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        return archive.GetEntry(path) is not null;
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path)
    {
        using var entryStream = archive.GetEntry(path)!.Open();
        return XDocument.Load(entryStream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var entryStream = entry.Open();
        document.Save(entryStream);
    }
}
