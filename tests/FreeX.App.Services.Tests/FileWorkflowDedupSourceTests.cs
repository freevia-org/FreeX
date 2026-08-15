using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileWorkflowDedupSourceTests
{
    [Fact]
    public void FreeXHostsShareWorkbookDocumentStateOwnership()
    {
        var documentStateSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "WorkbookDocumentState.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookSession.cs"));
        var wpfWindowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.xaml.cs"));
        var avaloniaWindowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));

        documentStateSource.Should().Contain("public sealed class WorkbookDocumentState");
        documentStateSource.Should().Contain("public void MarkDirty()");
        documentStateSource.Should().Contain("public void MarkSavedAtUndoDepth(int undoDepthAtSave, long undoStackVersionAtSave)");
        documentStateSource.Should().Contain("public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion)");

        sessionSource.Should().Contain("private readonly WorkbookDocumentState _documentState;");
        sessionSource.Should().Contain("sharedDocumentStateOwner?._documentState ?? documentState ?? new WorkbookDocumentState()");
        sessionSource.Should().Contain("_documentState.MarkDirty();");
        sessionSource.Should().Contain("_documentState.MarkSavedAtUndoDepth(");
        sessionSource.Should().Contain("_documentState.TryMarkCleanIfAtSavePoint(");
        sessionSource.Should().NotContain("private bool _isDirty;");
        sessionSource.Should().NotContain("private int _dirtyGeneration;");
        sessionSource.Should().NotContain("private string? _currentFilePath;");
        sessionSource.Should().NotContain("private int _savedUndoDepth");
        sessionSource.Should().NotContain("private long? _savedUndoStackVersion");

        wpfWindowSource.Should().Contain("private bool _workbookDirty => _session.IsDirty;");
        wpfWindowSource.Should().Contain("private int _workbookDirtyGeneration => _session.DirtyGeneration;");
        wpfWindowSource.Should().NotContain("private WorkbookDocumentState _documentState;");

        avaloniaWindowSource.Should().Contain("isDirty: _session.IsDirty");
        avaloniaWindowSource.Should().Contain("GetDirtyGeneration: () => _session.DirtyGeneration");
        avaloniaWindowSource.Should().NotContain("private bool _workbookDirty;");
        avaloniaWindowSource.Should().NotContain("private int _workbookDirtyGeneration;");
    }

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
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileWorkflow.cs"));
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
        // FreeP's WPF file commands moved out of FileCommands.cs into the port adapter when the
        // presentation file-command session was shared; this guard follows them there.
        var freepSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Host",
            "WpfPresentationFileCommandPorts.cs"));

        serviceSource.Should().Contain("FileLifecyclePlanner.PlanRecentRegistration(");
        sessionSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        workflowSource.Should().Contain("RegisterRecentFile(completionPlan.RecentFileRegistration)");
        workflowSource.Should().Contain("RegisterRecentFile(fileContext.RecentFileRegistration)");
        wpfWorkbookSource.Should().Contain(
            "request => RecentFileRegistrationService.RegisterIfNeeded(ReloadRecentFilesStore, request)");
        avaloniaWorkbookSource.Should().Contain("_fileWorkflow.RegisterRecentFile(");
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
    public void PortablePdfExportTargetDecision_StaysInSharedInteractionPolicy()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "ExportFilePickerPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var interactionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookExportInteractionPlanner.cs"));
        var printSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.Print.cs"));

        plannerSource.Should().Contain("BuildPortablePdfSaveTargetPlan(");
        plannerSource.Should().Contain("ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf)");
        plannerSource.Should().Contain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, pathPlan, pathExists)");

        interactionSource.Should().Contain("ExportPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, request, pathExists)");
        avaloniaSource.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
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

        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanOpenPicker(StorageProvider.CanOpen, _fileWorkflow.OpenFormats)");
        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanSaveAsPicker(");
        avaloniaSource.Should().Contain("_fileWorkflow.SaveFormats,");
        avaloniaSource.Should().Contain("StorageProvider.CanSave,");
        avaloniaSource.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        avaloniaSource.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        avaloniaSource.Should().NotContain("\"Open unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No open formats are available.\"");
        avaloniaSource.Should().NotContain("\"Save As unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No save formats are available.\"");

        wpfLifecycleSource.Should().Contain("_fileWorkflow.SaveResolvedAsync(");
        wpfLifecycleSource.Should().Contain("_fileWorkflow.ConfirmBeforeDestructiveActionAsync(");
        wpfLifecycleSource.Should().Contain("_fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        avaloniaSource.Should().Contain("_fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        avaloniaSource.Should().NotContain("WorkbookFileLifecycleCoordinator.SaveResolvedAsync(");
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
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileWorkflow.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));

        plannerSource.Should().Contain("LocalFilePath.TryNormalize(");
        plannerSource.Should().Contain("FileFormatResolver.FindOpenAdapter(");
        plannerSource.Should().Contain("new WorkbookOpenTarget(");

        sessionSource.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(");
        sessionSource.Should().NotContain("FileFormatResolver.FindOpenAdapter(_adapters, extension");
        workflowSource.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(");

        wpfSource.Should().Contain("_fileWorkflow.TryResolveOpenTarget(path");
        wpfSource.Should().NotContain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(");
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
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileWorkflow.cs"));

        coordinatorSource.Should().Contain("PlanSaveTargetWrite(");
        coordinatorSource.Should().Contain("FileSavePlanner.CanSkipCleanSave(");
        coordinatorSource.Should().Contain("PlanSavePathNormalization(");
        coordinatorSource.Should().Contain("WorkbookSession.EnsureSaveExtension(");

        workflowSource.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(");
        workflowSource.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(");
        avaloniaSource.Should().Contain("_fileWorkflow.ShouldSkipSaveTargetWrite(");
        avaloniaSource.Should().Contain("_fileWorkflow.PlanSavePathNormalization(");
        avaloniaSource.Should().NotContain("WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(");
        avaloniaSource.Should().NotContain("FileSavePlanner.CanSkipCleanSave(");
        avaloniaSource.Should().NotContain("ShouldPromptForNormalizedWorkbookOverwrite(");
        avaloniaSource.Should().NotContain("Path.GetFullPath(requestedPath)");

        wpfSource.Should().Contain("_fileWorkflow.ShouldSkipSaveTargetWrite(");
        wpfSource.Should().NotContain("WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(");
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
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileWorkflow.cs"));

        completionSource.Should().Contain("PlanOpen(");
        completionSource.Should().Contain("ResolveActiveSheetId(result.Workbook)");
        completionSource.Should().Contain("CurrentFilePath: result.OpenedAsTemplate ? null : target.Path");
        completionSource.Should().Contain("new RecentFileRegistrationRequest(");
        completionSource.Should().Contain("PlanSaveFileContext(");
        saveCompletionSource.Should().Contain("WorkbookFileCompletionPlanner.PlanSaveFileContext(");
        sessionFactorySource.Should().Contain("WorkbookFileCompletionPlanner.PlanOpen(");
        workflowSource.Should().Contain("WorkbookFileCompletionPlanner.PlanOpen(");
        workflowSource.Should().Contain("WorkbookSaveExecutionCoordinator.Begin(");
        wpfSource.Should().Contain("_fileWorkflow.OpenAsync(");
        wpfSource.Should().Contain("_fileWorkflow.SaveTargetAsync(");
        wpfSource.Should().NotContain("WorkbookFileCompletionPlanner.PlanOpen(");
        wpfSource.Should().NotContain("new FreeX.App.Services.WorkbookOpenResult(");
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
        wpfWorkbookSource.Should().Contain("_fileWorkflow.TryResolveSaveTarget(");
        wpfWorkbookSource.Should().NotContain("WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(");
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
        plannerSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(");
        plannerSource.Should().Contain("FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(");

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
    public void FreeXRendererFileByteReads_StayInApplicationWorkflow()
    {
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "FileByteReadWorkflow.cs"));
        var rendererSources = new[]
        {
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PageLayout.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "HeaderFooterDialog.Pictures.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.GetData.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs")),
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs")),
        };

        workflowSource.Should().Contain("public static Task<FileByteReadResult> ReadLocalPathAsync(");
        workflowSource.Should().Contain("public static async Task<FileByteReadResult> ReadStreamAsync(");
        string.Concat(rendererSources).Should().Contain("FileByteReadWorkflow.");

        foreach (var source in rendererSources)
        {
            source.Should().NotContain("File.ReadAllBytes(");
            source.Should().NotContain("File.ReadAllBytesAsync(");
            source.Should().NotContain("CopyToAsync(memory");
        }
    }

    [Fact]
    public void AvaloniaPdfExport_UsesAtomicFileWriter()
    {
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));

        avaloniaSource.Should().Contain("AtomicFileWriter.WriteAllBytesAsync(");
        avaloniaSource.Should().NotContain("File.WriteAllBytesAsync(");
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
            "MainWindow.cs"));
        var freepAvaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var freepLifecycleSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileLifecycleAdapter.cs"));
        var freepSessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileCommandSession.cs"));

        workflowSource.Should().Contain("CurrentFileName");
        workflowSource.Should().Contain("CurrentFileNameWithoutExtensionOr");
        freewWpfSource.Should().Contain("_workflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("FreeWFileTextResources.Document");
        freewAvaloniaSource.Should().NotContain("SisterAppFileTextPlanner.Document");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName)");
        freepLifecycleSource.Should().Contain("public string DisplayName => _workflow.DisplayName;");
        freepSessionSource.Should().Contain("public string DisplayName => _lifecycle.DisplayName;");
        freepWpfSource.Should().Contain("DisplayName: _fileSession.DisplayName,");
        freepWpfSource.Should().NotContain("_workflow.CurrentFileName");
        freepAvaloniaSource.Should().Contain("private readonly PresentationFileCommandSession _fileSession;");
        freepAvaloniaSource.Should().Contain("new PresentationFileLifecycleAdapter(");
        freepAvaloniaSource.Should().NotContain("_fileWorkflow.CurrentFileName");

        freewWpfSource.Should().NotContain("Path.GetFileName(_workflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileName(_fileWorkflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentPath)");
        freepWpfSource.Should().NotContain("SourceFileName(_workflow.CurrentPath)");
        freepAvaloniaSource.Should().NotContain("SourceFileName(_fileWorkflow.CurrentPath)");
    }
}
