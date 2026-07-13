using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for round-39 finding R39-io-calcchain-dependency-1, fixed in
/// XlsxFileAdapter.SourcePackageSnapshot.cs (TrySavePatchedCellValues).
///
/// The cell-patch eligibility gate (AllowsCellPatchSaveForPackage -&gt;
/// WorkbookRequiresFullSavePostProcessing) runs BEFORE any cell diff, so it can reject
/// patch-save for reasons that have nothing to do with what actually changed (e.g. the
/// workbook has a Slicer). Previously, that early-exit path called the local Fail() helper
/// with its default invalidatesCalcChain=false, so XlsxSaveDiagnostics.InvalidatesCalcChain was
/// unconditionally false for this path -- even when the triggering edit was a formula change
/// that DOES invalidate the source calcChain.xml. The stale source calcChain then survived the
/// full-rebuild fallback (CopyUnknownPackageParts copies it back unconditionally), so Excel
/// would see recalculated values/formulas alongside a pre-edit calcChain.xml.
/// </summary>
public sealed class R39_CalcChainEligibilityGateFallbackTests
{
    // ---- R39-io-calcchain-dependency-1 -------------------------------------------------------

    [Fact]
    public void Save_FormulaEditOnWorkbookWithPatchUnsafePivotFeature_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateSingleSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        // A Slicer trips WorkbookHasPatchUnsafePivotFeatures, which makes
        // WorkbookRequiresFullSavePostProcessing (and therefore the cell-patch ELIGIBILITY gate)
        // return true unconditionally -- BEFORE any cell diff runs. This reproduces the "gate
        // trips first" scenario from the finding without needing a real pivot/table/chart setup.
        // It must be present BEFORE the eligibility check is first evaluated/cached (i.e. before
        // TryPrepareLoadedPackageSnapshotForEdit), otherwise the gate would use a stale (pre-slicer)
        // cached "eligible" result instead of re-evaluating against the current workbook state.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "slicerCache1"
        });

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeFalse("the workbook now has a patch-unsafe pivot feature (a Slicer), so cell-patch " +
                     "eligibility must be rejected before any cell diff runs");
        blockReason.Should().Be("workbook_postprocessing_pivots");

        // An ordinary edit that changes a formula -- exactly the kind of change that, on the
        // normal (post-diff) block-reason path, is already recognized as calc-chain invalidating.
        var sheet = workbook.Sheets[0];
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "2+2");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("workbook_postprocessing_pivots");

        // The core regression: the eligibility-gate Fail() must not silently default
        // InvalidatesCalcChain to false just because the block happened before any diff ran.
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue(
            "the eligibility gate can't know whether formulas changed since it runs before any " +
            "diff, so it must assume the stale source calcChain.xml is no longer valid");

        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "an eligibility-gate full-save fallback must strip the stale source calcChain, " +
            "just like a post-diff structural block reason does");
    }

    [Fact]
    public void Save_PlainValueEditOnWorkbookWithPatchUnsafePivotFeature_FullSaveStripsStaleSourceCalcChain()
    {
        // Sibling no-regression case: the fix must apply uniformly regardless of what kind of edit
        // triggered the save -- even a plain (non-formula) literal value edit, which the post-diff
        // ChangesInvalidateCalcChain heuristic would NOT flag as calc-chain invalidating on its own,
        // must still be treated as invalidating when it's the eligibility gate (not the diff) that
        // blocked patch-save. The gate can't tell what changed, so it must stay conservative.
        var sourceBytes = CreateSingleSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "slicerCache1"
        });

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeFalse();
        blockReason.Should().Be("workbook_postprocessing_pivots");

        var sheet = workbook.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("workbook_postprocessing_pivots");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue(
            "the eligibility gate must conservatively invalidate the stale calcChain even for a " +
            "plain value edit, since it runs before any diff can classify the change");
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
    }

    private static byte[] CreateSingleSheetSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainEligibilityGateRegression");
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
