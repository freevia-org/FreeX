using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // -------------------------------------------------------------------------
    // Absolute-style autoFilter refs ("$A$3:$G$25") are written by some producers.
    // The load sanitizer used to reject them via CellAddress.TryParse and strip the
    // ref attribute, after which ClosedXML's LoadAutoFilter dereferenced the missing
    // Reference and crashed the whole open with a NullReferenceException.
    // -------------------------------------------------------------------------

    [Fact]
    public void LoadWorkbook_WithAbsoluteAutoFilterRef_Loads()
    {
        using var source = CreateAutoFilterSourcePackage("$A$1:$B$3", includeSortState: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Header"));
        sheet.AutoFilter.Should().NotBeNull("the filter must survive the load, not be stripped");
    }

    [Fact]
    public void LoadWorkbook_WithUnparseableAutoFilterRef_DropsFilterAndLoads()
    {
        using var source = CreateAutoFilterSourcePackage("not-a-range", includeSortState: false);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        // A filter whose ref cannot be normalized is dropped entirely rather than
        // handed to ClosedXML ref-less (which crashes its LoadAutoFilter).
        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("Header"));
    }

    [Fact]
    public void NormalizeElement_AbsoluteRef_RewritesToPlainReference()
    {
        var autoFilter = new XElement(MainNs + "autoFilter", new XAttribute("ref", "$A$3:$G$25"));
        var worksheet = new XElement(MainNs + "worksheet", autoFilter);

        var changed = XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot(worksheet);

        changed.Should().BeTrue();
        worksheet.Element(MainNs + "autoFilter")!.Attribute("ref")!.Value.Should().Be("A3:G25");
    }

    [Fact]
    public void NormalizeElement_UnparseableRef_RemovesAutoFilterElement()
    {
        var autoFilter = new XElement(MainNs + "autoFilter", new XAttribute("ref", "garbage"));
        var worksheet = new XElement(MainNs + "worksheet", autoFilter);

        var changed = XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot(worksheet);

        changed.Should().BeTrue();
        worksheet.Element(MainNs + "autoFilter").Should().BeNull(
            "ClosedXML dereferences autoFilter@ref unconditionally, so a ref-less filter must not reach it");
    }

    [Fact]
    public void NormalizeElement_MissingRef_RemovesAutoFilterElement()
    {
        var autoFilter = new XElement(MainNs + "autoFilter");
        var worksheet = new XElement(MainNs + "worksheet", autoFilter);

        var changed = XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot(worksheet);

        changed.Should().BeTrue();
        worksheet.Element(MainNs + "autoFilter").Should().BeNull();
    }

    private static MemoryStream CreateAutoFilterSourcePackage(string autoFilterRef, bool includeSortState)
    {
        // Start with a normal single-sheet workbook, then inject an autoFilter element
        // mirroring the shape real producer files use (filterColumn + filters + sortState).
        var workbook = new Workbook("AutoFilterRefs");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("In progress"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("New"));

        var stream = Save(workbook);
        InjectWorksheetAutoFilter(stream, autoFilterRef, includeSortState);
        stream.Position = 0;
        return stream;
    }

    private static void InjectWorksheetAutoFilter(MemoryStream stream, string autoFilterRef, bool includeSortState)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument worksheetXml;
        using (var reader = entry.Open())
            worksheetXml = XDocument.Load(reader);

        var autoFilter = new XElement(
            MainNs + "autoFilter",
            new XAttribute("ref", autoFilterRef),
            new XElement(
                MainNs + "filterColumn",
                new XAttribute("colId", "0"),
                new XElement(
                    MainNs + "filters",
                    new XAttribute("blank", "1"),
                    new XElement(MainNs + "filter", new XAttribute("val", "In progress")),
                    new XElement(MainNs + "filter", new XAttribute("val", "New")))));
        if (includeSortState)
        {
            autoFilter.Add(new XElement(
                MainNs + "sortState",
                new XAttribute("ref", "A1:B3"),
                new XElement(MainNs + "sortCondition", new XAttribute("ref", "A1:A3"))));
        }

        var sheetData = worksheetXml.Root!.Element(MainNs + "sheetData")!;
        sheetData.AddAfterSelf(autoFilter);

        entry.Delete();
        var newEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var writer = newEntry.Open();
        worksheetXml.Save(writer);
    }
}
