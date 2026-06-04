using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxLoadedWorkbookPatchSaveTests
{
    [Fact]
    public void Save_LoadedWorkbookWithExistingLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("  patched value  "));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("  patched value  ");
        ReadCellTextSpaceMode(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("preserve");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("  patched value  "));
    }

    [Fact]
    public void Save_LoadedWorkbookWithWorksheetMetadataEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowGridlines = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved).GetSheetAt(0);
        reloaded.ShowGridlines.Should().BeFalse();
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            sheet.Cell("C3").Value = true;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static byte[] ReadPackageEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string? ReadCellText(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        if (string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal))
            return cell.Element(ns + "is")?.Element(ns + "t")?.Value;

        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellTextSpaceMode(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "is")?.Element(ns + "t")?.Attribute(XNamespace.Xml + "space")?.Value;
    }

    private static XElement ReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .Single(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));

        return cell;
    }
}
