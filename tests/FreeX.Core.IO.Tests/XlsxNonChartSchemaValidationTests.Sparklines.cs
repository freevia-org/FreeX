using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void Sparklines_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateSparklineSourceWorkbook());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        worksheetXml.Root!
            .Elements()
            .Last()
            .Name.LocalName
            .Should()
            .Be("extLst");

        var sparklineGroups = worksheetXml.Descendants()
            .Where(element => element.Name.LocalName == "sparklineGroup")
            .ToList();
        sparklineGroups.Select(group => group.Attribute("type")?.Value)
            .Should()
            .BeEquivalentTo("line", "column", "stacked");

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithSparklines_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateSparklineSourceWorkbook());
        var sourceExtensionList = ReadWorksheetChildElement(source, "extLst");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertSparklineModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 7), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "extLst")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExtensionList.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloadedWorkbook = adapter.Load(saved);
        AssertSparklineModel(reloadedWorkbook.GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetExtensionListForSchemaValidity()
    {
        using var source = Save(CreateSparklineSourceWorkbook());
        var sourceUri = SetWorksheetExtensionListInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 7), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        SchemaErrors(saved).Should().BeEmpty();

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var extensionList = ReadWorksheetChildElement(saved, "extLst");
        extensionList.Attribute("customWorksheetExtLstFlag").Should().BeNull();
        extensionList.Element(worksheetNs + "nativeWorksheetExtLstChild").Should().BeNull();

        var extension = extensionList
            .Elements(worksheetNs + "ext")
            .Should()
            .ContainSingle()
            .Subject;
        extension.Attribute("uri")!.Value.Should().Be(sourceUri);
        extension.Attribute("customWorksheetExtFlag").Should().BeNull();
        extension.ToString(SaveOptions.DisableFormatting).Should().Contain("sparklineGroups");
    }

    private static Workbook CreateSparklineSourceWorkbook()
    {
        var workbook = new Workbook("SparklinePatchSave");
        var sheet = workbook.AddSheet("Spark Data");

        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 5; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
        }

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 5),
            Location = new CellAddress(sheet.Id, 1, 6),
            Kind = SparklineKind.Line
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 2, 1, 2, 5),
            Location = new CellAddress(sheet.Id, 2, 6),
            Kind = SparklineKind.Column
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 3, 1, 3, 5),
            Location = new CellAddress(sheet.Id, 3, 6),
            Kind = SparklineKind.WinLoss
        });

        return workbook;
    }

    private static void AssertSparklineModel(Sheet sheet)
    {
        sheet.Sparklines
            .Select(sparkline => (sparkline.Kind, sparkline.DataRange, sparkline.Location))
            .Should()
            .Equal(
                (SparklineKind.Line, Range(sheet, 1, 1, 1, 5), new CellAddress(sheet.Id, 1, 6)),
                (SparklineKind.Column, Range(sheet, 2, 1, 2, 5), new CellAddress(sheet.Id, 2, 6)),
                (SparklineKind.WinLoss, Range(sheet, 3, 1, 3, 5), new CellAddress(sheet.Id, 3, 6)));
    }

    private static string SetWorksheetExtensionListInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var extensionList = worksheetXml.Root!.Element(worksheetNs + "extLst")!;
        extensionList.SetAttributeValue("customWorksheetExtLstFlag", "removed");
        extensionList.Add(new XElement(worksheetNs + "nativeWorksheetExtLstChild"));

        var extension = extensionList.Element(worksheetNs + "ext")!;
        var uri = extension.Attribute("uri")!.Value;
        extension.SetAttributeValue("uri", $" {uri} ");
        extension.SetAttributeValue("customWorksheetExtFlag", "removed");
        extensionList.Add(new XElement(worksheetNs + "ext", new XAttribute("uri", " ")));
        extensionList.Add(new XElement(worksheetNs + "ext", new XAttribute("uri", uri)));
        worksheetXml.Root.Add(new XElement(
            worksheetNs + "extLst",
            new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-WORKSHEET-EXTLST}"))));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        return uri;
    }
}
