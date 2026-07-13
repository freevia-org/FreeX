using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R39-io-sheetview-2-1: rightToLeft is dual-tracked (modeled
/// Sheet.IsRightToLeft + captured verbatim in the load-time native-attribute bag for the primary
/// sheetView). XlsxWorksheetViewWriter correctly writes/removes the live rightToLeft attribute
/// from Sheet.IsRightToLeft, but XlsxWorksheetPrimaryViewMetadataWriter ran afterwards and blindly
/// reapplied the stale load-time bag value over it -- so a workbook loaded with rightToLeft="1"
/// always saved back with rightToLeft="1" even after the model's IsRightToLeft was flipped to
/// false (undoing the user's RTL-off toggle).
/// </summary>
public sealed class XlsxWorksheetPrimaryViewRightToLeftTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static byte[] SetRightToLeft(byte[] sourceBytes, string? rightToLeftValue)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var sheetView = worksheetXml.Root!
                .Element(worksheetNs + "sheetViews")!
                .Elements(worksheetNs + "sheetView")
                .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

            sheetView.SetAttributeValue("rightToLeft", rightToLeftValue);
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static string? ReadPrimarySheetViewAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
            ?.Attribute(attributeName)
            ?.Value;
    }

    [Fact]
    public void Save_LoadedRtlWorkbookWithRightToLeftTurnedOff_DoesNotResurrectStaleTrueFromBag()
    {
        // Regression: source loaded with rightToLeft="1", then the model's IsRightToLeft is
        // flipped to false (Excel's default -- so XlsxWorksheetViewWriter removes the attribute
        // entirely). The stale load-time bag value must not be reapplied afterwards by
        // XlsxWorksheetPrimaryViewMetadataWriter, which only runs on the FULL-save path (it is
        // never invoked by the cheap cell-patch path), so the save below must be forced onto that
        // path -- defining a new named range is an unsupported model delta for cell-patch (see
        // FreeXCleanupMED9Tests for the same technique), forcing XlsxFileAdapter.Save through the
        // ClosedXML full-rebuild + source-package-preservation path where
        // SaveSourcePackageIndependentPostProcessingMetadata (and thus this writer) actually runs.
        var sourceBytes = SetRightToLeft(CreateSourcePackage(), "1");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.IsRightToLeft.Should().BeTrue("the source file loaded with rightToLeft=\"1\"");

        sheet.IsRightToLeft = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("RightToLeftOffUnrelatedName", new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "rightToLeft")
            .Should()
            .BeNull("the live IsRightToLeft=false toggle must win over the stale load-time rightToLeft=\"1\" bag value");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.IsRightToLeft.Should().BeFalse();
    }

    [Fact]
    public void Save_LoadedRtlWorkbookWithRightToLeftLeftOn_StillRoundTripsTrue()
    {
        // Sibling already-working case: a workbook loaded with rightToLeft="1" whose model
        // IsRightToLeft is left untouched (still true) must still save back with rightToLeft="1"
        // on the same FULL-save path -- the writer-exclusion fix must not break this direction (an
        // untoggled RTL sheet still round-trips rightToLeft=1).
        var sourceBytes = SetRightToLeft(CreateSourcePackage(), "1");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.IsRightToLeft.Should().BeTrue();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("RightToLeftOnUnrelatedName", new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "rightToLeft")
            .Should()
            .Be("1");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.IsRightToLeft.Should().BeTrue();
    }
}
