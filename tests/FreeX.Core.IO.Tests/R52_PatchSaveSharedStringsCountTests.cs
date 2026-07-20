using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R52-io-sst-shared-inline-3-1: patch-save always rewrites an edited shared-string cell (t="s")
/// as an inline/literal value (XlsxFileAdapter.SourcePackageSnapshot.RewriteLiteralCellValue)
/// without ever touching xl/sharedStrings.xml, so the &lt;sst count="..."&gt; total (the
/// workbook-wide count of cell references to shared strings) goes stale by exactly one per such
/// edit. Only the count attribute is corrected here -- uniqueCount recomputation and orphan
/// &lt;si&gt; pruning both require a whole-workbook scan of every remaining t="s" cell to know
/// whether a shared-string index still has any referrer, which is intentionally out of scope for
/// this fix (see the comment on XlsxFileAdapter.SourcePackageSnapshot.DecrementSharedStringsReferenceCount).
/// </summary>
public sealed class R52_PatchSaveSharedStringsCountTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackageWithSharedString()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            // A1 and C1 reference the SAME shared string ("original value"), matching the
            // finding's repro: <sst count="2" uniqueCount="1"><si><t>original value</t></si></sst>.
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("C1").Value = "original value";
            // B1 is a plain number -- never a shared-string reference -- for the sibling
            // no-regression test to overwrite without touching xl/sharedStrings.xml at all.
            sheet.Cell("B1").Value = 7;
            workbook.SaveAs(stream);
        }

        // ClosedXML's own SaveAs does not stamp count/uniqueCount attributes onto the <sst> root at
        // all, so stamp them here to match what a real-Excel-authored file (the finding's exact
        // scenario) always carries -- this is the value patch-save must otherwise leave stale.
        var sourceBytes = stream.ToArray();
        using var packageStream = new MemoryStream();
        packageStream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml")!;
            XDocument sharedStringsXml;
            using (var entryStream = entry.Open())
                sharedStringsXml = XDocument.Load(entryStream);

            sharedStringsXml.Root!.SetAttributeValue("count", "2");
            sharedStringsXml.Root.SetAttributeValue("uniqueCount", "1");

            entry.Delete();
            var replacement = archive.CreateEntry("xl/sharedStrings.xml");
            using var replacementStream = replacement.Open();
            sharedStringsXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return packageStream.ToArray();
    }

    private static XDocument LoadSharedStringsXml(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        return XDocument.Load(entryStream);
    }

    [Fact]
    public void Save_PatchedEditOfSharedStringCell_DecrementsStaleSharedStringsCount()
    {
        var sourceBytes = CreateSourcePackageWithSharedString();

        // Sanity-check the fixture actually shares one <si> entry across both cells, as the
        // finding describes, before testing the patch-save behavior against it.
        var sourceSst = LoadSharedStringsXml(sourceBytes).Root!;
        ((int?)sourceSst.Attribute("count")).Should().Be(2, "both A1 and C1 reference the same shared string");
        ((int?)sourceSst.Attribute("uniqueCount")).Should().Be(1);

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("changed value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Confirm this actually exercised the patch-save path (not a full rebuild, which would
        // regenerate the SST cleanly and hide the bug).
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var savedSst = LoadSharedStringsXml(savedBytes).Root!;
        ((int?)savedSst.Attribute("count")).Should().Be(
            1,
            "only C1 still references the shared string after A1 was overwritten with a literal value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("changed value"));
        reloaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("original value"), "C1 must be unaffected by A1's edit");
    }

    /// <summary>
    /// Sibling no-regression case: editing a cell that was never a shared-string reference (a plain
    /// number) must leave xl/sharedStrings.xml completely untouched -- in particular its count must
    /// not be spuriously decremented for an edit that never removed a shared-string reference.
    /// </summary>
    [Fact]
    public void Save_PatchedEditOfNonSharedStringCell_LeavesSharedStringsCountUnchanged()
    {
        var sourceBytes = CreateSourcePackageWithSharedString();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        // Overwrite B1 (a plain number, never a shared-string reference) instead of touching
        // either shared-string cell.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var sourceSst = LoadSharedStringsXml(sourceBytes).Root!;
        var savedSst = LoadSharedStringsXml(savedBytes).Root!;
        ((int?)savedSst.Attribute("count")).Should().Be(
            (int?)sourceSst.Attribute("count"),
            "an edit that never overwrote a shared-string cell must not change the shared-strings count");
        ((int?)savedSst.Attribute("uniqueCount")).Should().Be((int?)sourceSst.Attribute("uniqueCount"));
    }
}
