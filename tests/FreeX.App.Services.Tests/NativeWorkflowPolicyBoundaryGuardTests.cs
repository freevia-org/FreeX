using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class NativeWorkflowPolicyBoundaryGuardTests
{
    private static readonly string[] SharedOwnerFiles =
    [
        Path.Combine("src", "FreeX.App.Services", "WorkbookFileCommandPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookFileDialogSurfacePlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookFilePickerPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookFileLifecycleCoordinator.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookFileWorkflow.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookImportWorkflow.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookExportWorkflow.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookPrintWorkflow.cs"),
        Path.Combine("src", "FreeX.App.Services", "ExportFilePickerPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "ExportOptionsDialogSurfacePlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "ExportPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "WorkbookExportPrintPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "PrintJobPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "PortablePdfExportPlanner.cs")
    ];

    [Fact]
    public void NativeFileExportAndPrintWorkflowPolicy_StaysInSharedServices()
    {
        var repoRoot = ResolveRepositoryRoot();

        foreach (var ownerFile in SharedOwnerFiles)
        {
            File.Exists(Path.Combine(repoRoot, ownerFile))
                .Should()
                .BeTrue($"{ownerFile} owns shared FreeX native workflow planning policy");
        }

        var workbookCommandPlanner = Read(repoRoot, "src", "FreeX.App.Services", "WorkbookFileCommandPlanner.cs");
        var dialogSurfacePlanner = Read(repoRoot, "src", "FreeX.App.Services", "WorkbookFileDialogSurfacePlanner.cs");
        var exportPickerPlanner = Read(repoRoot, "src", "FreeX.App.Services", "ExportFilePickerPlanner.cs");
        var exportOptionsPlanner = Read(repoRoot, "src", "FreeX.App.Services", "ExportOptionsDialogSurfacePlanner.cs");
        var exportPrintPlanner = Read(repoRoot, "src", "FreeX.App.Services", "WorkbookExportPrintPlanner.cs");
        var printJobPlanner = Read(repoRoot, "src", "FreeX.App.Services", "PrintJobPlanner.cs");
        var fileWorkflow = Read(repoRoot, "src", "FreeX.App.Services", "WorkbookFileWorkflow.cs");

        workbookCommandPlanner.Should().Contain("PlanOpenPicker(");
        workbookCommandPlanner.Should().Contain("PlanSaveAsPicker(");
        dialogSurfacePlanner.Should().Contain("CreateOpenPlan(FileOpenPickerPlan pickerPlan)");
        dialogSurfacePlanner.Should().Contain("CreateSaveAsPlan(FileSavePickerPlan pickerPlan)");
        exportPickerPlanner.Should().Contain("BuildPortablePdfPickerPlan(");
        exportPickerPlanner.Should().Contain("BuildPdfXpsDialogPlan(");
        exportPickerPlanner.Should().Contain("BuildPortablePdfSaveTargetPlan(");
        exportPickerPlanner.Should().Contain("FormatFromPdfXpsFilterIndex(");
        exportOptionsPlanner.Should().Contain("CreateFormatAvailability(");
        exportOptionsPlanner.Should().Contain("CreateResult(");
        exportPrintPlanner.Should().Contain("CreatePlanFromPageSetup(");
        printJobPlanner.Should().Contain("CreatePlanFromPageSetup(");
        fileWorkflow.Should().Contain("public async Task<WorkbookOpenWorkflowResult> OpenAsync(");
        fileWorkflow.Should().Contain("public async Task<WorkbookSaveWorkflowResult> SaveTargetAsync(");
    }

    [Fact]
    public void WpfAndAvaloniaHosts_ConsumeSharedNativeWorkflowPolicy()
    {
        var repoRoot = ResolveRepositoryRoot();
        var wpfBackstageSource = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var wpfExportSource = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.PrintExport.cs");
        var wpfImportSource = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.DataCommands.cs");
        var wpfExportOptionsSource = Read(repoRoot, "src", "FreeX.App.Host", "ExportOptionsDialog.cs");
        var wpfParitySource = Read(repoRoot, "src", "FreeX.App.Host", "ParityCapture.cs");
        var avaloniaMainSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaExportOptionsSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ExportOptions.cs");
        var avaloniaPrintSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Print.cs");
        var avaloniaImportSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.GetData.cs");
        var avaloniaParitySource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs");

        wpfBackstageSource.Should().Contain("WorkbookFilePickerPlanner.BuildOpenDialogPlan(_fileAdapters)");
        wpfBackstageSource.Should().Contain("WorkbookFilePickerPlanner.BuildSaveDialogPlan(");
        wpfBackstageSource.Should().Contain("_fileWorkflow.TryResolveSaveTarget(");
        wpfBackstageSource.Should().Contain("_fileWorkflow.OpenAsync(");
        wpfBackstageSource.Should().Contain("_fileWorkflow.SaveTargetAsync(");
        wpfExportSource.Should().Contain("ExportFilePickerPlanner.BuildPdfXpsDialogPlan(");
        wpfExportSource.Should().Contain("ExportFilePickerPlanner.FormatFromPdfXpsFilterIndex(");
        wpfExportSource.Should().Contain("ExportPlanner.PlanExport(");
        wpfExportSource.Should().Contain("WorkbookExportWorkflow.ExecuteBooleanAsync(");
        wpfExportSource.Should().Contain("WorkbookPrintWorkflow.CreatePlan(");
        wpfImportSource.Should().Contain("WorkbookImportWorkflow.ImportPathAsync(");
        wpfExportOptionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(");
        wpfExportOptionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateResult(");
        wpfParitySource.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateOpenPlan(");
        wpfParitySource.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(");

        avaloniaMainSource.Should().Contain("WorkbookFileCommandPlanner.PlanOpenPicker(");
        avaloniaMainSource.Should().Contain("WorkbookFileCommandPlanner.PlanSaveAsPicker(");
        avaloniaMainSource.Should().Contain("_fileWorkflow.OpenAsync(");
        avaloniaMainSource.Should().Contain("_fileWorkflow.SaveTargetAsync(");
        avaloniaMainSource.Should().Contain("WorkbookExportWorkflow.ExecuteAsync(");
        avaloniaMainSource.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        avaloniaMainSource.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        avaloniaMainSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfPickerPlan(");
        avaloniaMainSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(");
        avaloniaMainSource.Should().Contain("WorkbookExportPrintPlanner.CreatePlanFromPageSetup(");
        avaloniaExportOptionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(");
        avaloniaExportOptionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateResult(");
        avaloniaExportOptionsSource.Should().Contain("ExportPlanner.TryCreatePageRange(");
        avaloniaExportOptionsSource.Should().Contain("ExportPlanner.TryNormalizePdfLanguage(");
        avaloniaExportOptionsSource.Should().Contain("ExportPlanner.TryValidatePublishOptions(");
        avaloniaExportOptionsSource.Should().Contain("ExportPlanner.TryValidatePageRange(");
        avaloniaPrintSource.Should().Contain("WorkbookPrintWorkflow.CreatePlan(");
        avaloniaPrintSource.Should().Contain("WorkbookPrintWorkflow.ExecutePortableAsync(");
        avaloniaImportSource.Should().Contain("WorkbookImportWorkflow.ApplyImportedWorkbookEdit(");
        avaloniaPrintSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(");
        avaloniaParitySource.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateOpenPlan(");
        avaloniaParitySource.Should().Contain("WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(");
        avaloniaParitySource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(");

        wpfBackstageSource.Should().NotContain("WorkbookSaveExecutionCoordinator.Begin(");
        wpfBackstageSource.Should().Contain(
            "request => RecentFileRegistrationService.RegisterIfNeeded(ReloadRecentFilesStore, request)");
        avaloniaMainSource.Should().NotContain("WorkbookSaveExecutionCoordinator.Begin(");
        avaloniaMainSource.Should().NotContain("_openService.LoadAsync(");
        wpfImportSource.Should().NotContain("adapter.Load(stream)");
        avaloniaPrintSource.Should().NotContain("PrintJobPlanner.CreatePlanFromPageSetup(");
    }

    [Fact]
    public void RendererHosts_DoNotForkNativeWorkflowPickerOrSurfacePolicy()
    {
        var repoRoot = ResolveRepositoryRoot();
        var wpfBackstageSource = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var wpfExportSource = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.PrintExport.cs");
        var avaloniaMainSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaPrintSource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Print.cs");
        var avaloniaParitySource = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs");

        wpfBackstageSource.Should().NotContain("\"Open Workbook\"");
        wpfBackstageSource.Should().NotContain("\"Save Workbook\"");
        wpfBackstageSource.Should().NotContain("FileDialogFilterBuilder.BuildOpenFilter(");
        wpfBackstageSource.Should().NotContain("FileDialogFilterBuilder.BuildSaveFilter(");
        wpfExportSource.Should().NotContain("BuildSuggestedExportFileName(");
        wpfExportSource.Should().NotContain("FormatFromPdfXpsFilterIndex(int");

        avaloniaMainSource.Should().NotContain("\"Open unavailable on this platform.\"");
        avaloniaMainSource.Should().NotContain("\"Save As unavailable on this platform.\"");
        avaloniaMainSource.Should().NotContain("new FilePickerOpenOptions");
        avaloniaMainSource.Should().NotContain("new FilePickerSaveOptions");
        avaloniaMainSource.Should().NotContain("ExportPathPlanner.Plan(");
        avaloniaMainSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(");
        avaloniaPrintSource.Should().NotContain("ExportPathPlanner.Plan(");
        avaloniaPrintSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(");
        avaloniaParitySource.Should().NotContain("new WorkbookFileDialogSurfacePlan(");
    }

    private static string Read(string repoRoot, params string[] segments) =>
        File.ReadAllText(Path.Combine([repoRoot, .. segments]));

    private static string ResolveRepositoryRoot()
    {
        var servicesProject = RepositoryFileLocator.Find("src", "FreeX.App.Services", "FreeX.App.Services.csproj");
        return Directory.GetParent(servicesProject)?.Parent?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
    }
}
