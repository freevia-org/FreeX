using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void IndexedColors_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateIndexedColorsSourceWorkbook());

        SchemaErrors(stream).Should().BeEmpty();
        ReadIndexedColors(stream)
            .Elements()
            .Should()
            .HaveCount(56);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithIndexedColors_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateIndexedColorsSourceWorkbook());
        var sourceColors = ReadIndexedColors(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadIndexedColors(saved)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceColors.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateIndexedColorsSourceWorkbook()
    {
        var workbook = new Workbook("IndexedColorsPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        workbook.IndexedColors.SetColor(5, CellColor.FromArgb(10, 20, 30));
        workbook.IndexedColors.SetColor(12, CellColor.FromArgb(200, 120, 40));
        return workbook;
    }

    private static XElement ReadIndexedColors(Stream stream)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(ReadPackageRootElement(stream, "xl/styles.xml")
            .Element(workbookNs + "colors")!
            .Element(workbookNs + "indexedColors")!);
    }
}
