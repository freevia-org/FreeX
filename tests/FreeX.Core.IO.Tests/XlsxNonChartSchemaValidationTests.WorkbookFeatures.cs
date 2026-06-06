using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorkbookFileVersion_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFileVersionSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookFileVersion_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookFileVersionSourceWorkbook());
        var sourceFileVersion = ReadWorkbookChildElement(source, "fileVersion");
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
        ReadWorkbookChildElement(saved, "fileVersion")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceFileVersion.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateWorkbookFileVersionSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookFileVersionPatchSave")
        {
            FileVersion = new WorkbookFileVersionModel
            {
                AppName = "xl",
                LastEdited = "7",
                LowestEdited = "7",
                RupBuild = "28129"
            }
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("version"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }
}
