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

    [Fact]
    public void WorkbookFileSharing_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFileSharingSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookFileSharing_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookFileSharingSourceWorkbook());
        var sourceFileSharing = ReadWorkbookChildElement(source, "fileSharing");
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
        ReadWorkbookChildElement(saved, "fileSharing")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceFileSharing.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WorkbookFileRecoveryProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFileRecoveryPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookFileRecoveryProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookFileRecoveryPropertiesSourceWorkbook());
        var sourceFileRecoveryProperties = ReadWorkbookChildElement(source, "fileRecoveryPr");
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
        ReadWorkbookChildElement(saved, "fileRecoveryPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceFileRecoveryProperties.ToString(SaveOptions.DisableFormatting));
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

    private static Workbook CreateWorkbookFileSharingSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookFileSharingPatchSave")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                ReadOnlyRecommended = true,
                UserName = "FreeXTest",
                ReservationPassword = "1234"
            }
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("sharing"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static Workbook CreateWorkbookFileRecoveryPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookFileRecoveryPatchSave");
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = true,
            CrashSave = true,
            DataExtractLoad = false,
            RepairLoad = false
        });
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("recovery"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }
}
