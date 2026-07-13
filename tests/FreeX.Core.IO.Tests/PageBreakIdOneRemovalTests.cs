using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 37 (R37-io-page-breaks-print-2-1): a manual page break placed right after row 1 / column A
/// (OOXML brk id="1") must be removable by the user like any other page break. Previously,
/// XlsxWorksheetMetadataPreserver.MergeWorksheetBreaks treated id=1 as structurally "unsupported"
/// (the same bucket as truly unaddressable ids like id=0) and always resurrected it verbatim from the
/// pristine source package, regardless of whether the live model still had the break.
/// </summary>
public class PageBreakIdOneRemovalTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void XlsxAdapter_RemovingBreakAfterRowOne_IsNotResurrectedOnSave()
    {
        var (loaded, adapter) = LoadWorkbookWithInjectedBreaks(rowBreakIds: [1], columnBreakIds: [1]);
        var loadedSheet = loaded.GetSheetAt(0);

        // Sanity check: the live model can in fact represent a break at id=1 (this is what makes the
        // bug possible -- if it could never be modeled, unconditional retention would be correct).
        loadedSheet.RowPageBreaks.Should().Contain(1u);
        loadedSheet.ColumnPageBreaks.Should().Contain(1u);

        // Simulate the user clearing all page breaks (e.g. "Reset All Page Breaks"), then editing the
        // sheet, then saving -- exactly the failure scenario from the finding.
        loadedSheet.RowPageBreaks.Clear();
        loadedSheet.ColumnPageBreaks.Clear();
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var (rowBreakIds, columnBreakIds) = ReadSavedBreakIds(saved);

        rowBreakIds.Should().NotContain("1", "the user removed the break after row 1 and it must not be resurrected");
        columnBreakIds.Should().NotContain("1", "the user removed the break after column A and it must not be resurrected");
    }

    [Fact]
    public void XlsxAdapter_RemovingBreakAfterRowOne_StillKeepsOtherBreaks()
    {
        // Sibling no-regression case: removing the id=1 break must not affect an unrelated,
        // still-present break elsewhere on the same sheet.
        var (loaded, adapter) = LoadWorkbookWithInjectedBreaks(rowBreakIds: [1, 10], columnBreakIds: [1, 8]);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.RowPageBreaks.Should().Contain([1u, 10u]);
        loadedSheet.ColumnPageBreaks.Should().Contain([1u, 8u]);

        loadedSheet.RowPageBreaks.Remove(1u).Should().BeTrue();
        loadedSheet.ColumnPageBreaks.Remove(1u).Should().BeTrue();
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var (rowBreakIds, columnBreakIds) = ReadSavedBreakIds(saved);

        rowBreakIds.Should().NotContain("1");
        rowBreakIds.Should().Contain("10", "the still-present row break must survive the save");
        columnBreakIds.Should().NotContain("1");
        columnBreakIds.Should().Contain("8", "the still-present column break must survive the save");
    }

    private static (Workbook Loaded, XlsxFileAdapter Adapter) LoadWorkbookWithInjectedBreaks(
        IReadOnlyCollection<int> rowBreakIds,
        IReadOnlyCollection<int> columnBreakIds)
    {
        var workbook = new Workbook("PageBreakIdOne");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page breaks"));

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        InjectPageBreaks(source, rowBreakIds, columnBreakIds);

        source.Position = 0;
        var loaded = adapter.Load(source);
        return (loaded, adapter);
    }

    private static void InjectPageBreaks(
        MemoryStream packageStream,
        IReadOnlyCollection<int> rowBreakIds,
        IReadOnlyCollection<int> columnBreakIds)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(entry);
            var root = worksheetXml.Root!;
            root.Elements(WorksheetNs + "rowBreaks").Remove();
            root.Elements(WorksheetNs + "colBreaks").Remove();

            root.Add(
                new XElement(
                    WorksheetNs + "rowBreaks",
                    new XAttribute("count", rowBreakIds.Count.ToString()),
                    new XAttribute("manualBreakCount", rowBreakIds.Count.ToString()),
                    rowBreakIds.Select(id => new XElement(
                        WorksheetNs + "brk",
                        new XAttribute("id", id),
                        new XAttribute("max", "16383"),
                        new XAttribute("man", "1")))),
                new XElement(
                    WorksheetNs + "colBreaks",
                    new XAttribute("count", columnBreakIds.Count.ToString()),
                    new XAttribute("manualBreakCount", columnBreakIds.Count.ToString()),
                    columnBreakIds.Select(id => new XElement(
                        WorksheetNs + "brk",
                        new XAttribute("id", id),
                        new XAttribute("max", "1048575"),
                        new XAttribute("man", "1")))));

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var stream = newEntry.Open();
            worksheetXml.Save(stream);
        }

        packageStream.Position = 0;
    }

    private static (List<string> RowBreakIds, List<string> ColumnBreakIds) ReadSavedBreakIds(MemoryStream saved)
    {
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);

        var rowBreakIds = worksheetXml.Root!.Element(WorksheetNs + "rowBreaks")?
            .Elements(WorksheetNs + "brk")
            .Select(element => (string?)element.Attribute("id"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList() ?? [];
        var columnBreakIds = worksheetXml.Root!.Element(WorksheetNs + "colBreaks")?
            .Elements(WorksheetNs + "brk")
            .Select(element => (string?)element.Attribute("id"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList() ?? [];

        return (rowBreakIds, columnBreakIds);
    }
}
