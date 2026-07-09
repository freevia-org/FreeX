using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-19 findings R19-calcchain-detail-1, R19-calcchain-detail-3 and
/// R19-cell-image-richvalue-1, all fixed in XlsxFileAdapter.SourcePackageSnapshot.cs.
/// </summary>
public sealed class R19_sourcepkg_snapshot_Tests
{
    // ---- R19-calcchain-detail-1 / R19-calcchain-detail-3 -----------------------------------------
    // PatchBlockReasonInvalidatesCalcChain previously only recognized formula-related block reasons.
    // A sheet add/delete ("change_sheet_count") or a structural row/column edit that falls back to a
    // full save ("change_dimension_metadata" / "change_cell_count_mismatch") must ALSO invalidate the
    // stale source calcChain.xml, otherwise the pre-edit calcChain (wrong sheet indexes / cell refs)
    // ships in the saved file and triggers Excel's repair-on-open dialog.

    [Theory]
    [InlineData("change_sheet_count")]
    [InlineData("change_dimension_metadata")]
    [InlineData("change_cell_count_mismatch")]
    public void PatchBlockReasonInvalidatesCalcChain_StructuralBlockReasons_InvalidateCalcChain(string reason)
    {
        InvokePatchBlockReasonInvalidatesCalcChain(reason).Should().BeTrue(
            $"a full-save fallback triggered by '{reason}' reshapes sheet indexes/cell addresses, " +
            "so any stale source calcChain.xml must not survive the save");
    }

    private static bool InvokePatchBlockReasonInvalidatesCalcChain(string reason)
    {
        var method = FindPrivateStaticMethod("PatchBlockReasonInvalidatesCalcChain");
        return (bool)method.Invoke(null, [reason])!;
    }

    [Fact]
    public void Save_LoadedWorkbookWithSheetDeleted_FullSaveStripsStaleSourceCalcChain()
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

        workbook.RemoveSheet(workbook.Sheets[1].Id).Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_sheet_count");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();

        // The core regression: the pre-edit calcChain.xml (whose <c i="N"> sheet-index references are now
        // stale after the sheet delete) must not survive into the saved package.
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "a sheet-count change invalidates the source calcChain and must strip it on full-save fallback");
    }

    private static byte[] CreateTwoSheetSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainSheetCountRegression");
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

    // ---- R19-cell-image-richvalue-1 --------------------------------------------------------------
    // RewriteLiteralCellValue (the fast incremental cell-patch rewrite) must clear a stale vm/cm
    // rich-value metadata index when a rich-value placeholder cell (t="e" backed by vm/cm pointing
    // into xl/metadata.xml) has its literal value overwritten by the user.

    [Fact]
    public void RewriteLiteralCellValue_OnRichValuePlaceholderCell_ClearsStaleValueMetadataIndex()
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = new XElement(
            worksheetNs + "c",
            new XAttribute("r", "B2"),
            new XAttribute("t", "e"),
            new XAttribute("vm", "1"),
            new XAttribute("cm", "1"),
            new XElement(worksheetNs + "v", "#VALUE!"));

        InvokeRewriteLiteralCellValue(cell, worksheetNs, new NumberValue(42));

        cell.Attribute("vm").Should().BeNull("the vm index pointed at rich-value metadata for the OLD value");
        cell.Attribute("cm").Should().BeNull("the cm index pointed at cell metadata for the OLD value");
        cell.Attribute("t").Should().BeNull("a plain number has no type attribute");
        double.Parse(cell.Element(worksheetNs + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .Be(42d);
    }

    [Fact]
    public void RewriteLiteralCellValue_OnPlainCellWithUnrelatedMetadataIndex_LeavesItUntouched()
    {
        // A cell whose vm/cm attribute is NOT backed by a t="e" rich-value placeholder (e.g. a dynamic-array
        // XLDAPR cell-metadata marker) is a different metadataType and is intentionally left alone by this
        // narrower, package-read-free heuristic -- only the specific rich-value placeholder shape is cleared.
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = new XElement(
            worksheetNs + "c",
            new XAttribute("r", "A1"),
            new XAttribute("cm", "1"),
            new XElement(worksheetNs + "v", "1"));

        InvokeRewriteLiteralCellValue(cell, worksheetNs, new NumberValue(2));

        cell.Attribute("cm")!.Value.Should().Be("1");
        double.Parse(cell.Element(worksheetNs + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .Be(2d);
    }

    private static void InvokeRewriteLiteralCellValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
    {
        var method = FindPrivateStaticMethod("RewriteLiteralCellValue");
        method.Invoke(null, [cell, worksheetNs, value, null]);
    }

    // The two methods under test are private static members declared on XlsxFileAdapter (or one of its
    // private nested helper types), so a bare typeof(XlsxFileAdapter).GetMethod(...) can return null when the
    // method lives on a nested type. Search XlsxFileAdapter and all of its nested types recursively.
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

    // ---- shared helpers ----------------------------------------------------------------------------

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
