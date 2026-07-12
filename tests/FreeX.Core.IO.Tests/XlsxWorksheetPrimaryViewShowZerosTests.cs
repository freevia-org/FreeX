using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R33-io-worksheet-props-deep-1: showZeros is dual-tracked (modeled
/// Sheet.ShowZeros + captured verbatim in the load-time native-attribute bag for the primary
/// sheetView). XlsxWorksheetViewWriter correctly writes/removes the live showZeros attribute from
/// Sheet.ShowZeros, but XlsxWorksheetPrimaryViewMetadataWriter ran afterwards and blindly
/// reapplied the stale load-time bag value over it -- so a workbook loaded with showZeros="0"
/// always saved back with showZeros="0" even after the model's ShowZeros was flipped to true.
/// </summary>
public sealed class XlsxWorksheetPrimaryViewShowZerosTests
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

    private static byte[] SetShowZeros(byte[] sourceBytes, string? showZerosValue)
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

            sheetView.SetAttributeValue("showZeros", showZerosValue);
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
    public void Save_LoadedWorkbookWithShowZerosTurnedOn_DoesNotResurrectStaleFalseFromBag()
    {
        // Regression: source loaded with showZeros="0", then the model's ShowZeros is flipped to
        // true (Excel's default -- so XlsxWorksheetViewWriter removes the attribute entirely). The
        // stale load-time bag value must not be reapplied afterwards by
        // XlsxWorksheetPrimaryViewMetadataWriter.
        var sourceBytes = SetShowZeros(CreateSourcePackage(), "0");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowZeros.Should().BeFalse("the source file loaded with showZeros=\"0\"");

        sheet.ShowZeros = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "showZeros")
            .Should()
            .BeNull("the live ShowZeros=true toggle must win over the stale load-time showZeros=\"0\" bag value");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.ShowZeros.Should().BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithShowZerosTurnedOff_StillWritesFalse()
    {
        // Sibling already-working case: a workbook loaded with the Excel default (showZeros
        // absent, i.e. true) whose model ShowZeros is then flipped to false must still get the
        // live showZeros="0" written -- the writer-exclusion fix must not break this direction.
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowZeros.Should().BeTrue();

        sheet.ShowZeros = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "showZeros")
            .Should()
            .Be("0");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.ShowZeros.Should().BeFalse();
    }
}
