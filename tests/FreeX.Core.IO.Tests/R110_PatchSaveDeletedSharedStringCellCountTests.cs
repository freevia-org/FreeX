using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R110-io-sst-delete-1: R52-io-sst-shared-inline-3-1 taught the patch-save
/// XlsxCellValuePatchKind.LiteralValue/CellStyle branches to increment
/// sharedStringReferencesRemoved (and thus decrement xl/sharedStrings.xml's &lt;sst count="..."&gt;
/// total) whenever an edit overwrites a shared-string (t="s") cell -- but the sibling
/// XlsxCellValuePatchKind.DeletedCell branch (a Delete-key / clear-contents edit, which removes the
/// worksheet &lt;c&gt; element outright via Sheet.ClearCell instead of overwriting it) never got the
/// same treatment. That left &lt;sst count&gt; permanently overstated after clearing a shared-string
/// cell through patch-save.
/// </summary>
public sealed class R110_PatchSaveDeletedSharedStringCellCountTests
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
            // A1 and C1 reference the SAME shared string ("original value"):
            // <sst count="2" uniqueCount="1"><si><t>original value</t></si></sst>.
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("C1").Value = "original value";
            // B1 is a plain number -- never a shared-string reference -- for the sibling
            // no-regression test to delete without touching xl/sharedStrings.xml at all.
            sheet.Cell("B1").Value = 7;
            workbook.SaveAs(stream);
        }

        // ClosedXML's own SaveAs does not stamp count/uniqueCount attributes onto the <sst> root at
        // all, so stamp them here to match what a real-Excel-authored file always carries -- this is
        // the value patch-save must otherwise leave stale.
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
    public void Save_PatchedDeleteOfSharedStringCell_DecrementsStaleSharedStringsCount()
    {
        var sourceBytes = CreateSourcePackageWithSharedString();

        var sourceSst = LoadSharedStringsXml(sourceBytes).Root!;
        ((int?)sourceSst.Attribute("count")).Should().Be(2, "both A1 and C1 reference the same shared string");
        ((int?)sourceSst.Attribute("uniqueCount")).Should().Be(1);

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        // The real Delete-key / clear-contents entry point: Sheet.ClearCell removes the cell from
        // the sheet's live model entirely (not merely overwritten with a blank value), which is what
        // classifies the resulting patch as XlsxCellValuePatchKind.DeletedCell.
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 1));

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
            "only C1 still references the shared string after A1's shared-string reference was deleted");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.GetCell(1, 1).Should().BeNull("A1 was cleared");
        reloaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("original value"), "C1 must be unaffected by A1's deletion");
    }

    /// <summary>
    /// Sibling no-regression case: deleting a cell that was never a shared-string reference (a plain
    /// number) must leave xl/sharedStrings.xml completely untouched -- in particular its count must
    /// not be spuriously decremented for a delete that never removed a shared-string reference.
    /// </summary>
    [Fact]
    public void Save_PatchedDeleteOfNonSharedStringCell_LeavesSharedStringsCountUnchanged()
    {
        var sourceBytes = CreateSourcePackageWithSharedString();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        // Delete B1 (a plain number, never a shared-string reference) instead of touching either
        // shared-string cell.
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var sourceSst = LoadSharedStringsXml(sourceBytes).Root!;
        var savedSst = LoadSharedStringsXml(savedBytes).Root!;
        ((int?)savedSst.Attribute("count")).Should().Be(
            (int?)sourceSst.Attribute("count"),
            "a delete that never removed a shared-string cell must not change the shared-strings count");
        ((int?)savedSst.Attribute("uniqueCount")).Should().Be((int?)sourceSst.Attribute("uniqueCount"));
    }
}
