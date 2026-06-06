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

    [Fact]
    public void WorkbookFunctionGroups_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFunctionGroupsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookFunctionGroups_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookFunctionGroupsSourceWorkbook());
        var sourceFunctionGroups = ReadWorkbookChildElement(source, "functionGroups");
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
        ReadWorkbookChildElement(saved, "functionGroups")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceFunctionGroups.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WorkbookProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookPropertiesSourceWorkbook());
        var sourceWorkbookProperties = ReadWorkbookChildElement(source, "workbookPr");
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
        ReadWorkbookChildElement(saved, "workbookPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookProperties.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WorkbookProtection_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookProtectionSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookProtection_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookProtectionSourceWorkbook());
        var sourceWorkbookProtection = ReadWorkbookChildElement(source, "workbookProtection");
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
        ReadWorkbookChildElement(saved, "workbookProtection")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookProtection.ToString(SaveOptions.DisableFormatting));
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

    private static Workbook CreateWorkbookFunctionGroupsSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookFunctionGroupsPatchSave")
        {
            FunctionGroups = new WorkbookFunctionGroupsModel
            {
                BuiltInGroupCount = "16",
                Groups =
                {
                    new WorkbookFunctionGroupModel
                    {
                        Name = "FreeXNativeFunctions"
                    }
                }
            }
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("functions"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static Workbook CreateWorkbookPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookPropertiesPatchSave")
        {
            Uses1904DateSystem = true,
            Properties = CreateWorkbookPropertiesMetadata()
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("properties"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static NativeXmlPreserveBag CreateWorkbookPropertiesMetadata()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("workbookPr", """<e defaultThemeVersion="166925" />""");
        return bag;
    }

    private static Workbook CreateWorkbookProtectionSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookProtectionPatchSave")
        {
            IsStructureProtected = true,
            StructureProtectionPassword = "password"
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("protection"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }
}
