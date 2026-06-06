using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void SheetIdentityMetadata_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateSheetIdentityMetadataSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithSheetIdentityMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateSheetIdentityMetadataSourceWorkbook());
        var sourceSheets = ReadWorkbookChildElement(source, "sheets");
        var sourceHiddenSheetProperties = ReadWorksheetChildElement(source, "xl/worksheets/sheet2.xml", "sheetPr");
        var sourceVeryHiddenSheetProperties = ReadWorksheetChildElement(source, "xl/worksheets/sheet3.xml", "sheetPr");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var visibleSheet = workbook.GetSheet("Visible")!;
        visibleSheet.SetCell(new CellAddress(visibleSheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorkbookChildElement(saved, "sheets")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSheets.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "xl/worksheets/sheet2.xml", "sheetPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceHiddenSheetProperties.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "xl/worksheets/sheet3.xml", "sheetPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceVeryHiddenSheetProperties.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateSheetIdentityMetadataSourceWorkbook()
    {
        var workbook = new Workbook("SheetIdentityMetadataPatchSave");
        var visible = workbook.AddSheet("Visible");
        visible.SetCell(new CellAddress(visible.Id, 1, 1), new TextValue("visible"));

        var hidden = workbook.AddSheet("Hidden");
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden"));
        hidden.IsHidden = true;
        hidden.TabColor = new CellColor(255, 192, 0);

        var veryHidden = workbook.AddSheet("Internal");
        veryHidden.SetCell(new CellAddress(veryHidden.Id, 1, 1), new TextValue("internal"));
        veryHidden.IsHidden = true;
        veryHidden.IsVeryHidden = true;
        veryHidden.CodeName = "SheetInternal";

        return workbook;
    }

    private static XElement ReadWorksheetChildElement(Stream stream, string entryName, string localName)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(ReadPackageRootElement(stream, entryName).Element(worksheetNs + localName)!);
    }
}
