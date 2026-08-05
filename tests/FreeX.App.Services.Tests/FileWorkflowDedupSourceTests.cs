using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileWorkflowDedupSourceTests
{
    [Fact]
    public void RecentFileRegistrationDecision_StaysInSharedService()
    {
        var serviceSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "RecentFileRegistrationService.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "FileCommandSession.cs"));
        var wpfWorkbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));
        var avaloniaWorkbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var freewSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Host",
            "FileCommands.cs"));
        var freepSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));

        serviceSource.Should().Contain("FileLifecyclePlanner.PlanRecentRegistration(");
        sessionSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        wpfWorkbookSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        avaloniaWorkbookSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        avaloniaWorkbookSource.Should().Contain("FileAccessIdentity: fileAccessIdentity ?? target.FileAccessIdentity");

        sessionSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        wpfWorkbookSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        avaloniaWorkbookSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        wpfWorkbookSource.Should().NotContain("_recentFiles.AddOrUpdate(");
        avaloniaWorkbookSource.Should().NotContain("_recentFiles.AddOrUpdate(");
        freewSource.Should().Contain("FileCommandWorkflow");
        freepSource.Should().Contain("FileCommandWorkflow");
    }

    [Fact]
    public void AvaloniaPortablePdfExportTargetDecision_StaysInExportPickerPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "ExportFilePickerPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var printSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.Print.cs"));

        plannerSource.Should().Contain("BuildPortablePdfSaveTargetPlan(");
        plannerSource.Should().Contain("ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf)");
        plannerSource.Should().Contain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, pathPlan, pathExists)");

        avaloniaSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists)");
        printSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists)");
        avaloniaSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)");
        printSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)");
    }

    [Fact]
    public void FreeXWorkbookOpenSavePickerReadiness_StaysInSharedPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileCommandPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var wpfLifecycleSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.WorkbookLifecycle.cs"));

        plannerSource.Should().Contain("PlanOpenPicker(");
        plannerSource.Should().Contain("PlanSaveAsPicker(");
        plannerSource.Should().Contain("OpenUnavailableMessage");
        plannerSource.Should().Contain("NoSaveFormatsMessage");
        plannerSource.Should().Contain("FileOpenPickerPlan Picker");
        plannerSource.Should().Contain("FileSavePickerPlan Picker");

        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanOpenPicker(StorageProvider.CanOpen, _session.OpenFormats)");
        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanSaveAsPicker(");
        avaloniaSource.Should().Contain("StorageProvider.CanSave,");
        avaloniaSource.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        avaloniaSource.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        avaloniaSource.Should().NotContain("\"Open unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No open formats are available.\"");
        avaloniaSource.Should().NotContain("\"Save As unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No save formats are available.\"");

        wpfLifecycleSource.Should().Contain("WorkbookFileLifecycleCoordinator.SaveResolvedAsync(");
        wpfLifecycleSource.Should().Contain("WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(");
        wpfLifecycleSource.Should().Contain("WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        avaloniaSource.Should().Contain("WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        avaloniaSource.Should().NotContain("SaveCurrentWorkbookThenConfirmCleanAsync");
    }

    [Fact]
    public void FreeXWorkbookOpenTargetResolution_StaysInSharedPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookOpenTargetPlanner.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookSession.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));

        plannerSource.Should().Contain("LocalFilePath.TryNormalize(");
        plannerSource.Should().Contain("FileFormatResolver.FindOpenAdapter(");
        plannerSource.Should().Contain("new WorkbookOpenTarget(");

        sessionSource.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(");
        sessionSource.Should().NotContain("FileFormatResolver.FindOpenAdapter(_adapters, extension");

        wpfSource.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(_fileAdapters, path");
        wpfSource.Should().NotContain("FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext");
    }

    [Fact]
    public void FreeXWorkbookSaveTargetAndPathNormalizationDecisions_StayInCoordinator()
    {
        var coordinatorSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileLifecycleCoordinator.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));

        coordinatorSource.Should().Contain("PlanSaveTargetWrite(");
        coordinatorSource.Should().Contain("FileSavePlanner.CanSkipCleanSave(");
        coordinatorSource.Should().Contain("PlanSavePathNormalization(");
        coordinatorSource.Should().Contain("WorkbookSession.EnsureSaveExtension(");

        avaloniaSource.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(");
        avaloniaSource.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(");
        avaloniaSource.Should().NotContain("FileSavePlanner.CanSkipCleanSave(");
        avaloniaSource.Should().NotContain("ShouldPromptForNormalizedWorkbookOverwrite(");
        avaloniaSource.Should().NotContain("Path.GetFullPath(requestedPath)");

        wpfSource.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(");
        wpfSource.Should().NotContain("FileSavePlanner.CanSkipCleanSave(");
    }

    [Fact]
    public void ResolvedSaveChoreography_StaysInSharedCoordinator()
    {
        var sharedSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "AsyncFileLifecycleCoordinator.cs"));
        var workbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileLifecycleCoordinator.cs"));

        sharedSource.Should().Contain("FileLifecyclePlanner.PlanSave(");
        sharedSource.Should().Contain("var target = resolveCurrentTarget();");
        sharedSource.Should().Contain("resolvedTargetPolicy?.Invoke(target)");

        workbookSource.Should().Contain("AsyncFileLifecycleCoordinator.SaveResolvedAsync(");
        workbookSource.Should().Contain("resolvedTargetPolicy: target =>");
        workbookSource.Should().Contain("PlanSaveTargetWrite(isDirty, currentFilePath, target)");
        workbookSource.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        workbookSource.Should().NotContain("var target = resolveCurrentTarget();");
    }

    [Fact]
    public void FreeXWorkbookOpenSaveCompletionContext_StaysInSharedPlanner()
    {
        var completionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileCompletionPlanner.cs"));
        var saveCompletionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "SaveCompletionPlanner.cs"));
        var sessionFactorySource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookSessionFactory.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));

        completionSource.Should().Contain("PlanOpen(");
        completionSource.Should().Contain("ResolveActiveSheetId(result.Workbook)");
        completionSource.Should().Contain("CurrentFilePath: result.OpenedAsTemplate ? null : target.Path");
        completionSource.Should().Contain("new RecentFileRegistrationRequest(");
        completionSource.Should().Contain("PlanSaveFileContext(");
        saveCompletionSource.Should().Contain("WorkbookFileCompletionPlanner.PlanSaveFileContext(");
        sessionFactorySource.Should().Contain("WorkbookFileCompletionPlanner.PlanOpen(");
        wpfSource.Should().Contain("WorkbookFileCompletionPlanner.PlanOpen(");
        wpfSource.Should().Contain("new FreeX.App.Services.WorkbookOpenResult(");
        wpfSource.Should().Contain("plan.FileContext is { } fileContext");
        wpfSource.Should().NotContain("_currentFilePath = result.OpenedAsTemplate");
        wpfSource.Should().NotContain("var activeSheetIndex =");
        wpfSource.Should().NotContain("WorkbookTitleFormatter.DisplayNameFromPath(target.Path)");
    }

    [Fact]
    public void FreeXWorkbookPickerPlans_UseSharedDialogPlanRecords()
    {
        var pickerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFilePickerPlanner.cs"));
        var surfaceSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileDialogSurfacePlanner.cs"));

        pickerSource.Should().Contain("public static FileOpenDialogPlan BuildOpenDialogPlan");
        pickerSource.Should().Contain("public static FileSaveDialogPlan BuildSaveDialogPlan");
        pickerSource.Should().Contain("public static FileOpenPickerPlan BuildOpenPickerPlan");
        pickerSource.Should().Contain("public static FileSavePickerPlan BuildSavePickerPlan");
        pickerSource.Should().NotContain("record WorkbookOpenDialogPlan");
        pickerSource.Should().NotContain("record WorkbookSaveDialogPlan");
        pickerSource.Should().NotContain("record WorkbookOpenPickerPlan");
        pickerSource.Should().NotContain("record WorkbookSavePickerPlan");

        surfaceSource.Should().Contain("CreateOpenPlan(FileOpenPickerPlan pickerPlan)");
        surfaceSource.Should().Contain("CreateSaveAsPlan(FileSavePickerPlan pickerPlan)");
    }

    [Fact]
    public void SaveDialogSelectionPolicy_StaysInSharedIoResolver()
    {
        var resolverSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.IO",
            "FileDialogSaveSelectionResolver.cs"));
        var workbookPlannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFilePickerPlanner.cs"));
        var wpfWorkbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));
        var freewSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "DocumentPersistenceWorkflow.cs"));

        resolverSource.Should().Contain("ResolveAdapter");
        resolverSource.Should().Contain("FindSelectedSaveFormat");
        resolverSource.Should().Contain("resolveByExtension(adapterRows, chosenExtension)");
        workbookPlannerSource.Should().Contain("FileDialogSaveSelectionResolver.ResolveAdapter(");
        workbookPlannerSource.Should().Contain("filterIndex");
        wpfWorkbookSource.Should().Contain("WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(_fileAdapters, result.FileName!, result.FilterIndex, out var target)");
        freewSource.Should().Contain("FileDialogSaveSelectionResolver.ResolveAdapter(");
        freewSource.Should().NotContain("private IDocumentFileAdapter? ResolveSaveAdapter");
        freewSource.Should().NotContain("savePairs");
    }

    [Fact]
    public void BackstageInfoFileMetadataProbe_StaysInSharedService()
    {
        var readerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookInfoFileMetadataReader.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "BackstageInfoPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.Backstage.cs"));

        readerSource.Should().Contain("new FileInfo(currentFilePath)");
        readerSource.Should().Contain("WorkbookInfoPlanner.Build(");
        plannerSource.Should().Contain("WorkbookInfoFileMetadataReader.BuildPlan(");
        avaloniaSource.Should().Contain("WorkbookInfoFileMetadataReader.BuildPlan(");

        plannerSource.Should().NotContain("new FileInfo(");
        avaloniaSource.Should().NotContain("new FileInfo(");
        avaloniaSource.Should().NotContain("File.Exists(path)");
    }

    [Fact]
    public void GetDataPickerPolicy_StaysInSharedPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "ImportDataFilePickerPlanner.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.DataCommands.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.GetData.cs"));

        plannerSource.Should().Contain("AdapterImportExtensions");
        plannerSource.Should().Contain("\".csv\",");
        plannerSource.Should().Contain("\".xml\"");
        plannerSource.Should().Contain("TextImportPatterns");
        plannerSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(importAdapters)");

        wpfSource.Should().Contain("ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(_fileAdapters)");
        wpfSource.Should().Contain("var adapters = plan.Adapters;");
        wpfSource.Should().Contain("checkFileExists: plan.CheckFileExists");
        wpfSource.Should().Contain("multiselect: plan.Multiselect");
        wpfSource.Should().NotContain("string[] dataExtensions");
        wpfSource.Should().NotContain("FileDialogFilterBuilder.BuildOpenFilter(adapters)");

        avaloniaSource.Should().Contain("ImportDataFilePickerPlanner.BuildTextOpenPickerPlan(UiText.Get(\"GetData_FileTypeName\"))");
        avaloniaSource.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        avaloniaSource.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(");
        avaloniaSource.Should().NotContain("Patterns = [\"*.csv\", \"*.tsv\", \"*.tab\", \"*.txt\"]");
    }

    [Fact]
    public void AvaloniaStorageProviderPickerCalls_StayInSharedShellService()
    {
        var pickerService = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaFilePickerService.cs"));

        pickerService.Should().Contain("OpenFilePickerAsync(");
        pickerService.Should().Contain("SaveFilePickerAsync(");
        pickerService.Should().Contain("FilePickerOpenOptions");
        pickerService.Should().Contain("FilePickerSaveOptions");
        pickerService.Should().Contain("TryGetLocalPath()");

        var appSources = new[]
        {
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.GetData.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Print.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("freew", "FreeW.App.Avalonia", "MainWindow.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("freep", "FreeP.App.Avalonia", "MainWindow.cs")),
        };

        string.Concat(appSources).Should().Contain("AvaloniaFilePickerService.");

        foreach (var source in appSources)
        {
            source.Should().NotContain(".OpenFilePickerAsync(");
            source.Should().NotContain(".SaveFilePickerAsync(");
            source.Should().NotContain("new FilePickerOpenOptions");
            source.Should().NotContain("new FilePickerSaveOptions");
        }
    }

    [Fact]
    public void SisterAppFileNamePolicy_StaysInSharedWorkflow()
    {
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "FileCommandWorkflow.cs"));
        var freewWpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Host",
            "FileCommands.cs"));
        var freewAvaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs"));
        var freepWpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));
        var freepAvaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        workflowSource.Should().Contain("CurrentFileName");
        workflowSource.Should().Contain("CurrentFileNameWithoutExtensionOr");
        freewWpfSource.Should().Contain("_workflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("SisterAppFileTextPlanner.Document");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName)");
        freepWpfSource.Should().Contain("_workflow.CurrentFileName");
        freepAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileName");

        freewWpfSource.Should().NotContain("Path.GetFileName(_workflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileName(_fileWorkflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentPath)");
        freepWpfSource.Should().NotContain("SourceFileName(_workflow.CurrentPath)");
        freepAvaloniaSource.Should().NotContain("SourceFileName(_fileWorkflow.CurrentPath)");
    }
}
