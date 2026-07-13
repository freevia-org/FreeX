using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    private const string WorkbookViewExtensionUri = "{FREEX-WORKBOOK-VIEW-EXT}";
    private const string AdditionalWorkbookViewExtensionUri = "{FREEX-ADDITIONAL-WORKBOOK-VIEW-EXT}";
    private const string CustomWorkbookViewExtensionUri = "{FREEX-CUSTOM-WORKBOOK-VIEW-EXT}";

    [Fact]
    public void WorkbookFileVersion_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFileVersionSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookFileVersion_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorkbookFileVersionSourceWorkbook();
        workbook.FileVersion!.NativeAttributes["customVersionFlag"] = "removed";

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var fileVersion = ReadWorkbookChildElement(saved, "fileVersion");
        fileVersion.Attribute("appName")!.Value.Should().Be("xl");
        fileVersion.Attribute("customVersionFlag").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileVersion.Should().BeEquivalentTo(new WorkbookFileVersionModel
        {
            AppName = "xl",
            LastEdited = "7",
            LowestEdited = "7",
            RupBuild = "28129"
        });
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
        AssertWorkbookFileVersionModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookFileVersionModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookFileVersionForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFileVersionSourceWorkbook());
        SetWorkbookFileVersionInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var fileVersion = ReadWorkbookChildElement(saved, "fileVersion");
        fileVersion.Attribute("appName")!.Value.Should().Be("xl");
        fileVersion.Attribute("customVersionFlag").Should().BeNull();
        fileVersion.Element(fileVersion.Name.Namespace + "nativeFileVersionChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileVersion.Should().BeEquivalentTo(new WorkbookFileVersionModel
        {
            AppName = "xl",
            LastEdited = "7",
            LowestEdited = "7",
            RupBuild = "28129"
        });
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
        AssertWorkbookFileSharingModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookFileSharingModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookFileSharingForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFileSharingSourceWorkbook());
        SetWorkbookFileSharingInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var fileSharing = ReadWorkbookChildElement(saved, "fileSharing");
        fileSharing.Attribute("readOnlyRecommended").Should().BeNull();
        fileSharing.Attribute("reservationPassword").Should().BeNull();
        fileSharing.Attribute("revisionsPassword").Should().BeNull();
        fileSharing.Attribute("hashValue").Should().BeNull();
        fileSharing.Attribute("saltValue").Should().BeNull();
        fileSharing.Attribute("customFileSharingFlag").Should().BeNull();
        fileSharing.Attribute("spinCount").Should().BeNull();
        fileSharing.Element(fileSharing.Name.Namespace + "nativeFileSharingChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            UserName = "FreeXTest"
        });
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorkbookFileSharingForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFileSharingSourceWorkbook());
        SetWorkbookFileSharingInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var fileSharing = ReadWorkbookChildElement(saved, "fileSharing");
        fileSharing.Attribute("reservationPassword").Should().BeNull();
        fileSharing.Attribute("revisionsPassword").Should().BeNull();
        fileSharing.Attribute("hashValue").Should().BeNull();
        fileSharing.Attribute("saltValue").Should().BeNull();
        fileSharing.Attribute("customFileSharingFlag").Should().BeNull();
        fileSharing.Attribute("spinCount").Should().BeNull();
        fileSharing.Element(fileSharing.Name.Namespace + "nativeFileSharingChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = false,
            UserName = "FreeXTest"
        });
    }

    [Fact]
    public void WorkbookFileRecoveryProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFileRecoveryPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookFileRecoveryProperties_DoesNotMarkFreeXAuthoredWorkbookAsRepairLoaded()
    {
        using var saved = Save(CreateAuthoredWorkbookFileRecoveryRepairLoadSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var fileRecoveryPr = ReadWorkbookChildElement(saved, "fileRecoveryPr");
        fileRecoveryPr.Attribute("autoRecover")!.Value.Should().Be("1");
        fileRecoveryPr.Attribute("repairLoad").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileRecoveryProperties.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new WorkbookFileRecoveryPropertiesModel
            {
                AutoRecover = true
            });
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
        AssertWorkbookFileRecoveryPropertiesModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookFileRecoveryPropertiesModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookFileRecoveryPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFileRecoveryPropertiesSourceWorkbook());
        SetWorkbookFileRecoveryInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var fileRecoveryPr = ReadWorkbookChildElement(saved, "fileRecoveryPr");
        fileRecoveryPr.Attribute("autoRecover").Should().BeNull();
        fileRecoveryPr.Attribute("crashSave").Should().BeNull();
        fileRecoveryPr.Attribute("dataExtractLoad").Should().BeNull();
        fileRecoveryPr.Attribute("repairLoad").Should().BeNull();
        fileRecoveryPr.Attribute("customRecoveryFlag").Should().BeNull();
        fileRecoveryPr.Element(fileRecoveryPr.Name.Namespace + "nativeRecoveryChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileRecoveryProperties.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new WorkbookFileRecoveryPropertiesModel());
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorkbookFileRecoveryPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFileRecoveryPropertiesSourceWorkbook());
        SetWorkbookFileRecoveryInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var fileRecoveryPr = ReadWorkbookChildElement(saved, "fileRecoveryPr");
        fileRecoveryPr.Attribute("customRecoveryFlag").Should().BeNull();
        fileRecoveryPr.Element(fileRecoveryPr.Name.Namespace + "nativeRecoveryChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FileRecoveryProperties.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new WorkbookFileRecoveryPropertiesModel
            {
                AutoRecover = false,
                CrashSave = false,
                DataExtractLoad = false,
                RepairLoad = false
            });
    }

    [Fact]
    public void WorkbookFunctionGroups_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookFunctionGroupsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookFunctionGroups_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorkbookFunctionGroupsSourceWorkbook();
        workbook.FunctionGroups!.NativeAttributes["customFunctionGroupsFlag"] = "removed";
        workbook.FunctionGroups.Groups[0].NativeAttributes["customFunctionGroupFlag"] = "removed";

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var functionGroups = ReadWorkbookChildElement(saved, "functionGroups");
        functionGroups.Attribute("builtInGroupCount")!.Value.Should().Be("16");
        functionGroups.Attribute("customFunctionGroupsFlag").Should().BeNull();
        var functionGroup = functionGroups.Element(functionGroups.Name.Namespace + "functionGroup")!;
        functionGroup.Attribute("name")!.Value.Should().Be("FreeXNativeFunctions");
        functionGroup.Attribute("customFunctionGroupFlag").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FunctionGroups.Should().BeEquivalentTo(new WorkbookFunctionGroupsModel
        {
            BuiltInGroupCount = "16",
            Groups =
            {
                new WorkbookFunctionGroupModel
                {
                    Name = "FreeXNativeFunctions"
                }
            }
        });
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
        AssertWorkbookFunctionGroupsModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookFunctionGroupsModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookFunctionGroupsForSchemaValidity()
    {
        using var source = Save(CreateWorkbookFunctionGroupsSourceWorkbook());
        SetWorkbookFunctionGroupsInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var functionGroups = ReadWorkbookChildElement(saved, "functionGroups");
        functionGroups.Attribute("builtInGroupCount").Should().BeNull();
        functionGroups.Attribute("customFunctionGroupsFlag").Should().BeNull();
        functionGroups.Element(functionGroups.Name.Namespace + "nativeFunctionGroupsChild").Should().BeNull();

        var functionGroup = functionGroups.Element(functionGroups.Name.Namespace + "functionGroup")!;
        functionGroup.Attribute("name")!.Value.Should().Be("FreeXNativeFunctions");
        functionGroup.Attribute("customFunctionGroupFlag").Should().BeNull();
        functionGroup.Element(functionGroup.Name.Namespace + "nativeFunctionGroupChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.FunctionGroups.Should().BeEquivalentTo(new WorkbookFunctionGroupsModel
        {
            Groups =
            {
                new WorkbookFunctionGroupModel
                {
                    Name = "FreeXNativeFunctions"
                }
            }
        });
    }

    [Fact]
    public void WorkbookProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookProperties_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorkbookPropertiesSourceWorkbook();
        workbook.Properties = CreateWorkbookPropertiesMetadataWithInvalidXml();

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var workbookPr = ReadWorkbookChildElement(saved, "workbookPr");
        workbookPr.Attribute("date1904")!.Value.Should().Be("1");
        workbookPr.Attribute("defaultThemeVersion")!.Value.Should().Be("166925");
        workbookPr.Attribute("customWorkbookPrFlag").Should().BeNull();
        workbookPr.Element(workbookPr.Name.Namespace + "nativeWorkbookPrChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.Uses1904DateSystem.Should().BeTrue();
        reloaded.Properties!.Get("workbookPr").Should().Contain("defaultThemeVersion=\"166925\"");
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
        AssertWorkbookPropertiesModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookPropertiesModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookPropertiesSourceWorkbook());
        SetWorkbookPropertiesInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var workbookPr = ReadWorkbookChildElement(saved, "workbookPr");
        workbookPr.Attribute("date1904").Should().BeNull();
        workbookPr.Attribute("showObjects").Should().BeNull();
        workbookPr.Attribute("updateLinks").Should().BeNull();
        workbookPr.Attribute("defaultThemeVersion").Should().BeNull();
        workbookPr.Attribute("customWorkbookPrFlag").Should().BeNull();
        workbookPr.Element(workbookPr.Name.Namespace + "nativeWorkbookPrChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.Uses1904DateSystem.Should().BeFalse();
        reloaded.Properties!.Get("workbookPr").Should().Contain("codeName=\"ThisWorkbook\"");
        reloaded.Properties.Get("workbookPr").Should().NotContain("customWorkbookPrFlag");
    }

    [Fact]
    public void WorkbookProtection_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookProtectionSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookProtection_SanitizesInvalidAttributesForSchemaValidity()
    {
        var workbook = CreateWorkbookProtectionSourceWorkbook();
        workbook.ProtectionMetadata = CreateWorkbookProtectionMetadataWithInvalidXml();

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var protection = ReadWorkbookChildElement(saved, "workbookProtection");
        protection.Attribute("lockStructure")!.Value.Should().Be("1");
        protection.Attribute("workbookPassword")!.Value.Should().Be("83AF");
        protection.Attribute("lockWindows")!.Value.Should().Be("1");
        protection.Attribute("workbookAlgorithmName")!.Value.Should().Be("SHA-512");
        protection.Attribute("workbookHashValue")!.Value.Should().Be("AQIDBA==");
        protection.Attribute("workbookSaltValue")!.Value.Should().Be("BQYHCA==");
        protection.Attribute("workbookSpinCount")!.Value.Should().Be("100000");
        protection.Attribute("algorithmName").Should().BeNull();
        protection.Attribute("hashValue").Should().BeNull();
        protection.Attribute("saltValue").Should().BeNull();
        protection.Attribute("spinCount").Should().BeNull();
        protection.Attribute("customWorkbookProtectionFlag").Should().BeNull();
        protection.Element(protection.Name.Namespace + "nativeWorkbookProtectionChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.IsStructureProtected.Should().BeTrue();
        reloaded.StructureProtectionPassword.Should().Be("83AF");
        reloaded.ProtectionMetadata!.Get("workbookProtection").Should().Contain("workbookAlgorithmName=\"SHA-512\"");
        reloaded.ProtectionMetadata.Get("workbookProtection").Should().NotContain("customWorkbookProtectionFlag");
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
        AssertWorkbookProtectionModel(workbook);

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookProtectionModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookProtectionForSchemaValidity()
    {
        using var source = Save(CreateWorkbookProtectionSourceWorkbook());
        SetWorkbookProtectionInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var protection = ReadWorkbookChildElement(saved, "workbookProtection");
        protection.Attribute("lockStructure").Should().BeNull();
        protection.Attribute("lockWindows").Should().BeNull();
        protection.Attribute("lockRevision").Should().BeNull();
        protection.Attribute("workbookPassword").Should().BeNull();
        protection.Attribute("revisionsPassword").Should().BeNull();
        protection.Attribute("workbookHashValue").Should().BeNull();
        protection.Attribute("workbookSaltValue").Should().BeNull();
        protection.Attribute("workbookSpinCount").Should().BeNull();
        protection.Attribute("revisionsHashValue").Should().BeNull();
        protection.Attribute("revisionsSaltValue").Should().BeNull();
        protection.Attribute("revisionsSpinCount").Should().BeNull();
        protection.Attribute("algorithmName").Should().BeNull();
        protection.Attribute("hashValue").Should().BeNull();
        protection.Attribute("saltValue").Should().BeNull();
        protection.Attribute("spinCount").Should().BeNull();
        protection.Attribute("customWorkbookProtectionFlag").Should().BeNull();
        protection.Element(protection.Name.Namespace + "nativeWorkbookProtectionChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.IsStructureProtected.Should().BeFalse();
        reloaded.StructureProtectionPassword.Should().BeNull();
        reloaded.ProtectionMetadata.Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorkbookProtectionForSchemaValidity()
    {
        using var source = Save(CreateWorkbookProtectionSourceWorkbook());
        SetWorkbookProtectionInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var protection = ReadWorkbookChildElement(saved, "workbookProtection");
        protection.Attribute("lockStructure").Should().BeNull();
        protection.Attribute("lockWindows").Should().BeNull();
        protection.Attribute("lockRevision").Should().BeNull();
        protection.Attribute("workbookPassword").Should().BeNull();
        protection.Attribute("revisionsPassword").Should().BeNull();
        protection.Attribute("workbookHashValue").Should().BeNull();
        protection.Attribute("workbookSaltValue").Should().BeNull();
        protection.Attribute("workbookSpinCount").Should().BeNull();
        protection.Attribute("revisionsHashValue").Should().BeNull();
        protection.Attribute("revisionsSaltValue").Should().BeNull();
        protection.Attribute("revisionsSpinCount").Should().BeNull();
        protection.Attribute("algorithmName").Should().BeNull();
        protection.Attribute("hashValue").Should().BeNull();
        protection.Attribute("saltValue").Should().BeNull();
        protection.Attribute("spinCount").Should().BeNull();
        protection.Attribute("customWorkbookProtectionFlag").Should().BeNull();
        protection.Element(protection.Name.Namespace + "nativeWorkbookProtectionChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.IsStructureProtected.Should().BeFalse();
        reloaded.StructureProtectionPassword.Should().BeNull();
        reloaded.ProtectionMetadata.Should().BeNull();
    }

    [Fact]
    public void WorkbookCalculationProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookCalculationPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookCalculationProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookCalculationPropertiesSourceWorkbook());
        var sourceCalculationProperties = ReadWorkbookChildElement(source, "calcPr");
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
        ReadWorkbookChildElement(saved, "calcPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCalculationProperties.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        reloaded.FullCalculationOnLoad.Should().BeTrue();
        reloaded.ForceFullCalculation.Should().BeTrue();
        reloaded.IterativeCalculation.Should().BeTrue();
        reloaded.MaxCalculationIterations.Should().Be(123);
        reloaded.MaxCalculationChange.Should().Be(0.001);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookCalculationPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookCalculationPropertiesSourceWorkbook());
        SetWorkbookCalculationPropertiesInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var calcPr = ReadWorkbookChildElement(saved, "calcPr");
        calcPr.Attribute("calcId").Should().BeNull();
        calcPr.Attribute("refMode").Should().BeNull();
        calcPr.Attribute("fullPrecision").Should().BeNull();
        calcPr.Attribute("iterateCount").Should().BeNull();
        calcPr.Attribute("iterateDelta").Should().BeNull();
        calcPr.Attribute("concurrentManualCount").Should().BeNull();
        calcPr.Attribute("customCalcPrFlag").Should().BeNull();
        calcPr.Element(calcPr.Name.Namespace + "nativeCalcPrChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        reloaded.FullCalculationOnLoad.Should().BeTrue();
        reloaded.ForceFullCalculation.Should().BeTrue();
        reloaded.IterativeCalculation.Should().BeTrue();
        reloaded.MaxCalculationIterations.Should().BeNull();
        reloaded.MaxCalculationChange.Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorkbookCalculationPropertiesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookCalculationPropertiesSourceWorkbook());
        SetWorkbookCalculationPropertiesInvalidAttributes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var calcPr = ReadWorkbookChildElement(saved, "calcPr");
        calcPr.Attribute("calcId").Should().BeNull();
        calcPr.Attribute("refMode").Should().BeNull();
        // fullPrecision is now a MODELED workbook attribute (Workbook.FullPrecision, default true) so a
        // user "Precision as displayed" toggle survives a save instead of being reverted from the stale
        // native bag. The invalid source value "maybe" coerces to false on load, so the modeled writer
        // emits the schema-valid fullPrecision="0" (rather than the old over-strip to absent).
        calcPr.Attribute("fullPrecision")!.Value.Should().Be("0");
        calcPr.Attribute("iterateCount").Should().BeNull();
        calcPr.Attribute("iterateDelta").Should().BeNull();
        calcPr.Attribute("concurrentManualCount").Should().BeNull();
        calcPr.Attribute("customCalcPrFlag").Should().BeNull();
        calcPr.Element(calcPr.Name.Namespace + "nativeCalcPrChild").Should().BeNull();

        var reloaded = ReloadWorkbook(saved);
        reloaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        reloaded.FullCalculationOnLoad.Should().BeTrue();
        reloaded.ForceFullCalculation.Should().BeTrue();
        reloaded.IterativeCalculation.Should().BeTrue();
        reloaded.MaxCalculationIterations.Should().BeNull();
        reloaded.MaxCalculationChange.Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookDefinedNamesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookDefinedNamesSourceWorkbook());
        SetWorkbookDefinedNamesInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var definedNames = ReadWorkbookChildElement(saved, "definedNames");
        definedNames.Attribute("customDefinedNamesFlag").Should().BeNull();
        definedNames.Element(definedNames.Name.Namespace + "nativeDefinedNamesChild").Should().BeNull();
        var definedName = definedNames.Elements(definedNames.Name.Namespace + "definedName").Single();
        definedName.Attribute("name")!.Value.Should().Be("DynamicSalesRange");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
        definedName.Attribute("customDefinedNameFlag").Should().BeNull();
        definedName.Element(definedName.Name.Namespace + "nativeDefinedNameChild").Should().BeNull();
        definedName.Value.Should().Contain("1+1");

        var reloaded = ReloadWorkbook(saved);
        reloaded.GetSheetAt(0).GetValue(3, 3).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void WorkbookViewProperties_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookViewPropertiesSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookViewProperties_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookViewPropertiesSourceWorkbook());
        var sourceWorkbookViews = ReadWorkbookChildElement(source, "bookViews");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        AssertWorkbookViewPropertiesModel(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorkbookChildElement(saved, "bookViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookViews.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookViewPropertiesModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookFullSave_ClampsWorkbookViewIndexesAfterSheetRemovalForExcelOpenability()
    {
        using var source = Save(CreateWorkbookViewSheetRemovalSourceWorkbook());
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        workbook.FirstVisibleSheetIndex.Should().Be(2);
        workbook.ActiveSheetIndex.Should().Be(2);
        workbook.RemoveSheet(workbook.GetSheet("Archive")!.Id).Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorkbookViewIndexesWithinSheetCount(saved);

        var reloaded = ReloadWorkbook(saved);
        reloaded.Sheets.Should().HaveCount(2);
        reloaded.FirstVisibleSheetIndex.Should().BeLessThan(reloaded.Sheets.Count);
        reloaded.ActiveSheetIndex.Should().BeLessThan(reloaded.Sheets.Count);
    }

    [Fact]
    public void WorkbookAdditionalViews_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorkbookAdditionalViewsSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void WorkbookAdditionalViews_SanitizesInvalidWorkbookViewAttributesForSchemaValidity()
    {
        var workbook = CreateWorkbookAdditionalViewsSourceWorkbook();
        workbook.AdditionalViews!.NativeAttributes["customBookViewsFlag"] = "removed";
        workbook.AdditionalViews!.Views[0].NativeXml = CreateInvalidWorkbookViewNativeXml();

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var bookViews = ReadWorkbookChildElement(saved, "bookViews");
        bookViews.Attribute("customBookViewsFlag").Should().BeNull();
        var additionalView = bookViews.Elements(bookViews.Name.Namespace + "workbookView").Skip(1).Single();
        AssertWorkbookViewInvalidAttributesRemoved(additionalView);
        AssertExtensionListSanitized(
            additionalView,
            bookViews.Name.Namespace,
            AdditionalWorkbookViewExtensionUri,
            "FreeXAdditionalWorkbookViewExtension",
            "customAdditionalWorkbookViewExtLstFlag",
            "customAdditionalWorkbookViewExtFlag",
            "nativeAdditionalWorkbookViewExtLstChild");

        var reloaded = ReloadWorkbook(saved);
        var reloadedAdditionalView = reloaded.AdditionalViews!.Views.Should().ContainSingle().Subject;
        reloadedAdditionalView.NativeXml.Should().NotContain("customWorkbookViewFlag");
        reloadedAdditionalView.NativeXml.Should().Contain("FreeXAdditionalWorkbookViewExtension");
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookAdditionalViews_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookAdditionalViewsSourceWorkbook());
        var sourceWorkbookViews = ReadWorkbookChildElement(source, "bookViews");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        AssertWorkbookAdditionalViewsModel(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorkbookChildElement(saved, "bookViews")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookViews.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertWorkbookAdditionalViewsModel(reloaded);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookViewAttributesForSchemaValidity()
    {
        using var source = Save(CreateWorkbookAdditionalViewsSourceWorkbook());
        SetWorkbookViewInvalidAttributes(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var bookViews = ReadWorkbookChildElement(saved, "bookViews");
        bookViews.Attribute("customBookViewsFlag").Should().BeNull();
        bookViews.Element(bookViews.Name.Namespace + "nativeBookViewsChild").Should().BeNull();
        var primaryView = bookViews.Elements(bookViews.Name.Namespace + "workbookView").First();
        AssertWorkbookViewInvalidAttributesRemoved(primaryView);

        var reloaded = ReloadWorkbook(saved);
        reloaded.GetSheetAt(0).GetValue(3, 3).Should().Be(new NumberValue(42));
        reloaded.AdditionalViews!.NativeAttributes.Should().BeEmpty();
        var reloadedAdditionalView = reloaded.AdditionalViews.Views.Should().ContainSingle().Subject;
        reloadedAdditionalView.NativeXml.Should().NotContain("customWorkbookViewFlag");
        reloadedAdditionalView.NativeXml.Should().NotContain("nativeWorkbookViewChild");
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookViewExtensionListsForSchemaValidity()
    {
        using var source = Save(CreateWorkbookAdditionalViewsSourceWorkbook());
        SetWorkbookViewInvalidExtensionLists(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var bookViews = ReadWorkbookChildElement(saved, "bookViews");
        var views = bookViews.Elements(bookViews.Name.Namespace + "workbookView").ToList();
        views.Should().HaveCount(2);
        AssertExtensionListSanitized(
            views[0],
            bookViews.Name.Namespace,
            WorkbookViewExtensionUri,
            "FreeXWorkbookViewExtension",
            "customWorkbookViewExtLstFlag",
            "customWorkbookViewExtFlag",
            "nativeWorkbookViewExtLstChild");
        AssertExtensionListSanitized(
            views[1],
            bookViews.Name.Namespace,
            AdditionalWorkbookViewExtensionUri,
            "FreeXAdditionalWorkbookViewExtension",
            "customAdditionalWorkbookViewExtLstFlag",
            "customAdditionalWorkbookViewExtFlag",
            "nativeAdditionalWorkbookViewExtLstChild");

        var reloaded = ReloadWorkbook(saved);
        reloaded.GetSheetAt(0).GetValue(3, 3).Should().Be(new NumberValue(42));
        reloaded.AdditionalViews!.Views.Should().ContainSingle()
            .Which.NativeXml.Should().Contain("FreeXAdditionalWorkbookViewExtension")
            .And.NotContain("customAdditionalWorkbookViewExtFlag")
            .And.NotContain("FREEX-DUPLICATE");
    }

    private static void AssertWorkbookFileVersionModel(Workbook workbook)
    {
        workbook.FileVersion.Should().NotBeNull();
        workbook.FileVersion!.AppName.Should().Be("xl");
        workbook.FileVersion.LastEdited.Should().Be("7");
        workbook.FileVersion.LowestEdited.Should().Be("7");
        workbook.FileVersion.RupBuild.Should().Be("28129");
    }

    private static void AssertWorkbookFileSharingModel(Workbook workbook)
    {
        workbook.FileSharing.Should().NotBeNull();
        workbook.FileSharing!.ReadOnlyRecommended.Should().BeTrue();
        workbook.FileSharing.UserName.Should().Be("FreeXTest");
        workbook.FileSharing.ReservationPassword.Should().Be("1234");
    }

    private static void AssertWorkbookFileRecoveryPropertiesModel(Workbook workbook)
    {
        var recoveryProperties = workbook.FileRecoveryProperties.Should().ContainSingle().Subject;
        recoveryProperties.AutoRecover.Should().BeTrue();
        recoveryProperties.CrashSave.Should().BeTrue();
        recoveryProperties.DataExtractLoad.Should().BeFalse();
        recoveryProperties.RepairLoad.Should().BeFalse();
    }

    private static void AssertWorkbookFunctionGroupsModel(Workbook workbook)
    {
        workbook.FunctionGroups.Should().NotBeNull();
        workbook.FunctionGroups!.BuiltInGroupCount.Should().Be("16");
        workbook.FunctionGroups.Groups.Should().ContainSingle()
            .Which.Name.Should().Be("FreeXNativeFunctions");
    }

    private static void AssertWorkbookPropertiesModel(Workbook workbook)
    {
        workbook.Uses1904DateSystem.Should().BeTrue();
        NativeBagAttribute(workbook.Properties, "workbookPr", "defaultThemeVersion")
            .Should()
            .Be("166925");
    }

    private static void AssertWorkbookProtectionModel(Workbook workbook)
    {
        workbook.IsStructureProtected.Should().BeTrue();
        workbook.StructureProtectionPassword.Should().Be("83AF");
    }

    private static void AssertWorkbookViewPropertiesModel(Workbook workbook)
    {
        workbook.ShowSheetTabs.Should().BeFalse();
        workbook.SheetTabRatio.Should().Be(700);
        workbook.FirstVisibleSheetIndex.Should().Be(0);
        workbook.ActiveSheetIndex.Should().Be(1);
    }

    private static void AssertWorkbookViewIndexesWithinSheetCount(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var sheetCount = workbookXml.Root!
            .Element(workbookNs + "sheets")!
            .Elements(workbookNs + "sheet")
            .Count();
        sheetCount.Should().Be(2);

        var workbookView = workbookXml.Root!
            .Element(workbookNs + "bookViews")!
            .Element(workbookNs + "workbookView")!;

        foreach (var attributeName in new[] { "firstSheet", "activeTab" })
        {
            var attribute = workbookView.Attribute(attributeName);
            if (attribute is null)
                continue;

            var index = int.Parse(attribute.Value, System.Globalization.CultureInfo.InvariantCulture);
            index.Should().BeInRange(0, sheetCount - 1);
        }
    }

    private static void AssertWorkbookAdditionalViewsModel(Workbook workbook)
    {
        workbook.ShowSheetTabs.Should().BeFalse();
        workbook.SheetTabRatio.Should().Be(700);
        workbook.FirstVisibleSheetIndex.Should().Be(0);
        workbook.ActiveSheetIndex.Should().Be(0);

        workbook.AdditionalViews.Should().NotBeNull();
        var view = workbook.AdditionalViews!.Views.Should().ContainSingle().Subject;
        view.NativeXml.Should().NotBeNullOrWhiteSpace();
        var viewXml = XElement.Parse(view.NativeXml!);
        viewXml.Attribute("visibility")!.Value.Should().Be("hidden");
        viewXml.Attribute("minimized")!.Value.Should().Be("1");
        viewXml.Attribute("showHorizontalScroll")!.Value.Should().Be("0");
        viewXml.Attribute("showVerticalScroll")!.Value.Should().Be("0");
        viewXml.Attribute("showSheetTabs")!.Value.Should().Be("0");
        viewXml.Attribute("tabRatio")!.Value.Should().Be("700");
        viewXml.Attribute("firstSheet")!.Value.Should().Be("0");
        viewXml.Attribute("activeTab")!.Value.Should().Be("0");
    }

    private static string? NativeBagAttribute(NativeXmlPreserveBag? bag, string key, string attributeName)
    {
        bag.Should().NotBeNull();
        var xml = bag!.Get(key);
        xml.Should().NotBeNull();
        return XElement.Parse(xml!).Attribute(attributeName)?.Value;
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

    private static void SetWorkbookFileVersionInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var fileVersion = workbookXml.Root!.Element(workbookNs + "fileVersion")!;
        fileVersion.SetAttributeValue("customVersionFlag", "removed");
        fileVersion.Add(new XElement(workbookNs + "nativeFileVersionChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
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

    private static void SetWorkbookFileSharingInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var fileSharing = workbookXml.Root!.Element(workbookNs + "fileSharing")!;
        fileSharing.SetAttributeValue("readOnlyRecommended", "maybe");
        fileSharing.SetAttributeValue("reservationPassword", "not-hex");
        fileSharing.SetAttributeValue("revisionsPassword", "removed");
        fileSharing.SetAttributeValue("hashValue", "not-base64");
        fileSharing.SetAttributeValue("saltValue", "also-not-base64");
        fileSharing.SetAttributeValue("customFileSharingFlag", "removed");
        fileSharing.SetAttributeValue("spinCount", "not-a-number");
        fileSharing.Add(new XElement(workbookNs + "nativeFileSharingChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
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

    private static Workbook CreateAuthoredWorkbookFileRecoveryRepairLoadSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookFileRecoveryAuthoredRepairLoad");
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = true,
            RepairLoad = true
        });
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("repair marker"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static void SetWorkbookFileRecoveryInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var fileRecoveryPr = workbookXml.Root!.Element(workbookNs + "fileRecoveryPr")!;
        fileRecoveryPr.SetAttributeValue("autoRecover", "maybe");
        fileRecoveryPr.SetAttributeValue("crashSave", "maybe");
        fileRecoveryPr.SetAttributeValue("dataExtractLoad", "maybe");
        fileRecoveryPr.SetAttributeValue("repairLoad", "maybe");
        fileRecoveryPr.SetAttributeValue("customRecoveryFlag", "removed");
        fileRecoveryPr.Add(new XElement(workbookNs + "nativeRecoveryChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
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

    private static void SetWorkbookFunctionGroupsInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var functionGroups = workbookXml.Root!.Element(workbookNs + "functionGroups")!;
        functionGroups.SetAttributeValue("builtInGroupCount", "not-a-number");
        functionGroups.SetAttributeValue("customFunctionGroupsFlag", "removed");
        functionGroups.Add(new XElement(workbookNs + "nativeFunctionGroupsChild"));
        var functionGroup = functionGroups.Element(workbookNs + "functionGroup")!;
        functionGroup.SetAttributeValue("customFunctionGroupFlag", "removed");
        functionGroup.Add(new XElement(workbookNs + "nativeFunctionGroupChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
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

    private static NativeXmlPreserveBag CreateWorkbookPropertiesMetadataWithInvalidXml()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("workbookPr", """
            <e defaultThemeVersion="166925" customWorkbookPrFlag="removed">
              <nativeWorkbookPrChild xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />
            </e>
            """);
        return bag;
    }

    private static void SetWorkbookPropertiesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var workbookPr = workbookXml.Root!.Element(workbookNs + "workbookPr")!;
        workbookPr.SetAttributeValue("date1904", "maybe");
        workbookPr.SetAttributeValue("showObjects", "invalid");
        workbookPr.SetAttributeValue("updateLinks", "invalid");
        workbookPr.SetAttributeValue("defaultThemeVersion", "not-a-number");
        workbookPr.SetAttributeValue("customWorkbookPrFlag", "removed");
        workbookPr.Add(new XElement(workbookNs + "nativeWorkbookPrChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
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

    private static NativeXmlPreserveBag CreateWorkbookProtectionMetadataWithInvalidXml()
    {
        var bag = new NativeXmlPreserveBag();
        bag.Set("workbookProtection", """
            <e lockWindows="1"
               workbookAlgorithmName="SHA-512"
               workbookHashValue="AQIDBA=="
               workbookSaltValue="BQYHCA=="
               workbookSpinCount="100000"
               algorithmName="removed"
               hashValue="removed"
               saltValue="removed"
               spinCount="removed"
               customWorkbookProtectionFlag="removed">
              <nativeWorkbookProtectionChild xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />
            </e>
            """);
        return bag;
    }

    private static void SetWorkbookProtectionInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var protection = workbookXml.Root!.Element(workbookNs + "workbookProtection")!;
        protection.SetAttributeValue("lockStructure", "maybe");
        protection.SetAttributeValue("lockWindows", "maybe");
        protection.SetAttributeValue("lockRevision", "maybe");
        protection.SetAttributeValue("workbookPassword", "not-hex");
        protection.SetAttributeValue("revisionsPassword", "also-not-hex");
        protection.SetAttributeValue("workbookHashValue", "not-base64");
        protection.SetAttributeValue("workbookSaltValue", "also-not-base64");
        protection.SetAttributeValue("workbookSpinCount", "not-a-number");
        protection.SetAttributeValue("revisionsHashValue", "not-base64");
        protection.SetAttributeValue("revisionsSaltValue", "also-not-base64");
        protection.SetAttributeValue("revisionsSpinCount", "not-a-number");
        protection.SetAttributeValue("algorithmName", "removed");
        protection.SetAttributeValue("hashValue", "removed");
        protection.SetAttributeValue("saltValue", "removed");
        protection.SetAttributeValue("spinCount", "removed");
        protection.SetAttributeValue("customWorkbookProtectionFlag", "removed");
        protection.Add(new XElement(workbookNs + "nativeWorkbookProtectionChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static Workbook CreateWorkbookCalculationPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookCalculationPropertiesPatchSave")
        {
            CalculationMode = WorkbookCalculationMode.Manual,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true,
            IterativeCalculation = true,
            MaxCalculationIterations = 123,
            MaxCalculationChange = 0.001
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calculation"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static Workbook CreateWorkbookDefinedNamesSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookDefinedNamesPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("defined name"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static Workbook CreateWorkbookViewPropertiesSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookViewPropertiesPatchSave")
        {
            ShowSheetTabs = false,
            SheetTabRatio = 700,
            FirstVisibleSheetIndex = 0,
            ActiveSheetIndex = 1
        };
        var firstSheet = workbook.AddSheet("Data");
        var secondSheet = workbook.AddSheet("Report");
        firstSheet.SetCell(new CellAddress(firstSheet.Id, 1, 1), new TextValue("view"));
        firstSheet.SetCell(new CellAddress(firstSheet.Id, 2, 2), new NumberValue(24));
        secondSheet.SetCell(new CellAddress(secondSheet.Id, 1, 1), new TextValue("active"));
        return workbook;
    }

    private static Workbook CreateWorkbookViewSheetRemovalSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookViewSheetRemoval")
        {
            ShowSheetTabs = true,
            SheetTabRatio = 700,
            FirstVisibleSheetIndex = 2,
            ActiveSheetIndex = 2
        };
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var archive = workbook.AddSheet("Archive");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("data"));
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));
        archive.SetCell(new CellAddress(archive.Id, 1, 1), new TextValue("archive"));
        return workbook;
    }

    private static void SetWorkbookCalculationPropertiesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var calcPr = workbookXml.Root!.Element(workbookNs + "calcPr")!;
        calcPr.SetAttributeValue("calcId", "not-a-number");
        calcPr.SetAttributeValue("refMode", "invalid");
        calcPr.SetAttributeValue("fullPrecision", "maybe");
        calcPr.SetAttributeValue("iterateCount", "not-a-number");
        calcPr.SetAttributeValue("iterateDelta", "not-a-number");
        calcPr.SetAttributeValue("concurrentManualCount", "not-a-number");
        calcPr.SetAttributeValue("customCalcPrFlag", "removed");
        calcPr.Add(new XElement(workbookNs + "nativeCalcPrChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookDefinedNamesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var definedNames = workbookXml.Root!.Element(workbookNs + "definedNames");
        if (definedNames is null)
        {
            definedNames = new XElement(workbookNs + "definedNames");
            var sheets = workbookXml.Root!.Element(workbookNs + "sheets");
            if (sheets is not null)
                sheets.AddAfterSelf(definedNames);
            else
                workbookXml.Root!.Add(definedNames);
        }

        definedNames.SetAttributeValue("customDefinedNamesFlag", "removed");
        definedNames.Add(new XElement(workbookNs + "nativeDefinedNamesChild"));
        definedNames.Add(new XElement(
            workbookNs + "definedName",
            new XAttribute("name", "DynamicSalesRange"),
            new XAttribute("hidden", "1"),
            new XAttribute("customDefinedNameFlag", "removed"),
            "1+1",
            new XElement(workbookNs + "nativeDefinedNameChild")));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static Workbook CreateWorkbookAdditionalViewsSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookAdditionalViewsPatchSave")
        {
            ShowSheetTabs = false,
            SheetTabRatio = 700,
            FirstVisibleSheetIndex = 0,
            ActiveSheetIndex = 0,
            AdditionalViews = new WorkbookAdditionalViewsModel
            {
                Views =
                {
                    new WorkbookAdditionalViewModel
                    {
                        NativeXml = """
                            <workbookView xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" visibility="hidden" minimized="1" showHorizontalScroll="0" showVerticalScroll="0" showSheetTabs="0" tabRatio="700" firstSheet="0" activeTab="0" />
                            """
                    }
                }
            }
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("additional view"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static string CreateInvalidWorkbookViewNativeXml()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "invalid"),
                new XAttribute("minimized", "maybe"),
                new XAttribute("showHorizontalScroll", "maybe"),
                new XAttribute("showVerticalScroll", "maybe"),
                new XAttribute("showSheetTabs", "maybe"),
                new XAttribute("tabRatio", "not-a-number"),
                new XAttribute("firstSheet", "not-a-number"),
                new XAttribute("activeTab", "not-a-number"),
                new XAttribute("xWindow", "not-a-number"),
                new XAttribute("windowWidth", "not-a-number"),
                new XAttribute("customWorkbookViewFlag", "removed"),
                new XElement(workbookNs + "nativeWorkbookViewChild"),
                CreateInvalidExtensionList(
                    workbookNs,
                    AdditionalWorkbookViewExtensionUri,
                    "FreeXAdditionalWorkbookViewExtension",
                    "customAdditionalWorkbookViewExtLstFlag",
                    "customAdditionalWorkbookViewExtFlag",
                    "nativeAdditionalWorkbookViewExtLstChild"),
                new XElement(
                    workbookNs + "extLst",
                    new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-ADDITIONAL-WORKBOOK-VIEW-EXTLST}"))))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static void SetWorkbookViewInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var bookViews = workbookXml.Root!
            .Element(workbookNs + "bookViews")!;
        bookViews.SetAttributeValue("customBookViewsFlag", "removed");
        bookViews.Add(new XElement(workbookNs + "nativeBookViewsChild"));
        var workbookView = workbookXml.Root!
            .Element(workbookNs + "bookViews")!
            .Elements(workbookNs + "workbookView")
            .First();
        SetInvalidWorkbookViewAttributes(workbookView);
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookViewInvalidExtensionLists(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var workbookViews = workbookXml.Root!
            .Element(workbookNs + "bookViews")!
            .Elements(workbookNs + "workbookView")
            .ToList();
        AddInvalidWorkbookViewExtensionLists(
            workbookViews[0],
            WorkbookViewExtensionUri,
            "FreeXWorkbookViewExtension",
            "customWorkbookViewExtLstFlag",
            "customWorkbookViewExtFlag",
            "nativeWorkbookViewExtLstChild");
        AddInvalidWorkbookViewExtensionLists(
            workbookViews[1],
            AdditionalWorkbookViewExtensionUri,
            "FreeXAdditionalWorkbookViewExtension",
            "customAdditionalWorkbookViewExtLstFlag",
            "customAdditionalWorkbookViewExtFlag",
            "nativeAdditionalWorkbookViewExtLstChild");
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void AddInvalidWorkbookViewExtensionLists(
        XElement workbookView,
        string uri,
        string payloadName,
        string listAttributeName,
        string extensionAttributeName,
        string unexpectedChildName)
    {
        var workbookNs = workbookView.Name.Namespace;
        workbookView.Add(
            CreateInvalidExtensionList(workbookNs, uri, payloadName, listAttributeName, extensionAttributeName, unexpectedChildName),
            new XElement(
                workbookNs + "extLst",
                new XElement(workbookNs + "ext", new XAttribute("uri", $"{{FREEX-DUPLICATE-{payloadName.ToUpperInvariant()}-EXTLST}}"))));
    }

    private static void SetInvalidWorkbookViewAttributes(XElement workbookView)
    {
        workbookView.SetAttributeValue("visibility", "invalid");
        workbookView.SetAttributeValue("minimized", "maybe");
        workbookView.SetAttributeValue("showHorizontalScroll", "maybe");
        workbookView.SetAttributeValue("showVerticalScroll", "maybe");
        workbookView.SetAttributeValue("showSheetTabs", "maybe");
        workbookView.SetAttributeValue("tabRatio", "not-a-number");
        workbookView.SetAttributeValue("firstSheet", "not-a-number");
        workbookView.SetAttributeValue("activeTab", "not-a-number");
        workbookView.SetAttributeValue("xWindow", "not-a-number");
        workbookView.SetAttributeValue("windowWidth", "not-a-number");
        workbookView.SetAttributeValue("customWorkbookViewFlag", "removed");
        workbookView.Add(new XElement(workbookView.Name.Namespace + "nativeWorkbookViewChild"));
    }

    private static void AssertWorkbookViewInvalidAttributesRemoved(XElement workbookView)
    {
        workbookView.Attribute("visibility").Should().BeNull();
        workbookView.Attribute("minimized").Should().BeNull();
        workbookView.Attribute("showHorizontalScroll").Should().BeNull();
        workbookView.Attribute("showVerticalScroll").Should().BeNull();
        workbookView.Attribute("showSheetTabs").Should().BeNull();
        workbookView.Attribute("tabRatio").Should().BeNull();
        workbookView.Attribute("firstSheet").Should().BeNull();
        workbookView.Attribute("activeTab").Should().BeNull();
        workbookView.Attribute("xWindow").Should().BeNull();
        workbookView.Attribute("windowWidth").Should().BeNull();
        workbookView.Attribute("customWorkbookViewFlag").Should().BeNull();
        workbookView.Element(workbookView.Name.Namespace + "nativeWorkbookViewChild").Should().BeNull();
    }

    private static Workbook ReloadWorkbook(Stream stream)
    {
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }
}
