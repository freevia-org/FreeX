using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorksheetHyperlinks_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetHyperlinksSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetHyperlinks_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetHyperlinksSourceWorkbook());
        var sourceHyperlinks = ReadWorksheetChildElement(source, "hyperlinks");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "hyperlinks")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceHyperlinks.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateWorksheetHyperlinksSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetHyperlinksPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        var externalAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(externalAddress, new TextValue("Open docs"));
        sheet.Hyperlinks[externalAddress] = "https://example.com/docs";
        sheet.HyperlinkMetadata[externalAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open documentation",
            "");

        var internalAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(internalAddress, new TextValue("Jump inside"));
        sheet.Hyperlinks[internalAddress] = "Data!B2";
        sheet.HyperlinkMetadata[internalAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to details",
            "Data!B2");

        return workbook;
    }
}
