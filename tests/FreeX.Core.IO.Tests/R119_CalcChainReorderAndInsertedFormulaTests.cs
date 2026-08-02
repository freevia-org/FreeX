using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-119 finding: PatchBlockReasonInvalidatesCalcChain's allowlist omitted
/// full-save fallback reasons that ARE reached in ordinary use and DO invalidate a source
/// calcChain.xml -- a plain sheet reorder (no add/delete) and a brand-new formula typed into a cell
/// that was blank in the loaded source. Both used to fall through to invalidatesCalcChain = false, so
/// the pre-edit calcChain.xml (whose &lt;c i="N"&gt; sheet-index references are now stale, or which
/// simply omits the new formula cell) shipped unmodified in the saved package -- see
/// R19_CalcChainAndPatchMetadataTests.cs for the sibling fix already made for
/// change_sheet_count/change_dimension_metadata/change_cell_count_mismatch.
///
/// IMPORTANT correction vs. the originally reported mechanism: a plain sheet reorder does NOT reach
/// the per-sheet identity check that returns "change_sheet_identity_or_style_only_cells" (that check
/// runs only AFTER TryGetPatchableValueChanges' chart-source-range and pivot-source-range identity
/// checks, which perform the identical per-ordinal Sheet.Id/name comparison over every sheet -- charts
/// or no charts, pivots or no pivots -- and therefore always mismatch first on any real reorder). The
/// reason actually observed end-to-end for a plain reorder is "change_chart_source_metadata" (verified
/// below); "change_sheet_identity_or_style_only_cells" is reached only via its OTHER call site (a
/// style-only-cell bookkeeping-count mismatch, unrelated to reorder). Both new reasons are added to
/// the allowlist below since both are conservative-safe (see the existing eligibility-gate comments in
/// TrySavePatchedCellValues: "treat every ... rejection as calc-chain invalidating").
/// </summary>
public sealed class R119_CalcChainReorderAndInsertedFormulaTests
{
    // ---- unit-level: the allowlist itself ---------------------------------------------------------

    [Theory]
    [InlineData("change_chart_source_metadata")]
    [InlineData("change_pivot_source_metadata")]
    [InlineData("change_sheet_identity_or_style_only_cells")]
    [InlineData("change_inserted_cell")]
    public void PatchBlockReasonInvalidatesCalcChain_ReorderAndInsertedCellReasons_InvalidateCalcChain(string reason)
    {
        InvokePatchBlockReasonInvalidatesCalcChain(reason).Should().BeTrue(
            $"a full-save fallback triggered by '{reason}' reshapes sheet order or adds a formula cell " +
            "the source calcChain.xml does not know about, so any stale source calcChain.xml must not " +
            "survive the save");
    }

    [Theory]
    [InlineData("change_merge_metadata")]
    [InlineData("change_hyperlink_metadata")]
    [InlineData("change_comment_metadata")]
    [InlineData("change_worksheet_view_metadata")]
    public void PatchBlockReasonInvalidatesCalcChain_MetadataOnlyReasons_DoNotInvalidateCalcChain(string reason)
    {
        // Sibling no-regression check: reasons whose full-save fallback cannot touch sheet order or
        // formula membership must keep NOT invalidating the calc chain (otherwise every metadata-only
        // full-save fallback would gratuitously strip a still-valid source calcChain.xml).
        InvokePatchBlockReasonInvalidatesCalcChain(reason).Should().BeFalse(
            $"'{reason}' cannot change sheet order or formula membership, so a stale calcChain.xml risk does not apply");
    }

    private static bool InvokePatchBlockReasonInvalidatesCalcChain(string reason)
    {
        var method = FindPrivateStaticMethod("PatchBlockReasonInvalidatesCalcChain");
        return (bool)method.Invoke(null, [reason])!;
    }

    // ---- end-to-end: real Save() entry point, sheet reorder --------------------------------------

    [Fact]
    public void Save_LoadedWorkbookWithSheetsReordered_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateTwoSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        // Plain reorder: no sheet added or removed, only tab order changes (e.g. drag-reorder /
        // MoveSheetsCommand / "Move or Copy Sheet"). Sheet count and identities are unchanged, only
        // each sheet's ordinal position -- which is exactly what calcChain.xml's <c i="N"> keys off.
        workbook.MoveSheet(0, 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        // The chart-source-range baseline's per-ordinal identity check runs before the standalone
        // sheet-identity check and performs the identical comparison over EVERY sheet regardless of
        // whether it actually has any charts, so it is what actually trips on a plain reorder.
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_chart_source_metadata");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();

        // The core regression: the pre-edit calcChain.xml (whose <c i="N"> sheet-index references now
        // name the WRONG sheet after the reorder) must not survive into the saved package.
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "a sheet reorder invalidates the source calcChain (stale sheet-index references) and must strip it on full-save fallback");
    }

    private static byte[] CreateTwoSheetSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainReorderRegression");
        var sheet1 = workbook.AddSheet("Data");
        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 1), "1+1");
        workbook.AddSheet("Extra");

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        AddMinimalCalcChainPackage(source);
        source.Position = 0;
        return source.ToArray();
    }

    // ---- end-to-end: real Save() entry point, new formula in a previously-blank cell -------------

    [Fact]
    public void Save_NewFormulaTypedIntoPreviouslyBlankCell_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateSingleSheetSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        // B2 is blank in the loaded source (only A1 was occupied). Typing a brand-new formula into it
        // is exactly the "change_inserted_cell" full-save fallback path.
        var sheet = workbook.Sheets[0];
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A1+1");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_inserted_cell");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();

        // The core regression: the pre-edit calcChain.xml (which simply does not mention the newly
        // inserted formula cell) must not survive into the saved package.
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "a newly inserted formula cell invalidates the source calcChain (it omits the new cell) and must strip it on full-save fallback");
    }

    private static byte[] CreateSingleSheetSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainInsertedCellRegression");
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

    // ---- shared helpers (mirrors R19_CalcChainAndPatchMetadataTests.cs) ---------------------------

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

    private static System.Reflection.MethodInfo FindPrivateStaticMethod(string name)
    {
        var stack = new Stack<Type>();
        stack.Push(typeof(XlsxFileAdapter));
        var seen = new HashSet<Type>();
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            if (!seen.Add(type))
                continue;
            var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (method is not null)
                return method;
            foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                stack.Push(nested);
        }

        throw new MissingMethodException(
            $"Private static method '{name}' not found on XlsxFileAdapter or its nested types.");
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
