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
}
