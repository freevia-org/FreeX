using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaShellSourceTests
{
    [Fact]
    public void AddWatchParityCapture_UsesSharedFixtureAndStripsAvaloniaLabelMnemonic()
    {
        var avaloniaCaptureSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var avaloniaDialogSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuDialogs.cs"));
        var wpfCaptureSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "ParityCapture.cs"));

        avaloniaCaptureSource.Should().Contain(
            "ShowAddWatchDialogAsync(AddWatchDialogPlanner.ParitySelectedRangeText)");
        wpfCaptureSource.Should().Contain(
            "new AddWatchDialog(AddWatchDialogPlanner.ParitySelectedRangeText)");
        avaloniaDialogSource.Should().Contain(
            "Text = StripDisplayMnemonic(UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey))");
        avaloniaDialogSource.Should().NotContain(
            "Text = UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey)");
        avaloniaDialogSource.Should().Contain(
            "AddWatchDialogPlanner.ButtonMinWidth");
        avaloniaDialogSource.Should().Contain(
            "Content = UiText.Get(AddWatchDialogPlanner.CancelButtonKey)");
    }

    [Fact]
    public void App_WiresMacOsFileActivationToMainWindowOpenPipeline()
    {
        var appSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));
        var programSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var ingressPlannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookOpenIngressPlanner.cs"));

        programSource.Should().NotContain("DisableAvaloniaAppDelegate");
        appSource.Should().Contain("new MainWindow(StartupArguments)");
        appSource.Should().Contain("desktop.MainWindow = mainWindow;");
        appSource.Should().Contain("this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime");
        // Wired through a non-async-void wrapper so a thrown activation cannot crash the dispatcher.
        appSource.Should().Contain("activatableLifetime.Activated += (_, args) => _ = OnActivatedAsync(mainWindow, args);");
        appSource.Should().Contain("await MainWindow_ActivatedAsync(mainWindow, args);");
        appSource.Should().Contain("args is not FileActivatedEventArgs fileArgs");
        appSource.Should().Contain("fileArgs.Kind != ActivationKind.File");
        appSource.Should().Contain("mainWindow.Show();");
        appSource.Should().Contain("mainWindow.Activate();");
        appSource.Should().Contain("await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);");

        windowSource.Should().Contain("public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)");
        windowSource.Should().Contain("private bool TrySelectOpenableLocalWorkbookPath(IEnumerable<IStorageItem> files, out string? path, out string message)");
        windowSource.Should().Contain("TrySelectOpenableLocalWorkbookPath(files, out var path, out var storageItem, out var message)");
        windowSource.Should().Contain("file.TryGetLocalPath()");
        windowSource.Should().Contain("WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(");
        ingressPlannerSource.Should().Contain("LocalFilePath.TryNormalize(candidatePath, out var normalizedPath)");
        ingressPlannerSource.Should().Contain("File.Exists(normalizedPath)");
        windowSource.Should().Contain("ShowOpenIssue(message);");
        windowSource.Should().Contain("await OpenWorkbookPathAsync(path!, fileAccessIdentity)");
    }

    [Fact]
    public void MainWindow_WiresDroppedWorkbookFilesToSharedOpenPipeline()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var ingressPlannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookOpenIngressPlanner.cs"));

        source.Should().Contain("ConfigureWorkbookDropTarget();");
        source.Should().Contain("DragDrop.SetAllowDrop(this, true);");
        source.Should().Contain("DragDrop.AddDragOverHandler(this, MainWindow_DragOver);");
        source.Should().Contain("DragDrop.AddDropHandler(this, MainWindow_Drop);");
        source.Should().Contain("e.DataTransfer.TryGetFiles()");
        source.Should().Contain("TrySelectOpenableLocalWorkbookPath(files, out path, out storageItem, out message)");
        source.Should().Contain("file.TryGetLocalPath()");
        source.Should().Contain("_isOpening || _isSaving");
        source.Should().Contain("_session.IsDirty");
        source.Should().Contain("WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(");
        source.Should().Contain("_session.TryResolveOpenTarget(candidatePath, out var target, out var unsupportedMessage)");
        source.Should().Contain("path = plan.Path;");
        source.Should().Contain("storageItem = candidates[plan.CandidateIndex].StorageItem;");
        ingressPlannerSource.Should().Contain("LocalFilePath.TryNormalize(candidatePath, out var normalizedPath)");
        ingressPlannerSource.Should().Contain("Directory.Exists(normalizedPath)");
        ingressPlannerSource.Should().Contain("File.Exists(normalizedPath)");
        ingressPlannerSource.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(adapters, path");
        source.Should().Contain("ShowOpenIssue(message)");
        source.Should().Contain("await OpenWorkbookPathAsync(path!, fileAccessIdentity)");
        source.Should().Contain("await OpenWorkbookFromTargetAsync(target!)");
        source.Should().Contain("DragDropEffects.Copy");
        source.Should().Contain("DragDropEffects.None");
    }

    [Fact]
    public void MainWindow_WiresWorkbookFileAccessServiceToAvaloniaBookmarks()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var serviceSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "WorkbookFileAccessService.cs"));

        source.Should().Contain("private readonly IWorkbookFileAccessService _workbookFileAccessService;");
        source.Should().Contain("WorkbookFileAccessServiceFactory.Create(App.Diagnostics)");
        source.Should().Contain("ArgumentNullException.ThrowIfNull(workbookFileAccessService);");
        source.Should().Contain("_workbookFileAccessService = workbookFileAccessService;");
        source.Should().Contain("pickedStorageFile.StorageFile");
        source.Should().Contain("TrySelectOpenableLocalWorkbookPath(files, out var path, out var storageItem, out var message)");
        source.Should().Contain("TrySelectDroppedWorkbookPath(e, out var path, out var storageItem, out var message)");
        source.Should().Contain("storageItem = candidates[plan.CandidateIndex].StorageItem;");
        source.Should().Contain("await _workbookFileAccessService.BeginAccessAsync(");
        source.Should().Contain("StorageProvider,");
        source.Should().Contain("target.FileAccessIdentity");
        source.Should().Contain("fileAccessIdentity ??= await _workbookFileAccessService.CreateIdentityAsync(");
        source.Should().Contain("PrepareAsync: async _ =>");
        source.Should().Contain("ApplyCompletion: plan => _session.ApplySaveCompletion(plan)");
        source.Should().Contain("_fileWorkflow.RegisterRecentFile(");

        var recentBlock = ExtractSourceBlock(
            source,
            "private async Task OpenRecentWorkbookAsync(",
            "await OpenWorkbookPathAsync(target.Path, target.FileAccessIdentity);");
        recentBlock.Should().Contain("await _workbookFileAccessService.BeginAccessAsync(");
        recentBlock.Should().Contain("!File.Exists(target.Path)");

        serviceSource.Should().Contain("internal interface IWorkbookFileAccessService");
        serviceSource.Should().Contain("internal static class WorkbookFileAccessServiceFactory");
        serviceSource.Should().Contain("internal sealed class AvaloniaWorkbookFileAccessService : IWorkbookFileAccessService");
        serviceSource.Should().Contain("MacOsSecurityScopedBookmarkKind = \"macos-security-scoped-bookmark\"");
        serviceSource.Should().Contain("IStorageItem? storageItem = null");
        serviceSource.Should().Contain("OperatingSystem.IsMacOS()");
        serviceSource.Should().Contain("StorageItemMatchesPath(storageItem, path)");
        serviceSource.Should().Contain("storageItem.SaveBookmarkAsync()");
        serviceSource.Should().Contain("storageProvider.OpenFileBookmarkAsync(bookmark)");
        serviceSource.Should().Contain("WorkbookFileAccessScope.FromDisposable(");
        serviceSource.Should().Contain("PlatformPathIdentityComparer.Current.Equals(identity.LocalPath, resolvedPath)");
        serviceSource.Should().Contain("Create(AvaloniaAppDiagnostics? diagnostics = null)");
        serviceSource.Should().Contain("new AvaloniaWorkbookFileAccessService(diagnostics)");
        serviceSource.Should().Contain("AvaloniaWorkbookFileAccessService(AvaloniaAppDiagnostics? diagnostics = null)");
        serviceSource.Should().Contain("_diagnostics?.RecordEvent(eventName");
        serviceSource.Should().Contain("RecordIdentityEvent(\"bookmark_created\", grantKind: MacOsSecurityScopedBookmarkKind);");
        serviceSource.Should().Contain("RecordScopeEvent(\"scope_started\", grantKind: MacOsSecurityScopedBookmarkKind);");
        serviceSource.Should().Contain("RecordScopeEvent(\"scope_ended\", grantKind: MacOsSecurityScopedBookmarkKind)");
        serviceSource.Should().Contain("workbook_file_access_identity");
        serviceSource.Should().Contain("workbook_file_access_scope");
        serviceSource.Should().Contain("[\"grantKind\"]");
        serviceSource.Should().Contain("[\"payloadRedacted\"] = string.IsNullOrWhiteSpace(grantKind) ? null : \"true\"");
        var diagnosticEventBlock = ExtractSourceBlock(
            serviceSource,
            "private void RecordFileAccessEvent(string eventName, string status, string? grantKind)",
            "});");
        diagnosticEventBlock.Should().NotContain("[\"path\"]");
        diagnosticEventBlock.Should().NotContain("[\"fileName\"]");
        diagnosticEventBlock.Should().NotContain("[\"filename\"]");
        diagnosticEventBlock.Should().NotContain("[\"localPath\"]");
        diagnosticEventBlock.Should().NotContain("[\"workbookPath\"]");
        diagnosticEventBlock.Should().NotContain("[\"formula\"]");
        diagnosticEventBlock.Should().NotContain("[\"bookmarkPayload\"]");
        diagnosticEventBlock.Should().NotContain("[\"storageIdentifier\"]");
        diagnosticEventBlock.Should().NotContain("[\"rawStorageIdentifier\"]");
        serviceSource.Should().NotContain("AppKit");
        serviceSource.Should().NotContain("Foundation");
        serviceSource.Should().NotContain("ObjCRuntime");
        serviceSource.Should().NotContain("NSUrl");
        serviceSource.Should().NotContain("NSData");
        serviceSource.Should().NotContain("NSError");
        serviceSource.Should().NotContain("NSOpenPanel");
        serviceSource.Should().NotContain("NSSavePanel");
    }

    [Fact]
    public void MainWindow_WiresWorkbookSharePlannerToAvaloniaFallback()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var serviceSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "WorkbookShareSheetService.cs"));
        var macOsServiceSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOs", "MacOsWorkbookShareSheetService.cs"));

        source.Should().Contain("private const string WorkbookShareSheetLabel = \"macOS Share Sheet\";");
        source.Should().Contain("private readonly IWorkbookShareSheetService _workbookShareSheetService;");
        source.Should().Contain("WorkbookShareSheetServiceFactory.Create(WorkbookShareSheetLabel),");
        source.Should().Contain("WorkbookFileAccessServiceFactory.Create(App.Diagnostics),");
        source.Should().Contain("ArgumentNullException.ThrowIfNull(workbookShareSheetService);");
        source.Should().Contain("private readonly NativeMenuItem _shareWorkbookMenuItem = new();");
        source.Should().Contain("ConfigureNativeFileMenuItem(_shareWorkbookMenuItem, NativeFileMenuItemId.ShareWorkbook);");
        catalogSource.Should().Contain("NativeFileMenuItemId.ShareWorkbook");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_ShareWorkbook\"");
        source.Should().Contain("_shareWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Share);");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.ShareWorkbook)");
        source.Should().Contain("ApplyNativeFileMenuAvailability(isIdle);");
        source.Should().Contain("HasNativeShareWorkbookMenuItem: HasEnabledNativeFileMenuItem(_shareWorkbookMenuItem, NativeFileMenuItemId.ShareWorkbook)");
        source.Should().Contain("private static bool HasEnabledNativeFileMenuItem(NativeMenuItem item, NativeFileMenuItemId id)");
        source.Should().Contain("private async Task ShareWorkbookAsync()");
        source.Should().Contain("WorkbookShareActionPlanner.CreatePlan(");
        source.Should().Contain("_session.CurrentFilePath");
        source.Should().Contain("new(");
        source.Should().Contain("CanOpenContainingFolder: TopLevel.GetTopLevel(this)?.Launcher is not null");
        var shareSurfaceBlock = ExtractSourceBlock(
            source,
            "private WorkbookShareActionSurface CreateWorkbookShareActionSurface()",
            "OpenContainingFolderLabel: GetWorkbookShareOpenContainingFolderLabel());");
        shareSurfaceBlock.Should().Contain("var capability = _workbookShareSheetService.Capability;");
        shareSurfaceBlock.Should().Contain("capability.ShareSheetLabel");
        shareSurfaceBlock.Should().Contain("CanShowShareSheet: capability.CanShowShareSheet");
        shareSurfaceBlock.Should().NotContain("CanShowShareSheet: false");
        source.Should().Contain("OperatingSystem.IsMacOS()");
        source.Should().Contain("? \"Reveal in Finder\"");
        source.Should().Contain(": \"Open Containing Folder\"");
        source.Should().Contain("case WorkbookShareActionPlanKind.SaveAsBeforeShare:");
        source.Should().Contain("await SaveWorkbookAsAsync();");
        source.Should().Contain("case WorkbookShareActionPlanKind.OpenContainingFolder:");
        source.Should().Contain("await TrySaveDirtyWorkbookForShareAsync()");
        source.Should().Contain("await OpenWorkbookContainingFolderAsync(refreshedPlan);");
        source.Should().Contain("case WorkbookShareActionPlanKind.ShareSheet:");
        source.Should().Contain("await ShowWorkbookShareSheetAsync(plan);");
        source.Should().Contain("private async Task ShowWorkbookShareSheetAsync(WorkbookShareActionPlan plan)");
        source.Should().Contain("if (refreshedPlan.Kind != WorkbookShareActionPlanKind.ShareSheet)");
        source.Should().Contain("await _workbookShareSheetService.ShowShareSheetAsync(this, filePath)");
        source.Should().Contain("await FallbackToOpenContainingFolderAfterShareSheetFailureAsync(refreshedPlan);");
        source.Should().Contain("with { CanShowShareSheet = false }");
        source.Should().Contain("await OpenWorkbookContainingFolderAsync(fallbackPlan);");
        source.Should().Contain("case WorkbookShareActionPlanKind.Deferred:");
        source.Should().Contain("WorkbookShareActionPlanner.FormatStatus(plan)");
        source.Should().Contain("TopLevel.GetTopLevel(this)?.Launcher");
        source.Should().Contain("await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folderPath))");
        source.Should().Contain("WorkbookShareActionUnavailableReason.ContainingFolderUnavailable");

        serviceSource.Should().Contain("internal sealed record WorkbookShareSheetCapability(");
        serviceSource.Should().Contain("internal interface IWorkbookShareSheetService");
        serviceSource.Should().Contain("Task<WorkbookShareSheetResult> ShowShareSheetAsync(Window owner, string filePath);");
        serviceSource.Should().Contain("internal static class WorkbookShareSheetServiceFactory");
        serviceSource.Should().Contain("#if FREEX_MACOS_SHARE_SHEET");
        serviceSource.Should().Contain("return new MacOsWorkbookShareSheetService(shareSheetLabel);");
        serviceSource.Should().Contain("internal sealed class UnavailableWorkbookShareSheetService : IWorkbookShareSheetService");
        serviceSource.Should().Contain("Capability = new WorkbookShareSheetCapability(shareSheetLabel, CanShowShareSheet: false);");
        serviceSource.Should().Contain("WorkbookShareSheetResult.Unavailable(_unavailableMessage)");
        macOsServiceSource.Should().Contain("internal sealed class MacOsWorkbookShareSheetService : IWorkbookShareSheetService");
        macOsServiceSource.Should().Contain("Capability = new WorkbookShareSheetCapability(shareSheetLabel, CanShowShareSheet: true);");
        macOsServiceSource.Should().Contain("NSSharingServicePicker");
        macOsServiceSource.Should().Contain("NSUrl.FromFilename(filePath)");
        macOsServiceSource.Should().Contain("owner.TryGetPlatformHandle()");
        macOsServiceSource.Should().Contain("platformHandle?.HandleDescriptor != \"NSWindow\"");
        macOsServiceSource.Should().Contain("Runtime.GetNSObject<NSWindow>(platformHandle.Handle)");
        macOsServiceSource.Should().Contain("ShowRelativeToRect(anchorView.Bounds, anchorView, NSRectEdge.MinYEdge)");

        source.Should().NotContain("DataTransferManager");
        source.Should().NotContain("WindowInteropHelper");
        source.Should().NotContain("Microsoft.Win32");
        source.Should().NotContain("ProcessStartInfo");
        source.Should().NotContain("System.Windows");
        serviceSource.Should().NotContain("AppKit");
        serviceSource.Should().NotContain("Foundation");
        serviceSource.Should().NotContain("ObjCRuntime");
        serviceSource.Should().NotContain("NSSharingService");
        serviceSource.Should().NotContain("NSSharingServicePicker");
        serviceSource.Should().NotContain("DataTransferManager");
        serviceSource.Should().NotContain("WindowInteropHelper");
        serviceSource.Should().NotContain("Microsoft.Win32");
        serviceSource.Should().NotContain("ProcessStartInfo");
        serviceSource.Should().NotContain("System.Windows");
        macOsServiceSource.Should().NotContain("DataTransferManager");
        macOsServiceSource.Should().NotContain("WindowInteropHelper");
        macOsServiceSource.Should().NotContain("Microsoft.Win32");
        macOsServiceSource.Should().NotContain("ProcessStartInfo");
        macOsServiceSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresPortablePdfExportToNativeFileMenu()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var exporterSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "PortablePdfDocumentExporter.cs"));

        source.Should().Contain("private readonly NativeMenuItem _exportPdfMenuItem = new();");
        source.Should().Contain("ConfigureNativeFileMenuItem(_exportPdfMenuItem, NativeFileMenuItemId.ExportPdf);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_ExportPdf\"");
        source.Should().Contain("_exportPdfMenuItem.Click += async (_, _) => await ExportActiveSheetPdfAsync();");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.ExportPdf)");
        catalogSource.Should().Contain("new(NativeFileMenuItemId.ExportPdf, context.IsIdle && context.CanSaveThroughStorageProvider)");
        source.Should().Contain("private async Task ExportActiveSheetPdfAsync()");
        source.Should().Contain("var storageFile = await ShowPortablePdfSavePickerAsync(\"Export to PDF\");");
        source.Should().Contain("private Task<AvaloniaPickedStorageFile?> ShowPortablePdfSavePickerAsync(string title)");
        source.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfPickerPlan(_session.DisplayName, ApplicationTitle)");
        source.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        source.Should().Contain("AvaloniaFilePickerSaveRequest.FromDescriptors(");
        source.Should().Contain("pickerPlan.SuggestedFileName");
        source.Should().Contain("pickerPlan.DefaultExtensionWithoutDot");
        source.Should().Contain("showOverwritePrompt: true");
        source.Should().Contain("suggestFirstFileType: true");
        source.Should().Contain("storageFile.LocalPath");
        source.Should().Contain("var exportOptions = await ShowExportOptionsDialogAsync(ExportContentScope.ActiveSheet, ExportFormat.Pdf);");
        source.Should().Contain("var exportOptions = await ShowExportOptionsDialogAsync(ToExportContentScope(scope), ExportFormat.Pdf);");
        source.Should().Contain("CreatePortablePdfPrintPlan(exportOptions, WorkbookExportPrintOutputKind.Pdf)");
        source.Should().Contain("CreatePortablePdfPrintPlan(exportOptions, outputKind)");
        source.Should().Contain("TryPreparePortablePdfExportPlan(exportPlan, exportOptions, out var effectiveExportPlan, out var optionsError)");
        source.Should().Contain("if (exportOptions.OpenAfterPublish)");
        source.Should().Contain("await TryOpenExportedPdfAsync(path);");
        source.Should().Contain("var exportTargetPlan = ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists);");
        source.Should().Contain("exportTargetPlan.ShouldConfirmNormalizedOverwrite");
        source.Should().Contain("!await ConfirmNormalizedPdfOverwriteAsync(exportTargetPlan.Path)");
        source.Should().Contain("path = exportTargetPlan.Path;");
        source.Should().Contain("private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)");
        var normalizedOverwriteDialog = ExtractSourceBlock(
            source,
            "private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)",
            "await dialog.ShowDialog(this);");
        normalizedOverwriteDialog.Should().NotContain("IsDefault = true,");
        normalizedOverwriteDialog.Should().Contain("IsCancel = true,");
        normalizedOverwriteDialog.Should().Contain("dialog.Opened += (_, _) => cancelButton.Focus();");
        source.Should().Contain("AutomationProperties.SetAutomationId(replaceButton, \"PdfExportOverwriteReplaceButton\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"PdfExportOverwriteCancelButton\")");
        // The PDF export plan is created via the page-setup-aware overload (honours paper size / margins /
        // fit-to-page); the test accepts either the legacy or the new entry point so a future refactor does not
        // break a purely cosmetic name constraint.
        (source.Contains("WorkbookExportPrintPlanner.CreatePlan(") ||
         source.Contains("WorkbookExportPrintPlanner.CreatePlanFromPageSetup("))
            .Should().BeTrue("MainWindow must call WorkbookExportPrintPlanner to build the export-print plan");
        source.Should().Contain("WorkbookExportPrintSurface.MacOs");
        source.Should().Contain("PortablePdfExportPlanner.CreatePlan(exportPrintPlan)");
        // The menu handler routes through a single PDF export seam; the Skia-vs-portable decision lives there.
        // The real saved-file directory is threaded through so &Z/&[Path] header/footer tokens resolve
        // (R15-header-footer-print-titles-2) instead of always expanding to "".
        source.Should().Contain("Pdf.AvaloniaPdfDocumentExporter.Save(_session.Workbook, effectiveExportPlan, pdfBuffer, options: null, workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter())");
        var pdfRouterSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Pdf", "AvaloniaPdfDocumentExporter.cs"));
        // Unicode-capable export goes through Skia (auto font embedding); portable WinAnsi is the fallback.
        pdfRouterSource.Should().Contain("SkiaPdfDocumentExporter.Save(workbook, exportPlan, stream");
        pdfRouterSource.Should().Contain("PortablePdfDocumentExporter.Save(workbook, exportPlan, stream");
        source.Should().Contain("HasNativeExportPdfMenuItem: HasNativeFileMenuItem(_exportPdfMenuItem, NativeFileMenuItemId.ExportPdf)");

        smokeSource.Should().Contain("bool HasNativeExportPdfMenuItem,");
        smokeSource.Should().Contain("HasNativeExportPdfMenuItem &&");
        smokeSource.Should().Contain("native_export_pdf_menu_item={FormatBool(snapshot.HasNativeExportPdfMenuItem)}");

        exporterSource.Should().Contain("public static class PortablePdfDocumentExporter");
        // The WinAnsi byte format now lives in the shared Free.Shared.Pdf tier; the FreeX exporter
        // builds the app-agnostic draw-op model (via WorkbookPdfContentBuilder) and delegates byte
        // emission to the shared writer.
        exporterSource.Should().Contain("WorkbookPdfContentBuilder.Build(workbook, exportPlan, options)");
        exporterSource.Should().Contain("PortablePdfWriter.WriteToBytes(document, \"FreeX portable PDF\")");
        var builderSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookPdfContentBuilder.cs"));
        builderSource.Should().Contain("PortablePdfPageContentPlanner.CreatePlan(workbook, request)");
        exporterSource.Should().NotContain("/Encoding /Identity-H");
        exporterSource.Should().NotContain("/ArialMT");
        exporterSource.Should().NotContain("System.Windows");
        exporterSource.Should().NotContain("Microsoft.Win32");

        // The dependency-free WinAnsi guarantees are now proven on the shared writer source.
        var sharedWriterSource = File.ReadAllText(RepositoryFileLocator.Find("shared", "Free.Shared.Pdf", "PortablePdfWriter.cs"));
        sharedWriterSource.Should().Contain("/Encoding /WinAnsiEncoding");
        sharedWriterSource.Should().Contain("EncodeWinAnsiHexText(normalized)");
        sharedWriterSource.Should().Contain("private static byte EncodeWinAnsiByte(char ch)");
        sharedWriterSource.Should().Contain("built-in Helvetica/WinAnsi set");
        sharedWriterSource.Should().NotContain("/Encoding /Identity-H");
        sharedWriterSource.Should().NotContain("/ArialMT");
        sharedWriterSource.Should().NotContain("System.Windows");
        sharedWriterSource.Should().NotContain("Microsoft.Win32");
    }

    [Fact]
    public void MainWindow_PrintFallbackGuardsNormalizedPdfOverwriteAndCupsTimeouts()
    {
        var printSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Print.cs"));
        var cupsSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "CupsPlatformPrinter.cs"));

        printSource.Should().Contain("var exportTargetPlan = ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists);");
        printSource.Should().Contain("exportTargetPlan.ShouldConfirmNormalizedOverwrite");
        printSource.Should().Contain("!await ConfirmNormalizedPdfOverwriteAsync(exportTargetPlan.Path)");
        printSource.Should().Contain("UiText.Get(\"Print_SaveCanceled\")");
        printSource.Should().Contain("ShowPortablePdfSavePickerAsync(UiText.Get(\"Print_SaveAsPdfButton\"))");

        cupsSource.Should().Contain("private static readonly TimeSpan CommandTimeout");
        cupsSource.Should().Contain("timeout.CancelAfter(CommandTimeout)");
        cupsSource.Should().Contain("process.Kill(entireProcessTree: true)");
        cupsSource.Should().Contain("catch (TimeoutException)");
    }

    [Fact]
    public void MainWindow_FilePickersAreGuardedBeforeDialogsAndSaveAsConfirmsNormalizedOverwrite()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var printSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Print.cs"));

        source.Should().Contain("private bool TryBeginFileOperation()");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("_isSaving = true;");
        source.Should().Contain("private void EndFileOperation()");
        source.Should().Contain("WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(");
        source.Should().NotContain("private static bool ShouldPromptForNormalizedWorkbookOverwrite(");
        source.Should().NotContain("Path.GetFullPath(requestedPath)");

        var saveAsBlock = ExtractSourceBlock(
            source,
            "private async Task<bool> SaveWorkbookAsAsync()",
            "return await SaveWorkbookToTargetAsync(target!, fileAccessIdentity);");
        saveAsBlock.Should().Contain("if (!TryBeginFileOperation())");
        saveAsBlock.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        saveAsBlock.IndexOf("if (!TryBeginFileOperation())", StringComparison.Ordinal)
            .Should().BeLessThan(saveAsBlock.IndexOf("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync", StringComparison.Ordinal));
        saveAsBlock.Should().Contain("var pathPlan = WorkbookFileLifecycleCoordinator.PlanSavePathNormalization(");
        saveAsBlock.Should().Contain("pathPlan.ShouldConfirmOverwrite");
        saveAsBlock.Should().Contain("!await ConfirmNormalizedWorkbookOverwriteAsync(pathPlan.Path)");
        saveAsBlock.Should().Contain("path = pathPlan.Path;");
        source.Should().Contain("AutomationProperties.SetAutomationId(replaceButton, \"WorkbookSaveOverwriteReplaceButton\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"WorkbookSaveOverwriteCancelButton\")");

        var activeSheetExportBlock = ExtractSourceBlock(
            source,
            "private async Task ExportActiveSheetPdfAsync()",
            "EndFileOperation();");
        activeSheetExportBlock.Should().Contain("if (!TryBeginFileOperation())");
        activeSheetExportBlock.IndexOf("if (!TryBeginFileOperation())", StringComparison.Ordinal)
            .Should().BeLessThan(activeSheetExportBlock.IndexOf("ShowPortablePdfSavePickerAsync", StringComparison.Ordinal));

        var workbookExportBlock = ExtractSourceBlock(
            source,
            "private async Task ExportWorkbookPdfAsync(",
            "EndFileOperation();");
        workbookExportBlock.Should().Contain("if (!TryBeginFileOperation())");
        workbookExportBlock.IndexOf("if (!TryBeginFileOperation())", StringComparison.Ordinal)
            .Should().BeLessThan(workbookExportBlock.IndexOf("ShowPortablePdfSavePickerAsync", StringComparison.Ordinal));

        var printSaveBlock = ExtractSourceBlock(
            printSource,
            "private async Task SavePrintReadyPdfAsync(byte[] documentBytes)",
            "EndFileOperation();");
        printSaveBlock.Should().Contain("if (!TryBeginFileOperation())");
        printSaveBlock.IndexOf("if (!TryBeginFileOperation())", StringComparison.Ordinal)
            .Should().BeLessThan(printSaveBlock.IndexOf("ShowPortablePdfSavePickerAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_RoutesAccountingMenuChoicesToDistinctCurrencySymbols()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var menuSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.Ribbon.Definitions", "HomeRibbonMenus.g.cs"));

        menuSource.Should().Contain("m.Item(\"Accounting Number Format US Dollar\", \"US Dollar ($)\", \"D\")");
        menuSource.Should().Contain(".Item(\"Accounting Number Format Euro\", \"Euro");
        menuSource.Should().Contain(".Item(\"Accounting Number Format British Pound\", \"British Pound");
        menuSource.Should().Contain(".Item(\"Accounting Number Format Japanese Yen\", \"Japanese Yen");
        menuSource.Should().NotContain(".Item(\"Accounting Number Format\", \"Euro");
        menuSource.Should().NotContain(".Item(\"Accounting Number Format\", \"British Pound");
        menuSource.Should().NotContain(".Item(\"Accounting Number Format\", \"Japanese Yen");

        source.Should().Contain("[\"Accounting Number Format US Dollar\"] = () => ApplySelectedRangeAccountingFormat(\"$\")");
        source.Should().Contain("[\"Accounting Number Format Euro\"] = () => ApplySelectedRangeAccountingFormat(\"\\u20AC\")");
        source.Should().Contain("[\"Accounting Number Format British Pound\"] = () => ApplySelectedRangeAccountingFormat(\"\\u00A3\")");
        source.Should().Contain("[\"Accounting Number Format Japanese Yen\"] = () => ApplySelectedRangeAccountingFormat(\"\\u00A5\")");
    }

    [Fact]
    public void MainWindow_NativeFileMenuLabelsUseLocalizedResources()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var neutralResources = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Localization", "Resources", "Strings.resx"));
        var frenchResources = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Localization", "Resources", "Strings.fr-FR.resx"));

        var fileMenuKeys = new[]
        {
            "AvaloniaNativeMenu_NewWorkbook",
            "AvaloniaNativeMenu_Open",
            "AvaloniaNativeMenu_OpenRecent",
            "AvaloniaNativeMenu_Save",
            "AvaloniaNativeMenu_SaveAs",
            "AvaloniaNativeMenu_ExportPdf",
            "AvaloniaNativeMenu_PageSetup",
            "AvaloniaNativeMenu_PrintPreview",
            "AvaloniaNativeMenu_ShareWorkbook",
            "AvaloniaNativeMenu_WorkbookStatistics",
            "AvaloniaNativeMenu_CloseWorkbook",
        };

        foreach (var key in fileMenuKeys)
        {
            catalogSource.Should().Contain($"\"{key}\"");
            neutralResources.Should().Contain($"<data name=\"{key}\"");
            frenchResources.Should().Contain($"<data name=\"{key}\"");
        }

        source.Should().Contain("ConfigureNativeFileMenuItem(_newWorkbookMenuItem, NativeFileMenuItemId.NewWorkbook);");
        source.Should().Contain("GetNativeFileMenuItemHeader(NativeMenuCatalog.GetFileMenuItem(id))");
        source.Should().Contain("plan.UsesResourceKey");
        source.Should().Contain("UiText.Get(plan.Label)");
        catalogSource.Should().Contain("new(NativeMenuItemId.NewSheet, \"AvaloniaNativeMenu_NewSheet\"");
        catalogSource.Should().Contain("UsesResourceKey: true");
        neutralResources.Should().Contain("<data name=\"AvaloniaNativeMenu_NewSheet\"");
        frenchResources.Should().Contain("<data name=\"AvaloniaNativeMenu_NewSheet\"");

        var nativeMenuBlock = ExtractSourceBlock(
            source,
            "private void ConfigureNativeMenu()",
            "_renameSheetMenuItem.Click += async (_, _) => await RenameActiveSheetAsync();");
        nativeMenuBlock.Should().NotContain("_newWorkbookMenuItem.Header = \"New Workbook\"");
        nativeMenuBlock.Should().NotContain("_openMenuItem.Header = \"Open...\"");
        nativeMenuBlock.Should().NotContain("_openRecentMenuItem.Header = \"Open Recent\"");
        nativeMenuBlock.Should().NotContain("_saveMenuItem.Header = \"Save\"");
        nativeMenuBlock.Should().NotContain("_saveAsMenuItem.Header = \"Save As...\"");
        nativeMenuBlock.Should().NotContain("_exportPdfMenuItem.Header = \"Export to PDF...\"");
        nativeMenuBlock.Should().NotContain("_shareWorkbookMenuItem.Header = \"Share Workbook...\"");
        nativeMenuBlock.Should().NotContain("_workbookStatisticsMenuItem.Header = \"Workbook Statistics...\"");
        nativeMenuBlock.Should().NotContain("_closeWorkbookMenuItem.Header = \"Close Workbook\"");
        nativeMenuBlock.Should().NotContain("_newSheetMenuItem.Header = \"New Sheet\"");
        nativeMenuBlock.Should().NotContain("_renameSheetMenuItem.Header = \"Rename Sheet...\"");
    }

    [Fact]
    public void App_UsesSharedReleaseChannelForPrereleaseUpdatePolicy()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        source.Should().Contain("UpdateFeed.AllowPrereleases(AppHelpInfo.ReleaseChannel)");
        source.Should().Contain("releasesPageUrl: AppHelpInfo.LatestReleaseUrl");
        source.Should().NotContain("UpdateFeed.AllowPrereleases(\"test\")");
    }

    [Fact]
    public void MainWindow_WiresOptionsDialogToSharedAppOptionsStore()
    {
        var menuSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var optionsSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        // Wired into the native File menu with an Options entry that routes through the shared
        // Backstage workflow planner before the platform executor calls ShowOptions.
        menuSource.Should().Contain("private readonly NativeMenuItem _optionsMenuItem = new();");
        menuSource.Should().Contain("ConfigureNativeFileMenuItem(_optionsMenuItem, NativeFileMenuItemId.Options);");
        catalogSource.Should().Contain("\"Options_Title\"");
        menuSource.Should().Contain("_optionsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.Options);");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.Options)");

        // The dialog edits the shared AppOptions via the portable store and planner — no bespoke model.
        optionsSource.Should().Contain("var current = App.ParityCaptureOptions is null");
        optionsSource.Should().Contain("? AppOptionsStore.Load()");
        optionsSource.Should().Contain(": OptionsDialogParityFixture.Create();");
        optionsSource.Should().Contain("OptionsDialogPlanner.TryBuildInput(");
        optionsSource.Should().Contain("var projected = OptionsDialogPlanner.Project(current, input);");
        // R124-avalonia-options-multiwindow-lastwriter: the OK handler reloads the freshest on-disk
        // options and merges onto it only the fields this dialog session actually edited (see
        // OptionsDialogPlanner.MergeOntoFreshLoad), instead of saving `projected` -- built purely from
        // this dialog's open-time snapshot -- as the whole document. Saving `projected` directly would
        // silently discard whatever another window (or this window's own reload-before-mutate context
        // menus) persisted while this dialog was open.
        optionsSource.Should().Contain("var merged = OptionsDialogPlanner.MergeOntoFreshLoad(AppOptionsStore.Load(), current, projected);");
        optionsSource.Should().Contain("AppOptionsStore.Save(merged)");
        optionsSource.Should().NotContain("AppOptionsStore.Save(projected)");
        optionsSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"OptionsDialog\");");
        optionsSource.Should().Contain("const double optionsDialogWidth = OptionsDialogPlanner.CaptureWidth;");
        optionsSource.Should().Contain("const double optionsDialogHeight = OptionsDialogPlanner.CaptureHeight;");
        optionsSource.Should().Contain("const double optionsFormulasDialogWidth = OptionsDialogPlanner.CaptureWidth;");
        optionsSource.Should().Contain("const double optionsFormulasDialogHeight = OptionsDialogPlanner.FormulasCaptureHeight;");

        // Advanced ▸ Editing options: the "After pressing Enter, move selection" toggle and its
        // direction picker are now live-bound to the persisted AppOptions (previously shipped
        // disabled/hardcoded to Down), matching the WPF host's OptionsDialog.
        optionsSource.Should().Contain("isChecked: current.MoveSelectionAfterEnter");
        optionsSource.Should().Contain("AutomationProperties.SetAutomationId(moveAfterEnterBox, \"OptionsMoveSelectionAfterEnterCheckBox\");");
        optionsSource.Should().Contain("selectedIndex: OptionsDialogPlanner.AfterEnterDirectionToIndex(current.AfterEnterDirection)");
        optionsSource.Should().Contain("isEnabled: current.MoveSelectionAfterEnter");
        optionsSource.Should().Contain("AutomationProperties.SetAutomationId(afterEnterDirectionBox, \"OptionsAfterEnterDirectionComboBox\");");
        optionsSource.Should().Contain("moveAfterEnterBox.IsCheckedChanged +=");
        optionsSource.Should().Contain("afterEnterDirectionBox.IsEnabled = moveAfterEnterBox.IsChecked == true;");
        optionsSource.Should().Contain("moveAfterEnterBox.IsChecked == true,");
        optionsSource.Should().Contain("OptionsDialogPlanner.IndexToAfterEnterDirection(afterEnterDirectionBox.SelectedIndex)");
        optionsSource.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"OptionsOkButton\");");
        optionsSource.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"OptionsCancelButton\");");
        optionsSource.Should().Contain("OptionsText(\"Options_CategoryQuickAccessToolbar\")");
        optionsSource.Should().Contain("OptionsText(\"Options_CategoryTrustCenter\")");
        optionsSource.Should().NotContain("OptionsApplyButton");

        // Live application of the cheap view/calc settings to the running session.
        optionsSource.Should().Contain("private void ApplyLiveOptions(OptionsDialogPlanner.OptionsDialogInput input)");
        optionsSource.Should().Contain("_session.SetShowGridlines(input.ShowGridlines);");
        optionsSource.Should().Contain("_session.SetShowHeadings(input.ShowHeadings);");
        optionsSource.Should().Contain("FormulaErrorCheckingRuleCatalog.SupportedRules");
        optionsSource.Should().Contain("workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode)");
        optionsSource.Should().Contain("new SetFormulaErrorCheckingRuleCommand(rule.ErrorCode, enabled: !shouldDisable)");

        // PresentationPortabilityGuard forbids these tokens in portable shell source — make sure we stayed clean.
        optionsSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeFileMenuToSharedOpenSavePipeline()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var legalNoticesAdapterSource = File.ReadAllText(
            RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "LegalNoticesDialog.cs"));
        var sharedLegalNoticesSource = File.ReadAllText(
            RepositoryFileLocator.Find("shared", "Free.Shared.Shell.Avalonia", "AvaloniaLegalNoticesDialog.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.BackstageWorkflow.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("ConfigureNativeMenu();");
        source.Should().Contain("using FreeX.App.Presentation.Backstage;");
        source.Should().Contain("private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();");
        source.Should().Contain("private readonly NativeMenuItem _newWorkbookMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _openMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _openRecentMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _saveMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _saveAsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _closeWorkbookMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _undoMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _redoMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _cutMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _copyMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _pasteMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _pasteSpecialMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearContentsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _boldMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _italicMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _underlineMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _doubleUnderlineMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _strikethroughMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseFontSizeMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseFontSizeMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillColorMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearFillMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fontColorMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _horizontalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleCounterclockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleClockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _verticalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextUpMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextDownMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _currencyFormatMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _percentFormatMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _commaStyleMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseDecimalMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseDecimalMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignLeftMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignCenterMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignRightMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignTopMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignMiddleMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignBottomMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _wrapTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseIndentMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseIndentMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _helpOnlineMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _sendFeedbackMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _checkForUpdatesMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _aboutMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _legalNoticesMenuItem = new();");
        source.Should().Contain("ConfigureNativeFileMenuItem(_newWorkbookMenuItem, NativeFileMenuItemId.NewWorkbook);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_NewWorkbook\"");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.NewWorkbook)");
        source.Should().Contain("_newWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);");
        source.Should().Contain("ConfigureNativeFileMenuItem(_openMenuItem, NativeFileMenuItemId.Open);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_Open\"");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.OpenWorkbook)");
        source.Should().Contain("_openMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Open);");
        source.Should().Contain("ConfigureNativeFileMenuItem(_openRecentMenuItem, NativeFileMenuItemId.OpenRecent);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_OpenRecent\"");
        source.Should().Contain("_openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);");
        source.Should().Contain("ConfigureNativeFileMenuItem(_saveMenuItem, NativeFileMenuItemId.Save);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_Save\"");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.SaveWorkbook)");
        source.Should().Contain("_saveMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Save);");
        source.Should().Contain("ConfigureNativeFileMenuItem(_saveAsMenuItem, NativeFileMenuItemId.SaveAs);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_SaveAs\"");
        catalogSource.Should().Contain("NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift");
        source.Should().Contain("_saveAsMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.SaveAs);");
        source.Should().Contain("ConfigureNativeFileMenuItem(_closeWorkbookMenuItem, NativeFileMenuItemId.CloseWorkbook);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_CloseWorkbook\"");
        catalogSource.Should().Contain("new NativeMenuGesturePlan(NativeMenuGestureKey.W, NativeMenuGestureModifiers.Meta)");
        source.Should().Contain("_closeWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Close);");
        source.Should().Contain("_backstageExportMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.BackstageExport);");
        source.Should().Contain("_backstageAccountMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.BackstageAccount);");
        source.Should().Contain("_optionsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.Options);");
        workflowSource.Should().Contain("private Task ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId command)");
        workflowSource.Should().Contain("FreeXBackstageCommandWorkflowExecutor.ExecuteAsync(");
        workflowSource.Should().Contain("CreateBackstageCommandHandlers()");
        workflowSource.Should().Contain("ExportWorkbookAsync: ShowBackstageExportDialogAsync");
        workflowSource.Should().Contain("AccountAsync: ShowBackstageAccountDialogAsync");
        workflowSource.Should().Contain("OptionsAsync: () =>");
        workflowSource.Should().NotContain("FreeXBackstageFlowPlanner.BuildCommandWorkflow(command)");
        workflowSource.Should().NotContain("case FreeXBackstageCommandWorkflowKind.");
        source.Should().Contain("ConfigureNativeCatalogMenuItems();");
        source.Should().Contain("ConfigureNativeMenuItem(GetNativeMenuItem(id), id);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Undo, \"Undo\", NativeMenuGesture(WorkbookShortcutRoute.Undo))");
        source.Should().Contain("_undoMenuItem.Click += (_, _) => UndoLastEdit();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Redo, \"Redo\", NativeMenuGesture(WorkbookShortcutRoute.Redo))");
        source.Should().Contain("_redoMenuItem.Click += (_, _) => RedoLastEdit();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Cut, \"Cut\", NativeMenuGesture(WorkbookShortcutRoute.Cut))");
        source.Should().Contain("_cutMenuItem.Click += async (_, _) => await CutSelectedRangeToClipboardAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Copy, \"Copy\", NativeMenuGesture(WorkbookShortcutRoute.Copy))");
        source.Should().Contain("_copyMenuItem.Click += async (_, _) => await CopySelectedRangeToClipboardAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Paste, \"Paste\", NativeMenuGesture(WorkbookShortcutRoute.Paste))");
        source.Should().Contain("_pasteMenuItem.Click += async (_, _) => await PasteClipboardTextAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.PasteSpecial, \"Paste Special\", NativeMenuGesture(WorkbookShortcutRoute.PasteSpecial))");
        source.Should().Contain("_pasteSpecialMenuItem.Menu = CreateNativePasteSpecialMenu();");
        source.Should().Contain("CreateNativePasteCommentsMenuItem(\"Comments and Notes\")");
        source.Should().Contain("CreateNativePasteDataValidationMenuItem(\"Validation\")");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"All Except Borders\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"All Merging Conditional Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))");
        source.Should().Contain("CreateNativePasteColumnWidthsMenuItem(\"Column Widths\")");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Formulas and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Values and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Values and Source Formatting\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Keep Source Column Widths\", PasteCellsMode.All, default, keepSourceColumnWidths: true)");
        source.Should().Contain("CreateNativePasteLinkMenuItem(\"Paste Link\")");
        source.Should().Contain("CreateNativePasteSpecialTextMenuItem(\"Text\")");
        source.Should().Contain("CreateNativePasteSpecialTextMenuItem(\"Unicode Text\")");
        source.Should().Contain("CreateNativePastePictureMenuItem(\"Picture\", linkedPicture: false)");
        source.Should().Contain("CreateNativePastePictureMenuItem(\"Linked Picture\", linkedPicture: true)");
        catalogSource.Should().Contain("new(NativeMenuItemId.SelectAll, \"Select All\", new NativeMenuGesturePlan(NativeMenuGestureKey.A, NativeMenuGestureModifiers.Meta))");
        source.Should().Contain("_selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();");
        source.Should().Contain("_clearMenuItem.Menu = CreateNativeClearMenu();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Clear, \"Clear\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearAll, \"Clear All\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_clearAllMenuItem.Click += (_, _) => ClearSelectedRangeAll();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearFormats, \"Clear Formats\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_clearFormatsMenuItem.Click += (_, _) => ClearSelectedRangeFormats();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearContents, \"Clear Contents\", new NativeMenuGesturePlan(NativeMenuGestureKey.Delete))");
        source.Should().Contain("_clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearComments, \"Clear Comments and Notes\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_clearCommentsMenuItem.Click += (_, _) => ClearSelectedRangeComments();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearHyperlinks, \"Clear Hyperlinks\", RequiresGestureInSmoke: false)");
        // Same Home > Clear > Clear Hyperlinks semantics as the flyout item above -- strips both the
        // hyperlink and its formatting, so it is wired through RemoveSelectedRangeHyperlinks rather
        // than the format-preserving ClearSelectedRangeHyperlinks.
        source.Should().Contain("_clearHyperlinksMenuItem.Click += (_, _) => RemoveSelectedRangeHyperlinks();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Bold, \"Bold\", NativeMenuGesture(WorkbookShortcutRoute.ToggleBold))");
        source.Should().Contain("_boldMenuItem.Click += (_, _) => ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: true);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Italic, \"Italic\", NativeMenuGesture(WorkbookShortcutRoute.ToggleItalic))");
        source.Should().Contain("_italicMenuItem.Click += (_, _) => ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: true);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Underline, \"Underline\", NativeMenuGesture(WorkbookShortcutRoute.ToggleUnderline))");
        source.Should().Contain("_underlineMenuItem.Click += (_, _) => ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: true);");
        catalogSource.Should().Contain("new(NativeMenuItemId.DoubleUnderline, \"Double Underline\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_doubleUnderlineMenuItem.Click += (_, _) => ToggleSelectedRangeDoubleUnderline();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Strikethrough, \"Strikethrough\", NativeMenuGesture(WorkbookShortcutRoute.ToggleStrikethrough))");
        source.Should().Contain("_strikethroughMenuItem.Click += (_, _) => ToggleSelectedRangeStrikethrough();");
        catalogSource.Should().Contain("new(NativeMenuItemId.IncreaseFontSize, \"Increase Font Size\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_increaseFontSizeMenuItem.Click += (_, _) => IncreaseSelectedRangeFontSize();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DecreaseFontSize, \"Decrease Font Size\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_decreaseFontSizeMenuItem.Click += (_, _) => DecreaseSelectedRangeFontSize();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillColor, \"Fill Color\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_fillColorMenuItem.Menu = CreateNativeColorPaletteMenu(ColorPaletteTarget.Fill, includeClearFill: true);");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearFill, \"No Fill\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_clearFillMenuItem.Click += (_, _) => ClearSelectedRangeFill();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FontColor, \"Font Color\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_fontColorMenuItem.Menu = CreateNativeColorPaletteMenu(ColorPaletteTarget.Font, includeClearFill: false);");
        catalogSource.Should().Contain("new(NativeMenuItemId.HorizontalText, \"Horizontal\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(0, \"Set horizontal text for\", \"Horizontal Text failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.AngleCounterclockwise, \"Angle Counterclockwise\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.AngleClockwise, \"Angle Clockwise\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(-45, \"Angled text clockwise for\", \"Angle Clockwise failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.VerticalText, \"Vertical Text\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(255, \"Set vertical text for\", \"Vertical Text failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.RotateTextUp, \"Rotate Text Up\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(90, \"Rotated text up for\", \"Rotate Text Up failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.RotateTextDown, \"Rotate Text Down\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(-90, \"Rotated text down for\", \"Rotate Text Down failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.CurrencyFormat, \"Accounting Number Format\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_currencyFormatMenuItem.Click += (_, _) => ApplySelectedRangeCurrencyFormat();");
        catalogSource.Should().Contain("new(NativeMenuItemId.PercentFormat, \"Percent Style\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_percentFormatMenuItem.Click += (_, _) => ApplySelectedRangePercentFormat();");
        catalogSource.Should().Contain("new(NativeMenuItemId.CommaStyle, \"Comma Style\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_commaStyleMenuItem.Click += (_, _) => ApplySelectedRangeCommaStyle();");
        catalogSource.Should().Contain("new(NativeMenuItemId.IncreaseDecimal, \"Increase Decimal Places\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_increaseDecimalMenuItem.Click += (_, _) => IncreaseSelectedRangeDecimalPlaces();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DecreaseDecimal, \"Decrease Decimal Places\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_decreaseDecimalMenuItem.Click += (_, _) => DecreaseSelectedRangeDecimalPlaces();");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignTop, \"Align Top\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignTopMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Top);");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignMiddle, \"Align Middle\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignMiddleMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Center);");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignBottom, \"Align Bottom\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignBottomMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);");
        catalogSource.Should().Contain("new(NativeMenuItemId.WrapText, \"Wrap Text\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_wrapTextMenuItem.Click += (_, _) => ToggleSelectedRangeWrapText();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DecreaseIndent, \"Decrease Indent\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_decreaseIndentMenuItem.Click += (_, _) => DecreaseSelectedRangeIndent();");
        catalogSource.Should().Contain("new(NativeMenuItemId.IncreaseIndent, \"Increase Indent\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_increaseIndentMenuItem.Click += (_, _) => IncreaseSelectedRangeIndent();");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignLeft, \"Align Left\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignLeftMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignCenter, \"Align Center\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignCenterMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignRight, \"Align Right\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_alignRightMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);");
        source.Should().Contain("private readonly NativeMenuItem _showGridlinesMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _showHeadingsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _zoomInMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _zoomOutMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _zoom100MenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _zoomToSelectionMenuItem = new();");
        source.Should().Contain("private readonly TextBlock _zoomText = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ShowGridlines, \"Gridlines\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_showGridlinesMenuItem.ToggleType = MenuItemToggleType.CheckBox;");
        source.Should().Contain("_showGridlinesMenuItem.Click += (_, _) => ToggleShowGridlines();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ShowHeadings, \"Headings\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_showHeadingsMenuItem.ToggleType = MenuItemToggleType.CheckBox;");
        source.Should().Contain("_showHeadingsMenuItem.Click += (_, _) => ToggleShowHeadings();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ZoomIn, \"Zoom In\", new NativeMenuGesturePlan(NativeMenuGestureKey.OemPlus, NativeMenuGestureModifiers.Meta))");
        source.Should().Contain("_zoomInMenuItem.Click += (_, _) => ZoomIn();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ZoomOut, \"Zoom Out\", new NativeMenuGesturePlan(NativeMenuGestureKey.OemMinus, NativeMenuGestureModifiers.Meta))");
        source.Should().Contain("_zoomOutMenuItem.Click += (_, _) => ZoomOut();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Zoom100, \"100%\", new NativeMenuGesturePlan(NativeMenuGestureKey.D0, NativeMenuGestureModifiers.Meta))");
        source.Should().Contain("_zoom100MenuItem.Click += (_, _) => ZoomTo100Percent();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ZoomToSelection, \"Zoom to Selection\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_zoomToSelectionMenuItem.Click += (_, _) => ZoomToSelection();");
        source.Should().Contain("private readonly NativeMenuItem _freezePanesMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _freezeTopRowMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _freezeFirstColumnMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _unfreezePanesMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FreezePanes, \"Freeze Panes\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FreezeTopRow, \"Freeze Top Row\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_freezeTopRowMenuItem.Click += (_, _) => FreezeTopRow();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FreezeFirstColumn, \"Freeze First Column\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_freezeFirstColumnMenuItem.Click += (_, _) => FreezeFirstColumn();");
        catalogSource.Should().Contain("new(NativeMenuItemId.UnfreezePanes, \"Unfreeze Panes\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_unfreezePanesMenuItem.Click += (_, _) => UnfreezePanes();");
        source.Should().Contain("private readonly NativeMenuItem _showFormulasMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ShowFormulas, \"Show Formulas\", NativeMenuGesture(WorkbookShortcutRoute.ToggleShowFormulas))");
        source.Should().Contain("_showFormulasMenuItem.ToggleType = MenuItemToggleType.CheckBox;");
        source.Should().Contain("_showFormulasMenuItem.Click += (_, _) => ToggleShowFormulas();");
        source.Should().Contain("var viewMenu = CreateNativeMenu(NativeMenuTopLevelId.View);");
        source.Should().Contain("var formulasMenu = CreateNativeMenu(NativeMenuTopLevelId.Formulas);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ShowFormulas)");
        source.Should().NotContain("_showFormulasMenuItem.Header = \"Show Formulas\";");
        source.Should().NotContain("_showFormulasMenuItem.Gesture = new KeyGesture(Key.Oem3, KeyModifiers.Control);");
        source.Should().NotContain("formulasMenu.Items.Add(_showFormulasMenuItem);");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.View, \"View\")");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FreezePanes, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ShowGridlines, context.IsIdle, context.IsShowingGridlines)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ZoomIn, context.IsIdle && context.CanZoomIn)");
        source.Should().Contain("private void ToggleShowGridlines()");
        source.Should().Contain("var showGridlines = !_session.IsShowingGridlines;");
        source.Should().Contain("var result = _session.SetShowGridlines(showGridlines);");
        source.Should().Contain("RefreshShell(showGridlines ? \"Showing gridlines\" : \"Hiding gridlines\");");
        source.Should().Contain("private void ToggleShowHeadings()");
        source.Should().Contain("var showHeadings = !_session.IsShowingHeadings;");
        source.Should().Contain("var result = _session.SetShowHeadings(showHeadings);");
        source.Should().Contain("RefreshViewportSizeForZoom();");
        source.Should().Contain("RefreshShell(showHeadings ? \"Showing headings\" : \"Hiding headings\");");
        source.Should().Contain("[\"view.zoom\"] = () => _ = ShowZoomDialogAsync(),");
        source.Should().Contain("[\"More\"] = () => _ = ShowZoomDialogAsync(),");
        source.Should().Contain("private void ZoomIn() =>");
        source.Should().Contain("ApplyZoomPercent(_session.ZoomPercent + StatusBarZoomSliderPlanner.ZoomStepPercent, \"Zoom In failed.\")");
        source.Should().Contain("private void ZoomOut() =>");
        source.Should().Contain("ApplyZoomPercent(_session.ZoomPercent - StatusBarZoomSliderPlanner.ZoomStepPercent, \"Zoom Out failed.\")");
        source.Should().Contain("private void ZoomTo100Percent() =>");
        source.Should().Contain("ApplyZoomPercent(100, \"100% Zoom failed.\")");
        source.Should().Contain("private void ZoomToSelection()");
        source.Should().Contain("private async Task ShowZoomDialogAsync()");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ZoomDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, $\"ZoomPreset{zoom}Button\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(fitSelectionButton, \"ZoomFitSelectionButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(customButton, \"ZoomCustomButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(customBox, \"ZoomCustomPercentBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"ZoomOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"ZoomCancelButton\");");
        source.Should().Contain("ZoomDialogPlanner.TryCreateResult(customBox.Text, out var customResult, out var validationError)");
        source.Should().Contain("ResolveZoomDialogValidationError(validationError)");
        source.Should().Contain("selectedZoomPercent = CalculateZoomToSelectionPercent();");
        source.Should().Contain("private void ApplyZoomPercent(int zoomPercent, string errorMessage)");
        source.Should().Contain("var result = _session.SetZoomPercent(zoomPercent);");
        source.Should().Contain("RefreshShell($\"Zoom {StatusBarZoomSliderPlanner.FormatZoomPercent(_session.ZoomPercent)}\");");
        source.Should().Contain("private int CalculateZoomToSelectionPercent()");
        source.Should().Contain("ZoomSelectionPlanner.CalculateFitWholePercent(");
        source.Should().Contain("_zoomText.Text = StatusBarZoomSliderPlanner.FormatZoomPercent(_session.ZoomPercent);");
        catalogSource.Should().Contain("new(NativeMenuItemId.ShowFormulas, context.IsIdle, context.IsShowingFormulas)");
        source.Should().Contain("IsShowingFormulas: _session.IsShowingFormulas");
        source.Should().NotContain("_showFormulasMenuItem.IsEnabled = isIdle;");
        source.Should().NotContain("_showFormulasMenuItem.IsChecked = _session.IsShowingFormulas;");
        source.Should().Contain("private void FreezePanesAtActiveCell()");
        source.Should().Contain("private void FreezeTopRow()");
        source.Should().Contain("private void FreezeFirstColumn()");
        source.Should().Contain("private void UnfreezePanes()");
        source.Should().Contain("private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage)");
        source.Should().Contain("var result = execute();");
        source.Should().Contain("_session.FreezePanesAtActiveCell");
        source.Should().Contain("_session.FreezeTopRow");
        source.Should().Contain("_session.FreezeFirstColumn");
        source.Should().Contain("_session.UnfreezePanes");
        source.Should().Contain("private void ToggleShowFormulas()");
        source.Should().Contain("var showFormulas = !_session.IsShowingFormulas;");
        source.Should().Contain("var result = _session.SetShowFormulas(showFormulas);");
        source.Should().Contain("RefreshShell(showFormulas ? \"Showing formulas\" : \"Showing values\");");
        source.Should().Contain("var showHeadings = _session.IsShowingHeadings;");
        source.Should().Contain("var zoomFactor = GetActiveZoomFactor();");
        source.Should().Contain("var headerOffset = showHeadings ? 1 : 0;");
        source.Should().Contain("if (showHeadings)");
        source.Should().Contain("CellSurfaceGridlinePlanner.HasVisibleFill(");
        source.Should().Contain("BorderBrush = showGridlines ? defaultBorderBrush : Brushes.Transparent");
        source.Should().Contain("CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor)");
        source.Should().Contain("CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor)");
        source.Should().Contain("fontSize * zoomFactor");
        source.Should().Contain("displayHeight / zoomFactor");
        source.Should().Contain("private double GetActiveZoomFactor()");
        source.Should().Contain("TryGetDisplayedDrawingObjectBounds(");
        catalogSource.Should().Contain("new(NativeMenuItemId.HelpOnline, \"Help Online\", new NativeMenuGesturePlan(NativeMenuGestureKey.F1))");
        source.Should().Contain("_helpOnlineMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, \"Help Online\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.SendFeedback, \"Send Feedback\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_sendFeedbackMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, \"Send Feedback\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.CheckForUpdates, \"Check for Updates\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_checkForUpdatesMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, \"Check for Updates\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.About, \"About FreeX\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_aboutMenuItem.Click += async (_, _) => await ShowAboutDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.LegalNotices, \"Legal Notices\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_legalNoticesMenuItem.Click += async (_, _) => await ShowLegalNoticesDialogAsync();");
        source.Should().Contain("ConfigureNativeFileMenuItem(_quitMenuItem, NativeFileMenuItemId.Quit);");
        catalogSource.Should().Contain("\"Quit FreeX\"");
        catalogSource.Should().Contain("new NativeMenuGesturePlan(NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta)");
        source.Should().Contain("_quitMenuItem.Click += async (_, _) => await TryQuitApplicationAsync();");
        source.Should().Contain("var fileMenu = CreateNativeFileMenu();");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.NewWorkbook)");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.OpenRecent)");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.CloseWorkbook)");
        source.Should().Contain("var homeMenu = CreateNativeMenu(NativeMenuTopLevelId.Home);");
        source.Should().Contain("var helpMenu = CreateNativeMenu(NativeMenuTopLevelId.Help);");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> HomeMenuEntries");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> HelpMenuEntries");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Undo)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.HelpOnline)");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.Home, \"Home\")");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.PageLayout, \"Page Layout\")");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.Help, \"Help\")");
        source.Should().Contain("InstallNativeMenu(_nativeMenu);");
        source.Should().Contain("NativeDock.SetMenu(app, menu);");
        source.Should().Contain("NativeMenu.SetMenu(this, menu);");
        source.Should().Contain("_nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();");
        source.Should().Contain("ApplyNativeFileMenuAvailability(isIdle);");
        source.Should().Contain("new NativeFileMenuAvailabilityContext(");
        source.Should().Contain("CanOpen: _openButton.IsEnabled");
        source.Should().Contain("RefreshNativeOpenRecentMenu(isIdle);");
        source.Should().Contain("CanSave: _saveButton.IsEnabled");
        source.Should().Contain("CanSaveAs: _saveAsButton.IsEnabled");
        catalogSource.Should().Contain("new(NativeFileMenuItemId.CloseWorkbook, context.IsIdle)");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Undo, context.CanUndo)");
        catalogSource.Should().Contain("new(NativeMenuItemId.PasteSpecial, context.CanPasteSpecial)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearAll, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.Bold, context.CanBold)");
        catalogSource.Should().Contain("new(NativeMenuItemId.HorizontalText, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.CurrencyFormat, context.CanCurrencyFormat)");
        catalogSource.Should().Contain("new(NativeMenuItemId.AlignRight, context.CanAlignRight)");
        source.Should().Contain("private async Task CreateNewWorkbookAsync()");
        source.Should().Contain("ConfirmBeforeDestructiveWorkbookActionAsync(\"New Workbook\", \"Discard and Create\")");
        source.Should().Contain("_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true)");
        normalizedSource.Should().Contain("ReplaceSession(_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true));\n        RefreshViewportSizeForZoom();\n        ClearSelectedDrawingObject();\n        RefreshShell(_session.StartupStatus);");
        normalizedSource.Should().Contain("ReplaceSession(_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true));\n        RefreshViewportSizeForZoom();\n        ClearSelectedDrawingObject();\n        RefreshShell(status);");
        source.Should().Contain("RefreshShell(_session.StartupStatus);");
        source.Should().Contain("RecordStartupRecentWorkbook(source);");
        source.Should().Contain("private NativeMenu CreateNativeOpenRecentMenu(bool isIdle)");
        source.Should().Contain("Header = \"(No Recent Workbooks)\"");
        source.Should().Contain("OpenRecentWorkbookMenuPlanner.Create(");
        // Snapshot() (a copy taken under the store lock) rather than enumerating the live Entries.
        source.Should().Contain("_recentFiles.Snapshot()");
        source.Should().Contain("File.Exists");
        source.Should().Contain("path => _fileWorkflow.TryResolveOpenTarget(path, out var target, out _) ? target!.Path : null");
        source.Should().Contain("plan.ItemCount == 0");
        source.Should().Contain("foreach (var entry in plan.Items)");
        source.Should().Contain("var fileAccessIdentity = entry.FileAccessIdentity;");
        source.Should().Contain("Header = entry.Header");
        source.Should().Contain("private async Task OpenRecentWorkbookAsync(");
        source.Should().Contain("WorkbookFileAccessIdentity? fileAccessIdentity = null");
        source.Should().Contain("if (!_fileWorkflow.TryResolveOpenTarget(path, fileAccessIdentity, out var target, out _)");
        source.Should().Contain("_recentFiles.Remove(path);");
        source.Should().Contain("await OpenWorkbookPathAsync(target.Path, target.FileAccessIdentity);");
        source.Should().Contain("private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source)");
        source.Should().Contain("private void RecordRecentWorkbook(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)");
        source.Should().Contain("_fileWorkflow.RegisterRecentFile(");
        source.Should().Contain("new RecentFileRegistrationRequest(");
        source.Should().Contain("FileAccessIdentity: fileAccessIdentity ?? target.FileAccessIdentity");
        source.Should().Contain("_fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(");
        source.Should().Contain("completionPlan: context.CompletionPlan");
        source.Should().Contain("Closing += MainWindow_Closing;");
        source.Should().Contain("private async Task CloseWorkbookAsync()");
        source.Should().Contain("ConfirmBeforeDestructiveWorkbookActionAsync(\"Close Workbook\", \"Discard and Close\")");
        source.Should().Contain("ResetToNewWorkbook(\"Closed workbook.\");");
        source.Should().Contain("private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)");
        source.Should().Contain("e.Cancel = true;");
        source.Should().Contain("ConfirmBeforeDestructiveWorkbookActionAsync(\"Close FreeX\", \"Discard and Close\")");
        source.Should().Contain("private async Task TryQuitApplicationAsync()");
        source.Should().Contain("ConfirmBeforeDestructiveWorkbookActionAsync(\"Quit FreeX\", \"Discard and Quit\")");
        source.Should().Contain("_allowCloseWithoutDirtyPrompt = true;");
        source.Should().Contain("private async Task<bool> ConfirmBeforeDestructiveWorkbookActionAsync(string title, string discardButtonText)");
        source.Should().Contain("_fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        source.Should().Contain("ToSaveChangesPrompt(await ShowDirtyWorkbookCloseDialogAsync(title, discardButtonText))");
        source.Should().Contain("SaveCurrentWorkbookAsync");
        source.Should().Contain("() => _session.IsDirty");
        source.Should().NotContain("SaveCurrentWorkbookThenConfirmCleanAsync");
        source.Should().Contain("private static SaveChangesPrompt ToSaveChangesPrompt(DirtyWorkbookCloseChoice choice)");
        source.Should().Contain("_fileWorkflow.SaveResolvedAsync(");
        source.Should().Contain("private FileSaveTarget? ResolveExistingSaveTarget()");
        source.Should().Contain("_fileWorkflow.ResolveExistingSaveTarget(_session.CurrentFilePath)");
        // R68-async-ordering-race-sweep-3: OpenWorkbookAsync now claims _isOpening synchronously
        // before its own confirm-dialog/file-picker awaits, so its post-picker continuation must
        // call the guard-free OpenWorkbookPathCoreAsync directly -- routing back through the
        // guarded OpenWorkbookPathAsync wrapper here would see _isOpening already true and bail.
        source.Should().Contain("OpenWorkbookPathCoreAsync(path, fileAccessIdentity, confirmDirtyWorkbook: false)");
        source.Should().Contain("ConfirmBeforeDestructiveWorkbookActionAsync(\"Open Workbook\", \"Discard and Open\")");
        source.Should().Contain("private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync(");
        source.Should().Contain("AutomationProperties.SetAutomationId(saveButton, \"DirtyWorkbookSaveButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(discardButton, \"DirtyWorkbookDiscardButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"DirtyWorkbookCancelButton\");");
        source.Should().Contain("e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("await SaveWorkbookAsAsync();");
        AssertWorkbookShortcutRouteHandled(source, "NewWorkbook", "await CreateNewWorkbookAsync();");
        source.Should().Contain("e.Key == Key.W && HasOnlyCommandModifier(e.KeyModifiers)");
        source.Should().Contain("await CloseWorkbookAsync();");
        source.Should().Contain("TryQuitApplicationAsync()");
        source.Should().Contain("Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop");
        source.Should().Contain("desktop.TryShutdown(0);");
        source.Should().Contain("private async Task OpenExternalHelpLinkAsync(string url, string title)");
        source.Should().Contain("TopLevel.GetTopLevel(this)?.Launcher");
        source.Should().Contain("await launcher.LaunchUriAsync(uri)");
        source.Should().Contain("private async Task ShowAboutDialogAsync()");
        source.Should().Contain("var dialog = new AboutDialog();");
        source.Should().Contain("await dialog.ShowDialog(this);");
        var aboutSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "AboutDialog.cs"));
        aboutSource.Should().Contain("AvaloniaAboutDialog");
        aboutSource.Should().Contain("FreeXAboutDialogPresentation.Create");
        aboutSource.Should().Contain("typeof(AboutDialog).Assembly");
        aboutSource.Should().Contain("\"Avalonia\"");
        source.Should().Contain("private async Task ShowLegalNoticesDialogAsync()");
        source.Should().Contain("var dialog = new LegalNoticesDialog();");
        legalNoticesAdapterSource.Should().Contain("internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog");
        legalNoticesAdapterSource.Should().Contain("LegalNoticeProvider.GetDocuments()");
        sharedLegalNoticesSource.Should().Contain("AutomationProperties.SetAutomationId(_tabControl, \"LegalNoticesSectionTabs\");");
        sharedLegalNoticesSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyClassicTabChrome(");
        source.Should().Contain("await dialog.ShowDialog(this);");
    }

    [Fact]
    public void MainWindow_KeepsHelpLinksHttpOnlyBeforeSharedExternalLauncher()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var helpLinkSource = ExtractSourceBlock(
            source,
            "private async Task OpenExternalHelpLinkAsync(string url, string title)",
            "private async Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target)");
        var workbookHyperlinkSource = ExtractSourceBlock(
            source,
            "private async Task OpenExternalHyperlinkAsync(string target)",
            "private async Task ShowGoToSpecialDialogAsync()");

        helpLinkSource.Should().Contain("if (!IsHttpOrHttpsHelpUrl(url))");
        helpLinkSource.Should().Contain("ShowHelpIssue($\"{title} link is blocked.\");");
        helpLinkSource.Should().Contain("private static bool IsHttpOrHttpsHelpUrl(string url)");
        helpLinkSource.Should().Contain("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)");
        helpLinkSource.Should().Contain("string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)");
        helpLinkSource.Should().Contain("string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)");
        helpLinkSource.IndexOf("if (!IsHttpOrHttpsHelpUrl(url))", StringComparison.Ordinal)
            .Should().BeLessThan(helpLinkSource.IndexOf("var result = await OpenExternalUriAsync(url);", StringComparison.Ordinal));

        workbookHyperlinkSource.Should().Contain("var result = await OpenExternalUriAsync(target);");
        workbookHyperlinkSource.Should().NotContain("IsHttpOrHttpsHelpUrl");
    }

    [Fact]
    public void MainWindow_BuildsAvaloniaFilePickerTypesFromSharedIoDescriptors()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var commandPlanner = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookFileCommandPlanner.cs"));
        var pickerPlanner = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookFilePickerPlanner.cs"));
        var adapter = File.ReadAllText(RepositoryFileLocator.Find("shared", "Free.Shared.Shell.Avalonia", "AvaloniaFilePickerTypeAdapter.cs"));
        var pickerService = File.ReadAllText(RepositoryFileLocator.Find("shared", "Free.Shared.Shell.Avalonia", "AvaloniaFilePickerService.cs"));

        source.Should().Contain("WorkbookFileCommandPlanner.PlanOpenPicker(StorageProvider.CanOpen, _session.OpenFormats)");
        source.Should().Contain("WorkbookFileCommandPlanner.PlanSaveAsPicker(");
        source.Should().NotContain("WorkbookFilePickerPlanner.BuildOpenPickerPlan(_session.OpenFormats)");
        source.Should().NotContain("WorkbookFilePickerPlanner.BuildSavePickerPlan(");
        commandPlanner.Should().Contain("WorkbookFilePickerPlanner.BuildOpenPickerPlan(openFormats)");
        commandPlanner.Should().Contain("WorkbookFilePickerPlanner.BuildSavePickerPlan(");
        pickerPlanner.Should().Contain("FileDialogRequestPlanner.BuildOpenPickerPlan(");
        pickerPlanner.Should().Contain("FileDialogRequestPlanner.BuildSavePickerPlan(");
        pickerPlanner.Should().Contain("FileOpenPickerPlan BuildOpenPickerPlan");
        pickerPlanner.Should().Contain("FileSavePickerPlan BuildSavePickerPlan");
        source.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(\"Open Workbook\", openPlan.FileTypes)");
        source.Should().Contain("AvaloniaFilePickerSaveRequest.FromSavePlan(");
        pickerService.Should().Contain("AvaloniaFilePickerTypeAdapter.ToFileTypes(fileTypes)");
        pickerService.Should().Contain("AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes)");
        source.Should().NotContain("private static FilePickerFileType CreateFilePickerFileType(FilePickerTypeDescriptor descriptor)");
        adapter.Should().Contain("CreateFileType(descriptor.DisplayName, descriptor.Patterns, descriptor.MimeTypes)");
    }

    [Fact]
    public void App_WiresMacOsLaunchSmokeToRuntimeSnapshot()
    {
        var appSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));
        var programSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        programSource.Should().Contain("MacOsLaunchSmokeOptions.TryParse(");
        programSource.Should().Contain("out var launchSmokeOptions");
        programSource.Should().Contain("out var startupArguments");
        programSource.Should().Contain("AvaloniaAppDiagnostics.Create(launchSmokeOptions?.DiagnosticsDirectory)");
        programSource.Should().Contain("diagnostics.RecordEvent(\"app_start\"");
        programSource.Should().Contain("diagnostics.RecordEvent(\"app_exit\"");
        programSource.Should().Contain("diagnostics.RecordCrash(ex, \"avalonia_startup\")");
        programSource.Should().Contain("App.LaunchSmokeOptions = launchSmokeOptions;");
        programSource.Should().Contain("App.Diagnostics = diagnostics;");
        programSource.Should().Contain("StartWithClassicDesktopLifetime(startupArguments)");
        appSource.Should().Contain("private const string ApplicationTitle = \"FreeX\";");
        appSource.Should().Contain("Name = ApplicationTitle;");
        appSource.Should().Contain("internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }");
        appSource.Should().Contain("internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }");
        appSource.Should().Contain("Diagnostics?.RecordEvent(\"app_ready\"");
        appSource.Should().Contain("if (LaunchSmokeOptions is { } launchSmokeOptions)");
        appSource.Should().Contain("MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);");
        smokeSource.Should().Contain("public const string Argument = \"--macos-launch-smoke\";");
        smokeSource.Should().Contain("public const string DiagnosticsDirectoryArgument = \"--macos-launch-smoke-diagnostics-dir\";");
        smokeSource.Should().Contain("public const string VerifyImageClipboardPasteArgument = \"--macos-launch-smoke-verify-image-clipboard\";");
        smokeSource.Should().Contain("public const string VerifyLiveCommandKeysArgument = \"--macos-launch-smoke-verify-live-command-keys\";");
        smokeSource.Should().Contain("startupArguments = filteredArguments.ToArray();");
        smokeSource.Should().Contain("verifyImageClipboardPaste = true;");
        smokeSource.Should().Contain("verifyLiveCommandKeys = true;");
        smokeSource.Should().Contain("diagnosticsDirectory = args[++index];");
        smokeSource.Should().Contain("diagnosticsDirectory);");
        smokeSource.Should().Contain("mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);");
        smokeSource.Should().Contain("diagnostics?.RecordEvent(\"macos_launch_smoke\"");
        smokeSource.Should().Contain("diagnostics?.RecordCrash(ex, \"macos_launch_smoke\")");
        smokeSource.Should().Contain("mainWindow.CreateLaunchSmokeSnapshot()");
        smokeSource.Should().Contain("commandKeyEvidence = CaptureCommandKeyEvidence(mainWindow);");
        smokeSource.Should().Contain("liveCommandKeyEvidence = mainWindow.BeginLaunchSmokeLiveCommandKeyProbe();");
        smokeSource.Should().Contain("mainWindow.CreateLaunchSmokeLiveCommandKeySnapshot()");
        smokeSource.Should().Contain("await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();");
        smokeSource.Should().Contain("IsPassed(snapshot, options, initialExternalImageClipboardPictureCount)");
        smokeSource.Should().Contain("IsPassedWithCommandKeyEvidence(");
        smokeSource.Should().Contain("HasExternalImageClipboardPasteEvidence(");
        smokeSource.Should().Contain("app_diagnostics_directory_configured={FormatBool(appDiagnosticsConfigured)}");
        smokeSource.Should().Contain("liveCommandKeyEvidence.IsPassed");
        smokeSource.Should().Contain("macos_launch_smoke={(IsPassedWithCommandKeyEvidence(snapshot, options, initialExternalImageClipboardPictureCount, commandKeyEvidence, liveCommandKeyEvidence) ? \"passed\" : \"failed\")}");
        smokeSource.Should().Contain("command_key_smoke={(commandKeyEvidence.IsPassed ? \"passed\" : \"failed\")}");
        smokeSource.Should().Contain("command_key_smoke_attempted={FormatBool(attemptedCommandKeyEvidence)}");
        smokeSource.Should().Contain("HasFindDirectRouteSourceGuard: HasMainWindowDirectCommandRouteSourceSupport(");
        smokeSource.Should().Contain("HasPageUpDirectRouteSourceGuard: HasMainWindowDirectCommandRouteSourceSupport(");
        smokeSource.Should().Contain("HasPageDownDirectRouteSourceGuard: HasMainWindowDirectCommandRouteSourceSupport(");
        smokeSource.Should().Contain("cmd_find_direct_route_source_guard={FormatBool(commandKeyEvidence.HasFindDirectRouteSourceGuard)}");
        smokeSource.Should().Contain("cmd_page_up_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageUpDirectRouteSourceGuard)}");
        smokeSource.Should().Contain("cmd_page_down_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageDownDirectRouteSourceGuard)}");
        smokeSource.Should().Contain("live_command_key_smoke_required={FormatBool(options.VerifyLiveCommandKeys)}");
        smokeSource.Should().Contain("live_command_key_smoke={liveCommandKeySmokeStatus}");
        smokeSource.Should().Contain("live_command_key_smoke_ready={FormatBool(liveCommandKeyEvidence.IsReady)}");
        smokeSource.Should().Contain("live_cmd_select_all_state_changed={FormatBool(liveCommandKeyEvidence.HasSelectAllStateChange)}");
        smokeSource.Should().Contain("live_cmd_bold_state_changed={FormatBool(liveCommandKeyEvidence.HasBoldStateChange)}");
        smokeSource.Should().Contain("live_cmd_italic_state_changed={FormatBool(liveCommandKeyEvidence.HasItalicStateChange)}");
        smokeSource.Should().Contain("live_cmd_underline_state_changed={FormatBool(liveCommandKeyEvidence.HasUnderlineStateChange)}");
        smokeSource.Should().Contain("external_image_clipboard_paste_required={FormatBool(options.VerifyImageClipboardPaste)}");
        smokeSource.Should().Contain("external_image_clipboard_paste={FormatBool(imageClipboardPasteVerified)}");
        smokeSource.Should().Contain("external_image_clipboard_picture_count={snapshot.ExternalImageClipboardPictureCount}");
        smokeSource.Should().Contain("external_image_clipboard_picture_png_bytes={snapshot.ExternalImageClipboardPicturePngByteCount}");
        smokeSource.Should().Contain("bool HasNativeShareWorkbookMenuItem,");
        smokeSource.Should().Contain("HasNativeShareWorkbookMenuItem &&");
        smokeSource.Should().Contain("bool HasNativeWorkbookStatisticsMenuItem,");
        smokeSource.Should().Contain("HasNativeWorkbookStatisticsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialCommentsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialValidationMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialAllExceptBordersMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialAllMergingConditionalFormatsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialColumnWidthsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialFormulasAndNumberFormatsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialValuesAndNumberFormatsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialValuesAndSourceFormattingMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialKeepSourceColumnWidthsMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialPasteLinkMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialTextMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialUnicodeTextMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialPictureMenuItem &&");
        smokeSource.Should().Contain("HasNativePasteSpecialLinkedPictureMenuItem &&");
        smokeSource.Should().Contain("HasNativeFindMenuItem &&");
        smokeSource.Should().Contain("HasNativeFindNextMenuItem &&");
        smokeSource.Should().Contain("HasNativeReplaceMenuItem &&");
        smokeSource.Should().Contain("HasNativeGoToMenuItem &&");
        smokeSource.Should().Contain("HasNativeGoToSpecialMenuItem &&");
        smokeSource.Should().Contain("HasNativeBordersMenuItem &&");
        smokeSource.Should().Contain("NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length");
        smokeSource.Should().Contain("HasNativeTabColorMenuItem &&");
        smokeSource.Should().Contain("HasNativeClearTabColorMenuItem &&");
        smokeSource.Should().Contain("NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count");
        smokeSource.Should().Contain("HasBordersButton &&");
        smokeSource.Should().Contain("HasMergeAndCenterButton &&");
        smokeSource.Should().Contain("HasFocusableSheetTab &&");
        smokeSource.Should().Contain("HasFocusableActiveSheetTab &&");
        smokeSource.Should().Contain("HasShellFocusCycleTargets &&");
        smokeSource.Should().Contain("HasSheetTabContextKeyboardHelp &&");
        smokeSource.Should().Contain("HasSheetTabContextRenameMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextTabColorMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextNoColorMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextSelectAllSheetsMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextUngroupSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeSelectAllSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeUngroupSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeShowGridlinesMenuItem &&");
        smokeSource.Should().Contain("HasNativeShowHeadingsMenuItem &&");
        smokeSource.Should().Contain("HasNativeZoomInMenuItem &&");
        smokeSource.Should().Contain("HasNativeZoomOutMenuItem &&");
        smokeSource.Should().Contain("HasNativeZoom100MenuItem &&");
        smokeSource.Should().Contain("HasNativeZoomToSelectionMenuItem &&");
        smokeSource.Should().Contain("HasNativeFreezePanesMenuItem &&");
        smokeSource.Should().Contain("HasNativeFreezeTopRowMenuItem &&");
        smokeSource.Should().Contain("HasNativeFreezeFirstColumnMenuItem &&");
        smokeSource.Should().Contain("HasNativeUnfreezePanesMenuItem &&");
        smokeSource.Should().Contain("HasNativeDockMenu &&");
        smokeSource.Should().Contain("HasNativeDockFileMenu &&");
        smokeSource.Should().Contain("NativeDockFileMenuItemCount > 0 &&");
        smokeSource.Should().Contain("opened_source_path={snapshot.OpenedSourcePath ?? \"\"}");
        smokeSource.Should().Contain("native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}");
        smokeSource.Should().Contain("native_dock_menu_installed={FormatBool(snapshot.HasNativeDockMenu)}");
        smokeSource.Should().Contain("native_dock_file_menu={FormatBool(snapshot.HasNativeDockFileMenu)}");
        smokeSource.Should().Contain("native_dock_file_menu_item_count={snapshot.NativeDockFileMenuItemCount}");
        smokeSource.Should().Contain("native_new_workbook_menu_item={FormatBool(snapshot.HasNativeNewWorkbookMenuItem)}");
        smokeSource.Should().Contain("native_open_recent_menu_item={FormatBool(snapshot.HasNativeOpenRecentMenuItem)}");
        smokeSource.Should().Contain("native_open_recent_item_count={snapshot.NativeOpenRecentItemCount}");
        smokeSource.Should().Contain("native_share_workbook_menu_item={FormatBool(snapshot.HasNativeShareWorkbookMenuItem)}");
        smokeSource.Should().Contain("native_workbook_statistics_menu_item={FormatBool(snapshot.HasNativeWorkbookStatisticsMenuItem)}");
        smokeSource.Should().Contain("native_close_workbook_menu_item={FormatBool(snapshot.HasNativeCloseWorkbookMenuItem)}");
        smokeSource.Should().Contain("native_top_level_menu_order={snapshot.NativeTopLevelMenuOrder}");
        smokeSource.Should().Contain("native_home_menu={FormatBool(snapshot.HasNativeHomeMenu)}");
        smokeSource.Should().Contain("native_insert_menu={FormatBool(snapshot.HasNativeInsertMenu)}");
        smokeSource.Should().Contain("native_page_layout_menu={FormatBool(snapshot.HasNativePageLayoutMenu)}");
        smokeSource.Should().Contain("native_formulas_menu={FormatBool(snapshot.HasNativeFormulasMenu)}");
        smokeSource.Should().Contain("native_data_menu={FormatBool(snapshot.HasNativeDataMenu)}");
        smokeSource.Should().Contain("native_review_menu={FormatBool(snapshot.HasNativeReviewMenu)}");
        smokeSource.Should().Contain("native_view_menu={FormatBool(snapshot.HasNativeViewMenu)}");
        smokeSource.Should().Contain("native_help_menu={FormatBool(snapshot.HasNativeHelpMenu)}");
        smokeSource.Should().Contain("native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}");
        smokeSource.Should().Contain("native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}");
        smokeSource.Should().Contain("native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}");
        smokeSource.Should().Contain("native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}");
        smokeSource.Should().Contain("native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_menu_item={FormatBool(snapshot.HasNativePasteSpecialMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_comments_menu_item={FormatBool(snapshot.HasNativePasteSpecialCommentsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_validation_menu_item={FormatBool(snapshot.HasNativePasteSpecialValidationMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_all_except_borders_menu_item={FormatBool(snapshot.HasNativePasteSpecialAllExceptBordersMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_all_merging_conditional_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialAllMergingConditionalFormatsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_column_widths_menu_item={FormatBool(snapshot.HasNativePasteSpecialColumnWidthsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_formulas_and_number_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialFormulasAndNumberFormatsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_values_and_number_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialValuesAndNumberFormatsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_values_and_source_formatting_menu_item={FormatBool(snapshot.HasNativePasteSpecialValuesAndSourceFormattingMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_keep_source_column_widths_menu_item={FormatBool(snapshot.HasNativePasteSpecialKeepSourceColumnWidthsMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_paste_link_menu_item={FormatBool(snapshot.HasNativePasteSpecialPasteLinkMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_text_menu_item={FormatBool(snapshot.HasNativePasteSpecialTextMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_unicode_text_menu_item={FormatBool(snapshot.HasNativePasteSpecialUnicodeTextMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_picture_menu_item={FormatBool(snapshot.HasNativePasteSpecialPictureMenuItem)}");
        smokeSource.Should().Contain("native_paste_special_linked_picture_menu_item={FormatBool(snapshot.HasNativePasteSpecialLinkedPictureMenuItem)}");
        smokeSource.Should().Contain("native_find_menu_item={FormatBool(snapshot.HasNativeFindMenuItem)}");
        smokeSource.Should().Contain("native_find_next_menu_item={FormatBool(snapshot.HasNativeFindNextMenuItem)}");
        smokeSource.Should().Contain("native_replace_menu_item={FormatBool(snapshot.HasNativeReplaceMenuItem)}");
        smokeSource.Should().Contain("native_go_to_menu_item={FormatBool(snapshot.HasNativeGoToMenuItem)}");
        smokeSource.Should().Contain("native_go_to_special_menu_item={FormatBool(snapshot.HasNativeGoToSpecialMenuItem)}");
        smokeSource.Should().Contain("native_advanced_filter_menu_item={FormatBool(snapshot.HasNativeAdvancedFilterMenuItem)}");
        smokeSource.Should().Contain("native_remove_duplicates_menu_item={FormatBool(snapshot.HasNativeRemoveDuplicatesMenuItem)}");
        smokeSource.Should().Contain("native_data_validation_preview_menu_item={FormatBool(snapshot.HasNativeDataValidationPreviewMenuItem)}");
        smokeSource.Should().Contain("native_data_validation_menu_item={FormatBool(snapshot.HasNativeDataValidationMenuItem)}");
        smokeSource.Should().Contain("native_what_if_analysis_menu_item={FormatBool(snapshot.HasNativeWhatIfAnalysisMenuItem)}");
        smokeSource.Should().Contain("native_goal_seek_menu_item={FormatBool(snapshot.HasNativeGoalSeekMenuItem)}");
        smokeSource.Should().Contain("native_data_table_menu_item={FormatBool(snapshot.HasNativeDataTableMenuItem)}");
        smokeSource.Should().Contain("native_scenario_manager_menu_item={FormatBool(snapshot.HasNativeScenarioManagerMenuItem)}");
        smokeSource.Should().Contain("native_forecast_sheet_menu_item={FormatBool(snapshot.HasNativeForecastSheetMenuItem)}");
        smokeSource.Should().Contain("native_review_summary_menu_item={FormatBool(snapshot.HasNativeReviewSummaryMenuItem)}");
        smokeSource.Should().Contain("native_check_accessibility_menu_item={FormatBool(snapshot.HasNativeCheckAccessibilityMenuItem)}");
        smokeSource.Should().Contain("native_next_note_menu_item={FormatBool(snapshot.HasNativeNextNoteMenuItem)}");
        smokeSource.Should().Contain("native_previous_note_menu_item={FormatBool(snapshot.HasNativePreviousNoteMenuItem)}");
        smokeSource.Should().Contain("native_next_comment_menu_item={FormatBool(snapshot.HasNativeNextCommentMenuItem)}");
        smokeSource.Should().Contain("native_previous_comment_menu_item={FormatBool(snapshot.HasNativePreviousCommentMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_menu_item={FormatBool(snapshot.HasNativeTabColorMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_clear_item={FormatBool(snapshot.HasNativeClearTabColorMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_swatch_count={snapshot.NativeTabColorSwatchCount}");
        smokeSource.Should().Contain("native_select_all_sheets_menu_item={FormatBool(snapshot.HasNativeSelectAllSheetsMenuItem)}");
        smokeSource.Should().Contain("native_ungroup_sheets_menu_item={FormatBool(snapshot.HasNativeUngroupSheetsMenuItem)}");
        smokeSource.Should().Contain("native_select_all_menu_item={FormatBool(snapshot.HasNativeSelectAllMenuItem)}");
        smokeSource.Should().Contain("native_clear_menu_item={FormatBool(snapshot.HasNativeClearMenuItem)}");
        smokeSource.Should().Contain("native_clear_all_menu_item={FormatBool(snapshot.HasNativeClearAllMenuItem)}");
        smokeSource.Should().Contain("native_clear_formats_menu_item={FormatBool(snapshot.HasNativeClearFormatsMenuItem)}");
        smokeSource.Should().Contain("native_clear_contents_menu_item={FormatBool(snapshot.HasNativeClearContentsMenuItem)}");
        smokeSource.Should().Contain("native_clear_comments_menu_item={FormatBool(snapshot.HasNativeClearCommentsMenuItem)}");
        smokeSource.Should().Contain("native_clear_hyperlinks_menu_item={FormatBool(snapshot.HasNativeClearHyperlinksMenuItem)}");
        smokeSource.Should().Contain("native_bold_menu_item={FormatBool(snapshot.HasNativeBoldMenuItem)}");
        smokeSource.Should().Contain("native_italic_menu_item={FormatBool(snapshot.HasNativeItalicMenuItem)}");
        smokeSource.Should().Contain("native_underline_menu_item={FormatBool(snapshot.HasNativeUnderlineMenuItem)}");
        smokeSource.Should().Contain("native_double_underline_menu_item={FormatBool(snapshot.HasNativeDoubleUnderlineMenuItem)}");
        smokeSource.Should().Contain("native_strikethrough_menu_item={FormatBool(snapshot.HasNativeStrikethroughMenuItem)}");
        smokeSource.Should().Contain("native_increase_font_size_menu_item={FormatBool(snapshot.HasNativeIncreaseFontSizeMenuItem)}");
        smokeSource.Should().Contain("native_decrease_font_size_menu_item={FormatBool(snapshot.HasNativeDecreaseFontSizeMenuItem)}");
        smokeSource.Should().Contain("native_fill_color_menu_item={FormatBool(snapshot.HasNativeFillColorMenuItem)}");
        smokeSource.Should().Contain("native_clear_fill_menu_item={FormatBool(snapshot.HasNativeClearFillMenuItem)}");
        smokeSource.Should().Contain("native_font_color_menu_item={FormatBool(snapshot.HasNativeFontColorMenuItem)}");
        smokeSource.Should().Contain("native_fill_color_swatch_count={snapshot.NativeFillColorSwatchCount}");
        smokeSource.Should().Contain("native_font_color_swatch_count={snapshot.NativeFontColorSwatchCount}");
        smokeSource.Should().Contain("toolbar_clear_button={FormatBool(snapshot.HasClearButton)}");
        smokeSource.Should().Contain("toolbar_clear_all_menu_item={FormatBool(snapshot.HasClearAllMenuItem)}");
        smokeSource.Should().Contain("toolbar_clear_formats_menu_item={FormatBool(snapshot.HasClearFormatsMenuItem)}");
        smokeSource.Should().Contain("toolbar_clear_contents_menu_item={FormatBool(snapshot.HasClearContentsMenuItem)}");
        smokeSource.Should().Contain("toolbar_clear_comments_menu_item={FormatBool(snapshot.HasClearCommentsMenuItem)}");
        smokeSource.Should().Contain("toolbar_clear_hyperlinks_menu_item={FormatBool(snapshot.HasClearHyperlinksMenuItem)}");
        smokeSource.Should().Contain("toolbar_borders_button={FormatBool(snapshot.HasBordersButton)}");
        smokeSource.Should().Contain("toolbar_wrap_text_button={FormatBool(snapshot.HasWrapTextButton)}");
        smokeSource.Should().Contain("toolbar_merge_and_center_button={FormatBool(snapshot.HasMergeAndCenterButton)}");
        smokeSource.Should().Contain("native_borders_menu_item={FormatBool(snapshot.HasNativeBordersMenuItem)}");
        smokeSource.Should().Contain("native_borders_preset_count={snapshot.NativeBordersPresetCount}");
        smokeSource.Should().Contain("native_cell_styles_menu_item={FormatBool(snapshot.HasNativeCellStylesMenuItem)}");
        smokeSource.Should().Contain("native_cell_styles_preset_count={snapshot.NativeCellStylesPresetCount}");
        smokeSource.Should().Contain("native_horizontal_text_menu_item={FormatBool(snapshot.HasNativeHorizontalTextMenuItem)}");
        smokeSource.Should().Contain("native_angle_counterclockwise_menu_item={FormatBool(snapshot.HasNativeAngleCounterclockwiseMenuItem)}");
        smokeSource.Should().Contain("native_angle_clockwise_menu_item={FormatBool(snapshot.HasNativeAngleClockwiseMenuItem)}");
        smokeSource.Should().Contain("native_vertical_text_menu_item={FormatBool(snapshot.HasNativeVerticalTextMenuItem)}");
        smokeSource.Should().Contain("native_rotate_text_up_menu_item={FormatBool(snapshot.HasNativeRotateTextUpMenuItem)}");
        smokeSource.Should().Contain("native_rotate_text_down_menu_item={FormatBool(snapshot.HasNativeRotateTextDownMenuItem)}");
        smokeSource.Should().Contain("native_currency_format_menu_item={FormatBool(snapshot.HasNativeCurrencyFormatMenuItem)}");
        smokeSource.Should().Contain("native_percent_format_menu_item={FormatBool(snapshot.HasNativePercentFormatMenuItem)}");
        smokeSource.Should().Contain("native_comma_style_menu_item={FormatBool(snapshot.HasNativeCommaStyleMenuItem)}");
        smokeSource.Should().Contain("native_increase_decimal_menu_item={FormatBool(snapshot.HasNativeIncreaseDecimalMenuItem)}");
        smokeSource.Should().Contain("native_decrease_decimal_menu_item={FormatBool(snapshot.HasNativeDecreaseDecimalMenuItem)}");
        smokeSource.Should().Contain("native_align_top_menu_item={FormatBool(snapshot.HasNativeAlignTopMenuItem)}");
        smokeSource.Should().Contain("native_align_middle_menu_item={FormatBool(snapshot.HasNativeAlignMiddleMenuItem)}");
        smokeSource.Should().Contain("native_align_bottom_menu_item={FormatBool(snapshot.HasNativeAlignBottomMenuItem)}");
        smokeSource.Should().Contain("native_wrap_text_menu_item={FormatBool(snapshot.HasNativeWrapTextMenuItem)}");
        smokeSource.Should().Contain("native_merge_and_center_menu_item={FormatBool(snapshot.HasNativeMergeAndCenterMenuItem)}");
        smokeSource.Should().Contain("native_unmerge_cells_menu_item={FormatBool(snapshot.HasNativeUnmergeCellsMenuItem)}");
        smokeSource.Should().Contain("native_show_gridlines_menu_item={FormatBool(snapshot.HasNativeShowGridlinesMenuItem)}");
        smokeSource.Should().Contain("native_show_headings_menu_item={FormatBool(snapshot.HasNativeShowHeadingsMenuItem)}");
        smokeSource.Should().Contain("native_zoom_in_menu_item={FormatBool(snapshot.HasNativeZoomInMenuItem)}");
        smokeSource.Should().Contain("native_zoom_out_menu_item={FormatBool(snapshot.HasNativeZoomOutMenuItem)}");
        smokeSource.Should().Contain("native_zoom_100_menu_item={FormatBool(snapshot.HasNativeZoom100MenuItem)}");
        smokeSource.Should().Contain("native_zoom_to_selection_menu_item={FormatBool(snapshot.HasNativeZoomToSelectionMenuItem)}");
        smokeSource.Should().Contain("native_freeze_panes_menu_item={FormatBool(snapshot.HasNativeFreezePanesMenuItem)}");
        smokeSource.Should().Contain("native_freeze_top_row_menu_item={FormatBool(snapshot.HasNativeFreezeTopRowMenuItem)}");
        smokeSource.Should().Contain("native_freeze_first_column_menu_item={FormatBool(snapshot.HasNativeFreezeFirstColumnMenuItem)}");
        smokeSource.Should().Contain("native_unfreeze_panes_menu_item={FormatBool(snapshot.HasNativeUnfreezePanesMenuItem)}");
        smokeSource.Should().Contain("native_decrease_indent_menu_item={FormatBool(snapshot.HasNativeDecreaseIndentMenuItem)}");
        smokeSource.Should().Contain("native_increase_indent_menu_item={FormatBool(snapshot.HasNativeIncreaseIndentMenuItem)}");
        smokeSource.Should().Contain("native_align_left_menu_item={FormatBool(snapshot.HasNativeAlignLeftMenuItem)}");
        smokeSource.Should().Contain("native_align_center_menu_item={FormatBool(snapshot.HasNativeAlignCenterMenuItem)}");
        smokeSource.Should().Contain("native_align_right_menu_item={FormatBool(snapshot.HasNativeAlignRightMenuItem)}");
        smokeSource.Should().Contain("native_show_formulas_menu_item={FormatBool(snapshot.HasNativeShowFormulasMenuItem)}");
        smokeSource.Should().Contain("native_help_online_menu_item={FormatBool(snapshot.HasNativeHelpOnlineMenuItem)}");
        smokeSource.Should().Contain("native_send_feedback_menu_item={FormatBool(snapshot.HasNativeSendFeedbackMenuItem)}");
        smokeSource.Should().Contain("native_check_for_updates_menu_item={FormatBool(snapshot.HasNativeCheckForUpdatesMenuItem)}");
        smokeSource.Should().Contain("native_about_menu_item={FormatBool(snapshot.HasNativeAboutMenuItem)}");
        smokeSource.Should().Contain("native_legal_notices_menu_item={FormatBool(snapshot.HasNativeLegalNoticesMenuItem)}");
        smokeSource.Should().Contain("focusable_sheet_tab={FormatBool(snapshot.HasFocusableSheetTab)}");
        smokeSource.Should().Contain("focusable_active_sheet_tab={FormatBool(snapshot.HasFocusableActiveSheetTab)}");
        smokeSource.Should().Contain("shell_focus_cycle_targets={FormatBool(snapshot.HasShellFocusCycleTargets)}");
        smokeSource.Should().Contain("sheet_tab_context_keyboard_help={FormatBool(snapshot.HasSheetTabContextKeyboardHelp)}");
        smokeSource.Should().Contain("sheet_tab_context_rename_menu_item={FormatBool(snapshot.HasSheetTabContextRenameMenuItem)}");
        smokeSource.Should().Contain("sheet_tab_context_tab_color_menu_item={FormatBool(snapshot.HasSheetTabContextTabColorMenuItem)}");
        smokeSource.Should().Contain("sheet_tab_context_no_color_menu_item={FormatBool(snapshot.HasSheetTabContextNoColorMenuItem)}");
        smokeSource.Should().Contain("sheet_tab_context_select_all_sheets_menu_item={FormatBool(snapshot.HasSheetTabContextSelectAllSheetsMenuItem)}");
        smokeSource.Should().Contain("sheet_tab_context_ungroup_sheets_menu_item={FormatBool(snapshot.HasSheetTabContextUngroupSheetsMenuItem)}");
        smokeSource.Should().Contain("desktop.TryShutdown(exitCode);");
        windowSource.Should().Contain("private readonly NativeMenuItem _quitMenuItem = new();");
        windowSource.Should().Contain("private NativeMenu? _nativeMenu;");
        windowSource.Should().Contain("InstallNativeMenu(_nativeMenu);");
        windowSource.Should().Contain("NativeDock.SetMenu(app, menu);");
        windowSource.Should().Contain("NativeDock.GetMenu(app)");
        windowSource.Should().Contain("NativeMenu.SetMenu(this, menu);");
        windowSource.Should().Contain("internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()");
        windowSource.Should().Contain("internal MacOsLaunchSmokeLiveCommandKeySnapshot BeginLaunchSmokeLiveCommandKeyProbe()");
        windowSource.Should().Contain("FocusShellRegion(ShellFocusTarget.Worksheet);");
        windowSource.Should().Contain("private void RecordLaunchSmokeLiveCommandKey(Key key, bool before, bool after)");
        windowSource.Should().Contain("private void RecordLaunchSmokeLiveSelectAllCommandKey(GridRange before, GridRange after)");
        windowSource.Should().Contain("RecordLaunchSmokeLiveSelectAllCommandKey(before, _session.SelectedRange);");
        windowSource.Should().Contain("internal async Task<bool> TryPasteLaunchSmokeClipboardImageAsync()");
        // R68-async-ordering-race-sweep-1: the destination is now captured inside
        // TryPasteClipboardImageAsync itself (right before use, after the bitmap-read await)
        // rather than passed in by the caller, so the status message always names the live
        // active cell instead of one captured before the await could go stale.
        windowSource.Should().Contain("return await TryPasteClipboardImageAsync(clipboard);");
        windowSource.Should().Contain("var externalImageClipboardPictures = _session.ActiveSheet.Pictures");
        windowSource.Should().Contain("ExternalImageClipboardPictureCount: externalImageClipboardPictures.Length");
        windowSource.Should().Contain("ExternalImageClipboardPicturePngByteCount: externalImageClipboardPictures.Sum(static picture => picture.ImageBytes!.Length)");
        windowSource.Should().Contain("GetNativeTopLevelMenuOrder(_nativeMenu)");
        windowSource.Should().Contain("HasNativeTopLevelMenu(_nativeMenu, id);");
        windowSource.Should().Contain("HasNativeTopLevelMenu(menu, GetNativeTopLevelHeader(id))");
        windowSource.Should().Contain("FindNativeTopLevelSubmenu(menu, expectedHeader)");
        windowSource.Should().Contain("WindowShown: IsVisible");
        windowSource.Should().Contain("OpenedSourcePath: _session.CurrentFilePath");
        windowSource.Should().Contain("HasNativeNewWorkbookMenuItem: HasNativeFileMenuItem(_newWorkbookMenuItem, NativeFileMenuItemId.NewWorkbook)");
        windowSource.Should().Contain("HasNativeOpenRecentMenuItem: HasNativeFileMenuItem(_openRecentMenuItem, NativeFileMenuItemId.OpenRecent)");
        windowSource.Should().Contain("NativeOpenRecentItemCount: nativeOpenRecentItemCount");
        windowSource.Should().Contain("HasNativeWorkbookStatisticsMenuItem: HasNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics)");
        windowSource.Should().Contain("NativeTabColorSwatchCount: nativeTabColorSwatchCount");
        windowSource.Should().Contain("HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable)");
        windowSource.Should().Contain("HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true");
        windowSource.Should().Contain("HasShellFocusCycleTargets: _sheetGridHost.Focusable &&");
        windowSource.Should().Contain("GetToolbarFocusTargets().Any(control => control.Focusable) &&");
        windowSource.Should().Contain("_formulaBox.Focusable &&");
        windowSource.Should().Contain("_zoomText.Focusable");
        windowSource.Should().Contain("HasSheetTabContextKeyboardHelp: HasSheetTabButton(button =>");
        windowSource.Should().Contain("string.Equals(AutomationProperties.GetHelpText(button), SheetTabContextHelpText, StringComparison.Ordinal))");
        windowSource.Should().Contain("HasSheetTabContextRenameMenuItem: HasSheetTabContextMenuItem(\"Rename...\")");
        windowSource.Should().Contain("HasSheetTabContextTabColorMenuItem: HasSheetTabContextMenuItem(\"Tab Color\")");
        windowSource.Should().Contain("HasSheetTabContextNoColorMenuItem: HasSheetTabContextSubmenuItem(\"Tab Color\", \"No Color\")");
        windowSource.Should().Contain("HasSheetTabContextSelectAllSheetsMenuItem: HasSheetTabContextMenuItem(\"Select All Sheets\")");
        windowSource.Should().Contain("HasSheetTabContextUngroupSheetsMenuItem: HasSheetTabContextMenuItem(\"Ungroup Sheets\")");
        windowSource.Should().Contain("NativeDockTopLevelMenuOrder: nativeDockTopLevelMenuOrder");
        windowSource.Should().Contain("HasNativeDockMenu: hasNativeDockMenu");
        windowSource.Should().Contain("HasNativeDockFileMenu: hasNativeDockFileMenu");
        windowSource.Should().Contain("NativeDockFileMenuItemCount: nativeDockFileMenuItemCount");
        windowSource.Should().Contain("CountNativeTopLevelMenuItems(nativeDockMenu, NativeMenuTopLevelId.File)");
        windowSource.Should().Contain("HasNativeSelectAllSheetsMenuItem: HasNativeMenuItem(_selectAllSheetsMenuItem, NativeMenuItemId.SelectAllSheets)");
        windowSource.Should().Contain("HasNativeUngroupSheetsMenuItem: HasNativeMenuItem(_ungroupSheetsMenuItem, NativeMenuItemId.UngroupSheets)");
        windowSource.Should().Contain("HasNativeCloseWorkbookMenuItem: HasNativeFileMenuItem(_closeWorkbookMenuItem, NativeFileMenuItemId.CloseWorkbook)");
        windowSource.Should().Contain("NativeTopLevelMenuOrder: nativeTopLevelMenuOrder");
        windowSource.Should().Contain("HasNativeHomeMenu: hasNativeHomeMenu");
        windowSource.Should().Contain("HasNativeInsertMenu: hasNativeInsertMenu");
        windowSource.Should().Contain("HasNativePageLayoutMenu: hasNativePageLayoutMenu");
        windowSource.Should().Contain("HasNativeFormulasMenu: hasNativeFormulasMenu");
        windowSource.Should().Contain("HasNativeDataMenu: hasNativeDataMenu");
        windowSource.Should().Contain("HasNativeViewMenu: hasNativeViewMenu");
        windowSource.Should().Contain("HasNativeHelpMenu: hasNativeHelpMenu");
        windowSource.Should().Contain("HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, NativeMenuItemId.Undo)");
        windowSource.Should().Contain("HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, NativeMenuItemId.Redo)");
        windowSource.Should().Contain("HasNativeCutMenuItem: HasNativeMenuItem(_cutMenuItem, NativeMenuItemId.Cut)");
        windowSource.Should().Contain("HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, NativeMenuItemId.Copy)");
        windowSource.Should().Contain("HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, NativeMenuItemId.Paste)");
        windowSource.Should().Contain("HasNativePasteSpecialMenuItem: HasNativeMenuItem(_pasteSpecialMenuItem, NativeMenuItemId.PasteSpecial)");
        windowSource.Should().Contain("HasNativePasteSpecialCommentsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Comments and Notes\")");
        windowSource.Should().Contain("HasNativePasteSpecialValidationMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Validation\")");
        windowSource.Should().Contain("HasNativePasteSpecialAllExceptBordersMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"All Except Borders\")");
        windowSource.Should().Contain("HasNativePasteSpecialAllMergingConditionalFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"All Merging Conditional Formats\")");
        windowSource.Should().Contain("HasNativePasteSpecialColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Column Widths\")");
        windowSource.Should().Contain("HasNativePasteSpecialFormulasAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Formulas and Number Formats\")");
        windowSource.Should().Contain("HasNativePasteSpecialValuesAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Values and Number Formats\")");
        windowSource.Should().Contain("HasNativePasteSpecialValuesAndSourceFormattingMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Values and Source Formatting\")");
        windowSource.Should().Contain("HasNativePasteSpecialKeepSourceColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Keep Source Column Widths\")");
        windowSource.Should().Contain("HasNativePasteSpecialPasteLinkMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Paste Link\")");
        windowSource.Should().Contain("HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Text\")");
        windowSource.Should().Contain("HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Unicode Text\")");
        windowSource.Should().Contain("HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Picture\")");
        windowSource.Should().Contain("HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, \"Linked Picture\")");
        windowSource.Should().Contain("private static bool HasNativeSubmenuItem(NativeMenu? menu, string expectedHeader)");
        windowSource.Should().Contain("HasNativeSelectAllMenuItem: HasNativeMenuItem(_selectAllMenuItem, NativeMenuItemId.SelectAll)");
        windowSource.Should().Contain("HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, NativeMenuItemId.Find)");
        windowSource.Should().Contain("HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, NativeMenuItemId.FindNext)");
        windowSource.Should().Contain("HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, NativeMenuItemId.Replace)");
        windowSource.Should().Contain("HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, NativeMenuItemId.GoTo)");
        windowSource.Should().Contain("HasClearButton: _clearButton.Content?.ToString() == \"Clear\"");
        windowSource.Should().Contain("HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, \"Clear All\")");
        windowSource.Should().Contain("HasClearFormatsMenuItem: HasToolbarMenuItem(_clearFormatsFlyoutItem, \"Clear Formats\")");
        windowSource.Should().Contain("HasClearContentsMenuItem: HasToolbarMenuItem(_clearContentsFlyoutItem, \"Clear Contents\")");
        windowSource.Should().Contain("HasClearCommentsMenuItem: HasToolbarMenuItem(_clearCommentsFlyoutItem, \"Clear Comments and Notes\")");
        windowSource.Should().Contain("HasClearHyperlinksMenuItem: HasToolbarMenuItem(_clearHyperlinksFlyoutItem, \"Clear Hyperlinks\")");
        windowSource.Should().Contain("private static bool HasToolbarMenuItem(MenuItem item, string expectedHeader)");
        windowSource.Should().Contain("HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, NativeMenuItemId.Clear)");
        windowSource.Should().Contain("HasNativeClearAllMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearAll)");
        windowSource.Should().Contain("HasNativeClearFormatsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearFormats)");
        windowSource.Should().Contain("HasNativeClearContentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearContents)");
        windowSource.Should().Contain("HasNativeClearCommentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearComments)");
        windowSource.Should().Contain("HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearHyperlinks)");
        windowSource.Should().Contain("HasNativeBoldMenuItem: HasNativeMenuItem(_boldMenuItem, NativeMenuItemId.Bold)");
        windowSource.Should().Contain("HasNativeItalicMenuItem: HasNativeMenuItem(_italicMenuItem, NativeMenuItemId.Italic)");
        windowSource.Should().Contain("HasNativeUnderlineMenuItem: HasNativeMenuItem(_underlineMenuItem, NativeMenuItemId.Underline)");
        windowSource.Should().Contain("HasNativeDoubleUnderlineMenuItem: HasNativeMenuItem(_doubleUnderlineMenuItem, NativeMenuItemId.DoubleUnderline)");
        windowSource.Should().Contain("HasNativeStrikethroughMenuItem: HasNativeMenuItem(_strikethroughMenuItem, NativeMenuItemId.Strikethrough)");
        windowSource.Should().Contain("HasNativeIncreaseFontSizeMenuItem: HasNativeMenuItem(_increaseFontSizeMenuItem, NativeMenuItemId.IncreaseFontSize)");
        windowSource.Should().Contain("HasNativeDecreaseFontSizeMenuItem: HasNativeMenuItem(_decreaseFontSizeMenuItem, NativeMenuItemId.DecreaseFontSize)");
        windowSource.Should().Contain("HasNativeFillColorMenuItem: HasNativeMenuItem(_fillColorMenuItem, NativeMenuItemId.FillColor)");
        windowSource.Should().Contain("HasNativeClearFillMenuItem: HasNativeMenuItem(_clearFillMenuItem, NativeMenuItemId.ClearFill)");
        windowSource.Should().Contain("HasNativeFontColorMenuItem: HasNativeMenuItem(_fontColorMenuItem, NativeMenuItemId.FontColor)");
        windowSource.Should().Contain("NativeFillColorSwatchCount: nativeFillColorSwatchCount");
        windowSource.Should().Contain("NativeFontColorSwatchCount: nativeFontColorSwatchCount");
        windowSource.Should().Contain("HasBordersButton: _bordersButton.Content?.ToString() == \"Borders\"");
        windowSource.Should().Contain("HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == \"Merge & Center\"");
        windowSource.Should().Contain("HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, NativeMenuItemId.Borders)");
        windowSource.Should().Contain("NativeBordersPresetCount: nativeBordersPresetCount");
        windowSource.Should().Contain("HasNativeCellStylesMenuItem: HasNativeMenuItem(_cellStylesMenuItem, NativeMenuItemId.CellStyles)");
        windowSource.Should().Contain("NativeCellStylesPresetCount: nativeCellStylesPresetCount");
        windowSource.Should().Contain("HasNativeHorizontalTextMenuItem: HasNativeMenuItem(_horizontalTextMenuItem, NativeMenuItemId.HorizontalText)");
        windowSource.Should().Contain("HasNativeAngleCounterclockwiseMenuItem: HasNativeMenuItem(_angleCounterclockwiseMenuItem, NativeMenuItemId.AngleCounterclockwise)");
        windowSource.Should().Contain("HasNativeAngleClockwiseMenuItem: HasNativeMenuItem(_angleClockwiseMenuItem, NativeMenuItemId.AngleClockwise)");
        windowSource.Should().Contain("HasNativeVerticalTextMenuItem: HasNativeMenuItem(_verticalTextMenuItem, NativeMenuItemId.VerticalText)");
        windowSource.Should().Contain("HasNativeRotateTextUpMenuItem: HasNativeMenuItem(_rotateTextUpMenuItem, NativeMenuItemId.RotateTextUp)");
        windowSource.Should().Contain("HasNativeRotateTextDownMenuItem: HasNativeMenuItem(_rotateTextDownMenuItem, NativeMenuItemId.RotateTextDown)");
        windowSource.Should().Contain("HasNativeCurrencyFormatMenuItem: HasNativeMenuItem(_currencyFormatMenuItem, NativeMenuItemId.CurrencyFormat)");
        windowSource.Should().Contain("HasNativePercentFormatMenuItem: HasNativeMenuItem(_percentFormatMenuItem, NativeMenuItemId.PercentFormat)");
        windowSource.Should().Contain("HasNativeCommaStyleMenuItem: HasNativeMenuItem(_commaStyleMenuItem, NativeMenuItemId.CommaStyle)");
        windowSource.Should().Contain("HasNativeIncreaseDecimalMenuItem: HasNativeMenuItem(_increaseDecimalMenuItem, NativeMenuItemId.IncreaseDecimal)");
        windowSource.Should().Contain("HasNativeDecreaseDecimalMenuItem: HasNativeMenuItem(_decreaseDecimalMenuItem, NativeMenuItemId.DecreaseDecimal)");
        windowSource.Should().Contain("HasNativeAlignTopMenuItem: HasNativeMenuItem(_alignTopMenuItem, NativeMenuItemId.AlignTop)");
        windowSource.Should().Contain("HasNativeAlignMiddleMenuItem: HasNativeMenuItem(_alignMiddleMenuItem, NativeMenuItemId.AlignMiddle)");
        windowSource.Should().Contain("HasNativeAlignBottomMenuItem: HasNativeMenuItem(_alignBottomMenuItem, NativeMenuItemId.AlignBottom)");
        windowSource.Should().Contain("HasWrapTextButton: _wrapTextButton.Content?.ToString() == \"Wrap\"");
        windowSource.Should().Contain("HasNativeWrapTextMenuItem: HasNativeMenuItem(_wrapTextMenuItem, NativeMenuItemId.WrapText)");
        windowSource.Should().Contain("HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, NativeMenuItemId.MergeAndCenter)");
        windowSource.Should().Contain("HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, NativeMenuItemId.UnmergeCells)");
        windowSource.Should().Contain("HasNativeShowGridlinesMenuItem: HasNativeMenuItem(_showGridlinesMenuItem, NativeMenuItemId.ShowGridlines)");
        windowSource.Should().Contain("HasNativeShowHeadingsMenuItem: HasNativeMenuItem(_showHeadingsMenuItem, NativeMenuItemId.ShowHeadings)");
        windowSource.Should().Contain("HasNativeZoomInMenuItem: HasNativeMenuItem(_zoomInMenuItem, NativeMenuItemId.ZoomIn)");
        windowSource.Should().Contain("HasNativeZoomOutMenuItem: HasNativeMenuItem(_zoomOutMenuItem, NativeMenuItemId.ZoomOut)");
        windowSource.Should().Contain("HasNativeZoom100MenuItem: HasNativeMenuItem(_zoom100MenuItem, NativeMenuItemId.Zoom100)");
        windowSource.Should().Contain("HasNativeZoomToSelectionMenuItem: HasNativeMenuItem(_zoomToSelectionMenuItem, NativeMenuItemId.ZoomToSelection)");
        windowSource.Should().Contain("HasNativeFreezePanesMenuItem: HasNativeMenuItem(_freezePanesMenuItem, NativeMenuItemId.FreezePanes)");
        windowSource.Should().Contain("HasNativeFreezeTopRowMenuItem: HasNativeMenuItem(_freezeTopRowMenuItem, NativeMenuItemId.FreezeTopRow)");
        windowSource.Should().Contain("HasNativeFreezeFirstColumnMenuItem: HasNativeMenuItem(_freezeFirstColumnMenuItem, NativeMenuItemId.FreezeFirstColumn)");
        windowSource.Should().Contain("HasNativeUnfreezePanesMenuItem: HasNativeMenuItem(_unfreezePanesMenuItem, NativeMenuItemId.UnfreezePanes)");
        windowSource.Should().Contain("HasNativeDecreaseIndentMenuItem: HasNativeMenuItem(_decreaseIndentMenuItem, NativeMenuItemId.DecreaseIndent)");
        windowSource.Should().Contain("HasNativeIncreaseIndentMenuItem: HasNativeMenuItem(_increaseIndentMenuItem, NativeMenuItemId.IncreaseIndent)");
        windowSource.Should().Contain("HasNativeAlignLeftMenuItem: HasNativeMenuItem(_alignLeftMenuItem, NativeMenuItemId.AlignLeft)");
        windowSource.Should().Contain("HasNativeAlignCenterMenuItem: HasNativeMenuItem(_alignCenterMenuItem, NativeMenuItemId.AlignCenter)");
        windowSource.Should().Contain("HasNativeAlignRightMenuItem: HasNativeMenuItem(_alignRightMenuItem, NativeMenuItemId.AlignRight)");
        windowSource.Should().Contain("HasNativeShowFormulasMenuItem: HasNativeMenuItem(_showFormulasMenuItem, NativeMenuItemId.ShowFormulas)");
        windowSource.Should().Contain("HasNativeHelpOnlineMenuItem: HasNativeMenuItem(_helpOnlineMenuItem, NativeMenuItemId.HelpOnline)");
        windowSource.Should().Contain("HasNativeSendFeedbackMenuItem: HasNativeMenuItem(_sendFeedbackMenuItem, NativeMenuItemId.SendFeedback)");
        windowSource.Should().Contain("HasNativeCheckForUpdatesMenuItem: HasNativeMenuItem(_checkForUpdatesMenuItem, NativeMenuItemId.CheckForUpdates)");
        windowSource.Should().Contain("HasNativeAboutMenuItem: HasNativeMenuItem(_aboutMenuItem, NativeMenuItemId.About)");
        windowSource.Should().Contain("HasNativeLegalNoticesMenuItem: HasNativeMenuItem(_legalNoticesMenuItem, NativeMenuItemId.LegalNotices)");
        windowSource.Should().Contain("HasNativeQuitMenuItem: HasNativeFileMenuItem(_quitMenuItem, NativeFileMenuItemId.Quit)");
    }

    [Fact]
    public void MainWindow_RendersBasicDrawingObjectPreviewOverlay()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("_sessionFactory.Create(source, InitialViewportHeight, InitialViewportWidth, includeObjects: true)");
        source.Should().Contain("_sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth, includeObjects: true)");
        source.Should().Contain("private Canvas BuildDrawingObjectOverlay(ViewportModel viewport)");
        source.Should().Contain("viewport.DrawingObjects is not { Count: > 0 }");
        source.Should().Contain("foreach (var renderPlan in DrawingObjectRenderPlanner.Plan(viewport))");
        source.Should().Contain("var drawingObject = renderPlan.Bounds;");
        source.Should().Contain("TryGetDisplayedDrawingObjectBounds(");
        source.Should().Contain("var visual = CreateSelectableDrawingObjectVisual(renderPlan, width, height);");
        source.Should().Contain("private Control CreateSelectableDrawingObjectVisual(");
        source.Should().Contain("DrawingObjectRenderPlan renderPlan,");
        source.Should().Contain("IsHitTestVisible = true");
        source.Should().Contain("Focusable = true");
        source.Should().Contain("AutomationProperties.SetAutomationId(container, $\"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}\");");
        source.Should().Contain("AutomationProperties.SetName(container, $\"{FormatDrawingObjectKind(drawingObject.Kind)} {drawingObject.DisplayName}\");");
        source.Should().Contain("AutomationProperties.SetHelpText(container, \"Selects this drawing object preview in the workbook viewport.\");");
        source.Should().Contain("AutomationProperties.SetItemStatus(container, selected ? \"Selected\" : \"Not selected\");");
        source.Should().Contain("container.PointerPressed += (_, args) =>");
        source.Should().Contain("args.GetCurrentPoint(container).Properties.IsLeftButtonPressed");
        source.Should().Contain("container.KeyDown += (_, args) =>");
        source.Should().Contain("if (args.Key is Key.Enter or Key.Space)");
        source.Should().Contain("SelectDrawingObject(drawingObject);");
        source.Should().Contain("? CreateDrawingObjectSelectionAdorner(width, height, drawingObject.RotationDegrees)");
        source.Should().Contain("WireDrawingObjectDragMoveRelease(renderPlan, container, surface);");
        source.Should().Contain("TryBeginDrawingObjectDrag(renderPlan, container, surface, adorner, args)");
        source.Should().Contain("private void SelectDrawingObject(DrawingObjectBounds drawingObject)");
        source.Should().Contain("_selectedDrawingObjectKind = drawingObject.Kind;");
        source.Should().Contain("_selectedDrawingObjectId = drawingObject.Id;");
        source.Should().Contain("RefreshShell($\"Selected {FormatDrawingObjectKind(drawingObject.Kind)}: {drawingObject.DisplayName}\");");
        source.Should().Contain("private bool IsSelectedDrawingObject(DrawingObjectBounds drawingObject)");
        source.Should().Contain("private void ClearSelectedDrawingObject()");
        source.Should().Contain("private static string FormatDrawingObjectKind(SelectionPaneObjectKind kind)");
        source.Should().Contain("private static Control CreateDrawingObjectVisual(");
        source.Should().Contain("DrawingObjectRenderPrimitiveKind.Shape => CreateDrawingShapeVisual(drawingObject, width, height)");
        source.Should().Contain("DrawingObjectRenderPrimitiveKind.Image or DrawingObjectRenderPrimitiveKind.CroppedImage");
        source.Should().Contain("CreateDrawingImageVisual(renderPlan, width, height)");
        source.Should().Contain("DrawingObjectRenderPrimitiveKind.CellRangeSnapshot => CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height, theme)");
        source.Should().Contain("DrawingObjectRenderPrimitiveKind.TextBox => CreateDrawingTextBoxVisual(drawingObject, width, height)");
        source.Should().Contain("private static Control CreateDrawingShapeVisual(");
        source.Should().Contain("using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;");
        source.Should().Contain("using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;");
        source.Should().Contain("DrawingShapeKind.Ellipse => CreateEllipseShapeVisual(fill, strokeBrush, strokeThickness, dashArray, w, h),");
        source.Should().Contain("_ => CreateDrawingShapeGeometryVisual(drawingObject.ShapeKind, fill, strokeBrush, strokeThickness, dashArray, w, h),");
        source.Should().Contain("private static Control CreateDrawingShapeGeometryVisual(");
        source.Should().Contain("new AvaloniaRectangle");
        source.Should().Contain("private static Control CreateDrawingImageVisual(");
        source.Should().Contain("TryCreateDrawingBitmap(imageBytes, out var bitmap)");
        source.Should().Contain("new ImageBrush(bitmap)");
        source.Should().Contain("SourceRect = CreateDrawingImageSourceRect(crop)");
        source.Should().Contain("private static Control CreateDrawingCellRangeSnapshotVisual(");
        source.Should().Contain("renderPlan.PictureGrid is not { } pictureGrid");
        // Round-8 finding N52: PictureModel.Cells has no uniqueness constraint on (RowOffset,
        // ColumnOffset), so a straight .ToDictionary(...) throws on adversarial/hand-edited .fxl
        // files with duplicate offsets. The render was reshaped into a dedup-safe manual last-wins
        // loop (still keyed by the same tuple), so pin that shape instead of the old ToDictionary call.
        source.Should().Contain("var cellLookup = new Dictionary<(uint RowOffset, uint ColumnOffset), PictureCellSnapshot>();");
        source.Should().Contain("foreach (var cell in pictureGrid.Cells)");
        source.Should().Contain("cellLookup[(cell.RowOffset, cell.ColumnOffset)] = cell;");
        source.Should().Contain("Source = bitmap");
        source.Should().Contain("private static Control CreateDrawingTextBoxVisual(");
        source.Should().Contain("drawingObject.Text");
        source.Should().Contain("drawingObject.AnchorCol");
        source.Should().Contain("drawingObject.AnchorRow");
        source.Should().Contain("Canvas.SetLeft(visual, left - (selected ? DrawingObjectSelectionHorizontalPadding : 0));");
        source.Should().Contain("Canvas.SetTop(visual, top - (selected ? DrawingObjectSelectionTopPadding : 0));");
        source.Should().Contain("GetDisplayedColumnWidth(metric, zoomFactor)");
        source.Should().Contain("GetDisplayedRowHeight(metric, zoomFactor)");
        source.Should().Contain("ApplyDrawingObjectTransform(");
        source.Should().Contain("drawingObject.FlipHorizontal");
        source.Should().Contain("drawingObject.FlipVertical");
        source.Should().Contain("new RotateTransform(rotationDegrees)");
        source.Should().Contain("new ScaleTransform(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1)");
    }

    [Fact]
    public void MainWindow_WiresManualWorksheetScrollBarsToSessionViewport()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ScrollBar _verticalWorksheetScrollBar = new();");
        source.Should().Contain("private readonly ScrollBar _horizontalWorksheetScrollBar = new();");
        source.Should().Contain("private bool _isUpdatingWorksheetScrollBars;");
        source.Should().Contain("workArea.Children.Add(BuildWorksheetViewportChrome());");
        source.Should().Contain("_sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;");
        source.Should().Contain("_sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;");
        source.Should().Contain("_verticalWorksheetScrollBar.Orientation = Orientation.Vertical;");
        source.Should().Contain("_horizontalWorksheetScrollBar.Orientation = Orientation.Horizontal;");
        source.Should().Contain("_verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;");
        source.Should().Contain("_horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;");
        source.Should().Contain("WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)");
        source.Should().Contain("ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);");
        source.Should().Contain("ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin(");
        source.Should().Contain("_session.SetViewportOrigin(topRow, leftCol)");
        source.Should().Contain("UpdateViewportScrollBars();");
    }

    [Fact]
    public void MainWindow_WiresCellEditingThroughFormulaBoxAndKeyboardEntry()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly TextBox _formulaBox = new();");
        source.Should().Contain("private string? _formulaBoxEditOriginalText;");
        source.Should().Contain("_formulaBox.GotFocus += FormulaBox_GotFocus;");
        source.Should().Contain("_formulaBox.KeyDown += FormulaBox_KeyDown;");
        source.Should().Contain("TextInput += MainWindow_TextInput;");
        source.Should().Contain("border.DoubleTapped += (_, args) =>");
        source.Should().Contain("BeginFormulaEdit(address);");
        source.Should().Contain("private void FormulaBox_GotFocus(object? sender, FocusChangedEventArgs e)");
        source.Should().Contain("_session.BeginFormulaEdit(_session.ActiveCell);");
        source.Should().Contain("_formulaBoxEditOriginalText = _formulaBox.Text ?? \"\";");
        source.Should().Contain("if (e.Key == Key.F2)");
        source.Should().Contain("BeginFormulaEdit(_session.ActiveCell);");
        source.Should().Contain("private void MainWindow_TextInput(object? sender, TextInputEventArgs e)");
        source.Should().Contain("string.IsNullOrEmpty(e.Text)");
        source.Should().Contain("char.IsControl(character)");
        source.Should().Contain("BeginFormulaEdit(_session.ActiveCell, e.Text);");
        source.Should().Contain("ExcelEditKeyPlanner.GetIntent(");
        source.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorKey(e.Key)");
        source.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorModifiers(e.KeyModifiers)");
        source.Should().Contain("intent.Action == ExcelEditKeyAction.CommitAndMove");
        // M-round12 (R12-avalonia-parity-deep) made Enter/Tab commit-and-move merge-aware: the
        // formula box now resolves the landing cell through ExcelWorksheetNavigationPlanner's
        // shared AdjustTargetPastMerge helper (mirrors the inline cell editor and the WPF host)
        // instead of moving straight to the raw intent.Target.
        source.Should().Contain("var adjustedTarget = ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(");
        source.Should().Contain("var rowDelta = GetCellIndexDelta(current.Row, adjustedTarget.Row);");
        source.Should().Contain("var colDelta = GetCellIndexDelta(current.Col, adjustedTarget.Col);");
        source.Should().Contain("_session.MoveActiveCell(rowDelta, colDelta);");
        source.Should().Contain("var result = _session.CommitCellText(_formulaBox.Text ?? \"\", UseR1C1ReferenceStyle);");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("Finish saving before editing cells.");
        source.Should().Contain("RefreshShell($\"Edited {FormatCellReference(address)}\");");
        source.Should().Contain("private bool TryCommitPendingFormulaEdit()");
        source.Should().Contain("private bool HasPendingFormulaEditText() =>");
        source.Should().Contain("_session.CancelFormulaEdit();");
        source.Should().Contain("StringComparison.Ordinal");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("Finish the current cell edit before opening another workbook.");
    }

    [Fact]
    public void MainWindow_SaveCapturesXlsxWarningsInAvaloniaStatus()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("var saveWarnings = await _saveService.SaveAsync");
        source.Should().Contain("RefreshShell(FormatSaveCompletionStatus(targetPath, saveWarnings));");
        source.Should().Contain("private static string FormatSaveCompletionStatus(string path, IReadOnlyList<string> warnings)");
        source.Should().Contain("with {warnings.Count} warning(s)");
    }

    [Fact]
    public void MainWindow_WiresUndoRedoThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly Button _undoButton = new();");
        source.Should().Contain("private readonly Button _redoButton = new();");
        source.Should().Contain("_undoButton.Content = \"Undo\";");
        source.Should().Contain("_redoButton.Content = \"Redo\";");
        source.Should().Contain("_undoButton.Click += UndoButton_Click;");
        source.Should().Contain("_redoButton.Click += RedoButton_Click;");
        source.Should().Contain("_undoButton.IsEnabled = isIdle && _session.CanUndo;");
        source.Should().Contain("_redoButton.IsEnabled = isIdle && _session.CanRedo;");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Undo", "WorkbookShortcutKey.Z", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Redo", "WorkbookShortcutKey.Y", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Redo", "WorkbookShortcutKey.Z", "WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift");
        AssertWorkbookShortcutRouteHandled(source, "Undo", "UndoLastEdit();");
        AssertWorkbookShortcutRouteHandled(source, "Redo", "RedoLastEdit();");
        source.Should().Contain("private void UndoLastEdit()");
        source.Should().Contain("private void RedoLastEdit()");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("ApplyEditHistoryResult(_session.UndoLastEdit(), \"Undid last edit\");");
        source.Should().Contain("ApplyEditHistoryResult(_session.RedoLastEdit(), \"Redid last edit\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Edit history unavailable.\");");
    }

    [Fact]
    public void MainWindow_WiresClipboardCopyPasteThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly Button _cutButton = new();");
        source.Should().Contain("private readonly Button _copyButton = new();");
        source.Should().Contain("private readonly Button _pasteButton = new();");
        source.Should().Contain("private readonly DropDownButton _pasteSpecialButton = new();");
        source.Should().Contain("_cutButton.Content = \"Cut\";");
        source.Should().Contain("_copyButton.Content = \"Copy\";");
        source.Should().Contain("_pasteButton.Content = \"Paste\";");
        source.Should().Contain("_pasteSpecialButton.Content = \"Paste Special\";");
        source.Should().Contain("_pasteSpecialButton.Flyout = CreatePasteSpecialFlyout();");
        source.Should().Contain("_cutButton.Click += CutButton_Click;");
        source.Should().Contain("_copyButton.Click += CopyButton_Click;");
        source.Should().Contain("_pasteButton.Click += PasteButton_Click;");
        source.Should().Contain("_cutButton.IsEnabled = isIdle;");
        source.Should().Contain("_copyButton.IsEnabled = isIdle;");
        source.Should().Contain("_pasteButton.IsEnabled = isIdle;");
        source.Should().Contain("_pasteSpecialButton.IsEnabled = isIdle;");
        source.Should().Contain("private async Task CutSelectedRangeToClipboardAsync()");
        source.Should().Contain("private async Task CopySelectedRangeToClipboardAsync()");
        source.Should().Contain("private async Task PasteClipboardTextAsync()");
        source.Should().Contain("private async Task PasteSpecialClipboardTextAsync(");
        source.Should().Contain("private MenuFlyout CreatePasteSpecialFlyout()");
        source.Should().Contain("private NativeMenu CreateNativePasteSpecialMenu()");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Values\", PasteCellsMode.Values, default)");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Formulas\", PasteCellsMode.Formulas, default)");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Formats\", PasteCellsMode.Formats, default)");
        source.Should().Contain("CreatePasteCommentsMenuItem(\"Comments and Notes\")");
        source.Should().Contain("CreatePasteDataValidationMenuItem(\"Validation\")");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"All Except Borders\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"All Merging Conditional Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))");
        source.Should().Contain("CreatePasteColumnWidthsMenuItem(\"Column Widths\")");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Formulas and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Values and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Values and Source Formatting\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Keep Source Column Widths\", PasteCellsMode.All, default, keepSourceColumnWidths: true)");
        source.Should().Contain("CreatePasteLinkMenuItem(\"Paste Link\")");
        source.Should().Contain("CreatePasteSpecialTextMenuItem(\"Text\")");
        source.Should().Contain("CreatePasteSpecialTextMenuItem(\"Unicode Text\")");
        source.Should().Contain("CreatePastePictureMenuItem(\"Picture\", linkedPicture: false)");
        source.Should().Contain("CreatePastePictureMenuItem(\"Linked Picture\", linkedPicture: true)");
        source.Should().Contain("CreateNativePasteCommentsMenuItem(\"Comments and Notes\")");
        source.Should().Contain("CreateNativePasteDataValidationMenuItem(\"Validation\")");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"All Except Borders\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"All Merging Conditional Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))");
        source.Should().Contain("CreateNativePasteColumnWidthsMenuItem(\"Column Widths\")");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Formulas and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Values and Number Formats\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Values and Source Formatting\", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))");
        source.Should().Contain("CreateNativePasteSpecialMenuItem(\"Keep Source Column Widths\", PasteCellsMode.All, default, keepSourceColumnWidths: true)");
        source.Should().Contain("CreateNativePasteLinkMenuItem(\"Paste Link\")");
        source.Should().Contain("CreateNativePasteSpecialTextMenuItem(\"Text\")");
        source.Should().Contain("CreateNativePasteSpecialTextMenuItem(\"Unicode Text\")");
        source.Should().Contain("CreateNativePastePictureMenuItem(\"Picture\", linkedPicture: false)");
        source.Should().Contain("CreateNativePastePictureMenuItem(\"Linked Picture\", linkedPicture: true)");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Transpose\", PasteCellsMode.All, new PasteSpecialOptions(Transpose: true))");
        source.Should().Contain("CreatePasteSpecialMenuItem(\"Skip Blanks\", PasteCellsMode.All, new PasteSpecialOptions(SkipBlanks: true))");
        source.Should().Contain("new PasteSpecialOptions(Operation: PasteSpecialOperation.Add)");
        source.Should().Contain("using Avalonia.Input.Platform;");
        source.Should().Contain("using FreeX.Core.Commands;");
        source.Should().Contain("TopLevel.GetTopLevel(this)?.Clipboard");
        source.Should().Contain("var cutResult = _session.TryCutSelectedRangeText();");
        source.Should().Contain("await clipboard.SetTextAsync(cutResult.Text);");
        source.Should().Contain("var copyResult = _session.TryCopySelectedRangeText();");
        // Copy places plain text AND an HTML table fragment on the OS clipboard together (review
        // P47 — parity with real Excel and the WPF host's M7 CF_HTML export), via a DataTransfer
        // instead of the plain SetTextAsync used by Cut (which does not need HTML — Excel's own
        // Cut clipboard payload is plain-text-only in practice for this shell's parity target).
        source.Should().Contain("using var transfer = new DataTransfer();");
        // R14-clipboard-formats-deep-1: the on-screen _session.Viewport truncates any part of the
        // selection scrolled out of view, so the CF_HTML fragment must be built from the full-range
        // viewport TryCopySelectedRangeText() already constructed for the same range (falling back to
        // the on-screen Viewport only if a result somehow carries none), mirroring the WPF host's P41
        // fix (MainWindow.BuildFullRangeViewportForClipboard).
        source.Should().Contain("AddClipboardTextAndHtml(transfer, copiedText, copyResult.Viewport ?? _session.Viewport, _session.ActiveSheet, _session.SelectedRange, _session.Workbook.Theme);");
        source.Should().Contain("await clipboard.SetDataAsync(transfer);");
        source.Should().Contain("var text = await clipboard.TryGetTextAsync();");
        source.Should().Contain("_session.ShouldPreferExternalClipboardImage(text)");
        // R68-async-ordering-race-sweep-1: destination dropped as a parameter -- it is now
        // captured as _session.ActiveCell inside the method, right before use, so a caller can
        // no longer hand in a destination that goes stale across the bitmap-read await.
        source.Should().Contain("private async Task<bool> TryPasteClipboardImageAsync(IClipboard clipboard)");
        source.Should().Contain("await clipboard.TryGetBitmapAsync()");
        source.Should().Contain("bitmap.Save(stream)");
        source.Should().Contain("_session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight)");
        // R66-services-clipboard-formats-6-1: every external-clipboard paste call site also reads
        // the OS clipboard's HTML payload (TryGetClipboardHtmlAsync) and forwards it as `html`, so
        // WorkbookSession.PasteExternalTextAtActiveCell's HTML-table-aware row/column recovery is
        // actually reachable from this shell (previously only the WPF host read it).
        source.Should().Contain("_session.PasteClipboardTextAtActiveCell(text, clipboardReadFailed: clipboardReadFailed, html: html)");
        source.Should().Contain("_session.PasteSpecialClipboardAtActiveCell(text, mode, options, clipboardReadFailed: clipboardReadFailed, html: html)");
        source.Should().Contain("_session.PasteSpecialClipboardAtActiveCell(text, mode, options, keepSourceColumnWidths: true, clipboardReadFailed: clipboardReadFailed, html: html)");
        source.Should().Contain("private async Task PasteColumnWidthsFromClipboardAsync(string label)");
        source.Should().Contain("_session.PasteColumnWidthsFromClipboardAtActiveCell(text)");
        source.Should().Contain("private async Task PasteCommentsFromClipboardAsync(string label)");
        source.Should().Contain("_session.PasteCommentsFromClipboardAtActiveCell(text)");
        source.Should().Contain("private async Task PasteDataValidationFromClipboardAsync(string label)");
        source.Should().Contain("_session.PasteDataValidationFromClipboardAtActiveCell(text)");
        source.Should().Contain("private async Task PasteLinkFromClipboardAsync(string label)");
        source.Should().Contain("_session.PasteLinkFromClipboardAtActiveCell(text)");
        source.Should().Contain("private async Task PasteSpecialExternalTextFromClipboardAsync(string label)");
        source.Should().Contain("_session.PasteClipboardTextAtActiveCell(text, preserveText: true, clipboardReadFailed: clipboardReadFailed, html: html)");
        source.Should().Contain("private async Task PastePictureFromClipboardAsync(string label, bool linkedPicture)");
        source.Should().Contain("_session.PastePictureFromClipboardAtActiveCell(text, linkedPicture)");
        source.Should().Contain("_session.SelectedRanges.Any(range => range.Contains(address))");
        source.Should().Contain("private bool IsSelectedColumn(uint col)");
        source.Should().Contain("private bool IsSelectedRow(uint row)");
        source.Should().Contain("private bool IsSelectedCell(CellAddress address)");
        source.Should().Contain("args.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("_session.SelectAnchoredRange(anchor, address);");
        source.Should().Contain("private static string FormatRangeReference(GridRange range)");
        source.Should().Contain("private void SelectCurrentRegionOrAll()");
        source.Should().Contain("var range = _session.SelectCurrentRegionOrAll();");
        source.Should().Contain("if (_formulaBox.IsFocused &&");
        source.Should().Contain("e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U or Key.D4 or Key.NumPad4 or Key.D5 or Key.NumPad5)");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Cut", "WorkbookShortcutKey.X", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Copy", "WorkbookShortcutKey.C", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Paste", "WorkbookShortcutKey.V", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "Cut", "await CutSelectedRangeToClipboardAsync();");
        AssertWorkbookShortcutRouteHandled(source, "Copy", "await CopySelectedRangeToClipboardAsync();");
        AssertWorkbookShortcutRouteHandled(source, "Paste", "await PasteClipboardTextAsync();");
        source.Should().Contain("else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers))");
        source.Should().Contain("SelectCurrentRegionOrAll();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Paste Special failed.\");");
        source.Should().Contain("ShowEditIssue(\"Clipboard unavailable on this platform.\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Paste failed.\");");
    }

    [Fact]
    public void MainWindow_WiresFormatPainterThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("private readonly Button _formatPainterButton = new();");
        source.Should().Contain("private readonly NativeMenuItem _formatPainterMenuItem = new();");
        source.Should().Contain("_formatPainterButton.Content = \"Format Painter\";");
        source.Should().Contain("_formatPainterButton.Click += FormatPainterButton_Click;");
        source.Should().Contain("_formatPainterButton.DoubleTapped += (_, args) =>");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true);");
        source.Should().Contain("AutomationProperties.SetAutomationId(_formatPainterButton, \"HomeFormatPainterButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_formatPainterButton, \"Copy formatting from the selection and apply it to another range.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.FormatPainter, \"Format Painter\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_formatPainterMenuItem.Click += (_, _) => CaptureFormatPainterSource(persistent: false);");
        source.Should().Contain("NativeMenuItemId.FormatPainter => _formatPainterMenuItem,");
        source.Should().Contain("var homeMenu = CreateNativeMenu(NativeMenuTopLevelId.Home);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.FormatPainter)");
        source.Should().Contain("_formatPainterButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FormatPainter, context.CanFormatPainter)");
        source.Should().Contain("_formatPainterButton,");
        source.Should().Contain("private void FormatPainterButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void CaptureFormatPainterSource(bool persistent)");
        source.Should().Contain("_session.CaptureFormatPainterSource(persistent)");
        source.Should().Contain("private void ApplyFormatPainterAfterTargetSelection()");
        source.Should().Contain("_session.ApplyFormatPainterToSelectedRange()");
        source.Should().Contain("private void CancelFormatPainter()");
        source.Should().Contain("_session.CancelFormatPainter();");
        source.Should().Contain("e.Key == Key.Escape && _session.IsFormatPainterActive");
        source.Should().Contain("ApplyFormatPainterAfterTargetSelection();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Format Painter failed.\");");
        source.Should().Contain("HasFormatPainterButton: _formatPainterButton.Content?.ToString() == \"Format Painter\"");
        source.Should().Contain("HomeFormatPainterButton");
        source.Should().Contain("HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, NativeMenuItemId.FormatPainter)");

        smokeSource.Should().Contain("bool HasFormatPainterButton,");
        smokeSource.Should().Contain("bool HasNativeFormatPainterMenuItem,");
        smokeSource.Should().Contain("HasFormatPainterButton &&");
        smokeSource.Should().Contain("HasNativeFormatPainterMenuItem &&");
        smokeSource.Should().Contain("toolbar_format_painter_button={FormatBool(snapshot.HasFormatPainterButton)}");
        smokeSource.Should().Contain("native_format_painter_menu_item={FormatBool(snapshot.HasNativeFormatPainterMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresAutoSumMenuThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        sessionSource.Should().Contain("public WorkbookCellEditResult InsertAutoSumFormula(string functionName)");
        sessionSource.Should().Contain("AutoSumFormulaPlanner.TryCreatePlan(ActiveSheet, functionName, SelectedRange, out var plan)");
        sessionSource.Should().Contain("CreateEditCellsCommand([(plan.Target, Cell.FromFormula(plan.Formula))])");
        sessionSource.Should().Contain("ApplySuccessfulEditResult(result, plan.Target);");
        sessionSource.Should().NotContain("GetNextAutoSumCell");

        source.Should().Contain("private readonly DropDownButton _autoSumButton = new();");
        source.Should().Contain("private readonly MenuItem _autoSumSumFlyoutItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _autoSumMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _autoSumSumMenuItem = new();");
        source.Should().Contain("_autoSumButton.Content = \"AutoSum\";");
        source.Should().Contain("_autoSumButton.Click += AutoSumButton_Click;");
        source.Should().Contain("_autoSumButton.Flyout = CreateAutoSumFlyout();");
        source.Should().Contain("AutomationProperties.SetAutomationId(_autoSumButton, \"HomeAutoSumButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_autoSumButton, \"Insert a formula using nearby numeric cells.\");");
        source.Should().Contain("_autoSumSumFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"SUM\");");
        source.Should().Contain("_autoSumAverageFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"AVERAGE\");");
        source.Should().Contain("_autoSumCountNumbersFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"COUNT\");");
        source.Should().Contain("_autoSumCountAllFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"COUNTA\");");
        source.Should().Contain("_autoSumMaxFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"MAX\");");
        source.Should().Contain("_autoSumMinFlyoutItem.Click += (_, _) => InsertAutoSumFormula(\"MIN\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.AutoSum, \"AutoSum\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_autoSumMenuItem.Menu = CreateNativeAutoSumMenu();");
        catalogSource.Should().Contain("new(NativeMenuItemId.AutoSumSum, \"Sum\", NativeMenuGesture(WorkbookShortcutRoute.AutoSum))");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> AutoSumMenuEntries");
        source.Should().Contain("=> CreateNativeMenu(NativeMenuCatalog.AutoSumMenuEntries);");
        source.Should().Contain("var formulasMenu = CreateNativeMenu(NativeMenuTopLevelId.Formulas);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.AutoSum)");
        source.Should().NotContain("_autoSumMenuItem.Header = \"AutoSum\";");
        source.Should().NotContain("_autoSumSumMenuItem.Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Alt);");
        source.Should().NotContain("formulasMenu.Items.Add(_autoSumMenuItem);");
        source.Should().Contain("_autoSumButton.IsEnabled = isIdle;");
        catalogSource.Should().Contain("new(NativeMenuItemId.AutoSum, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.AutoSumSum, context.IsIdle)");
        source.Should().Contain("_autoSumButton,");
        source.Should().Contain("private MenuFlyout CreateAutoSumFlyout()");
        source.Should().Contain("private NativeMenu CreateNativeAutoSumMenu()");
        source.Should().Contain("private void AutoSumButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void InsertAutoSumFormula(string functionName)");
        source.Should().Contain("var result = _session.InsertAutoSumFormula(functionName);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"AutoSum failed.\");");
        source.Should().Contain("RefreshShell($\"Inserted {functionName.ToUpperInvariant()} at {targetReference}\");");
        source.Should().Contain("private static bool IsAutoSumShortcut(KeyEventArgs args)");
        source.Should().Contain("args.Key == Key.OemPlus && args.KeyModifiers == KeyModifiers.Alt;");
        source.Should().Contain("InsertAutoSumFormula(\"SUM\");");
        source.Should().Contain("HasAutoSumButton: _autoSumButton.Content?.ToString() == \"AutoSum\"");
        source.Should().Contain("HasAutoSumSumMenuItem: HasToolbarMenuItem(_autoSumSumFlyoutItem, \"Sum\")");
        source.Should().Contain("HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, NativeMenuItemId.AutoSum)");
        source.Should().Contain("HasNativeAutoSumSumMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumSum)");

        smokeSource.Should().Contain("bool HasAutoSumButton,");
        smokeSource.Should().Contain("bool HasAutoSumSumMenuItem,");
        smokeSource.Should().Contain("bool HasNativeAutoSumMenuItem,");
        smokeSource.Should().Contain("bool HasNativeAutoSumSumMenuItem,");
        smokeSource.Should().Contain("HasAutoSumButton &&");
        smokeSource.Should().Contain("HasAutoSumSumMenuItem &&");
        smokeSource.Should().Contain("HasNativeAutoSumMenuItem &&");
        smokeSource.Should().Contain("HasNativeAutoSumSumMenuItem &&");
        smokeSource.Should().Contain("toolbar_autosum_button={FormatBool(snapshot.HasAutoSumButton)}");
        smokeSource.Should().Contain("toolbar_autosum_sum_menu_item={FormatBool(snapshot.HasAutoSumSumMenuItem)}");
        smokeSource.Should().Contain("native_autosum_menu_item={FormatBool(snapshot.HasNativeAutoSumMenuItem)}");
        smokeSource.Should().Contain("native_autosum_sum_menu_item={FormatBool(snapshot.HasNativeAutoSumSumMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresNativeDataSortMenuThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var quickSortSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "QuickSortRangePlanner.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        sessionSource.Should().Contain("public bool CanSortSelectedRange => SelectedRange.RowCount > 1;");
        sessionSource.Should().Contain("public WorkbookCellEditResult SortSelectedRange(bool ascending)");
        sessionSource.Should().Contain("QuickSortRangePlanner.Create(ActiveSheet, range, ActiveCell)");
        sessionSource.Should().Contain("sortPlan.SortByColOffset");
        sessionSource.Should().Contain("public WorkbookCellEditResult SortSelectedRange(IReadOnlyList<CoreSortKey> sortKeys, SortOptions options, bool hasHeaders)");
        sessionSource.Should().Contain("SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders)");
        sessionSource.Should().Contain("new SortCommand(sheetId, sheetRange, sortKeys, options)");
        sessionSource.Should().Contain("\"Select at least two rows to sort.\"");
        quickSortSource.Should().Contain("QuickAnalysisSelectionReader.HasHeaderRow(sheet, range)");

        source.Should().Contain("private readonly NativeMenuItem _sortAscendingMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _sortDescendingMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _customSortMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.SortAscending, \"Sort A to Z\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_sortAscendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: true);");
        catalogSource.Should().Contain("new(NativeMenuItemId.SortDescending, \"Sort Z to A\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_sortDescendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: false);");
        catalogSource.Should().Contain("new(NativeMenuItemId.CustomSort, \"Sort...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_customSortMenuItem.Click += async (_, _) => await ShowSortDialogAsync();");
        source.Should().Contain("var dataMenu = CreateNativeMenu(NativeMenuTopLevelId.Data);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.SortAscending)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.SortDescending)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.CustomSort)");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.Data, \"Data\")");
        source.Should().Contain("[NativeMenuTopLevelId.Data] = dataMenu,");
        source.Should().Contain("var hasNativeDataMenu = HasNativeTopLevelMenu(NativeMenuTopLevelId.Data);");
        source.Should().Contain("HasNativeDataMenu: hasNativeDataMenu");
        catalogSource.Should().Contain("new(NativeMenuItemId.SortAscending, context.IsIdle && context.CanSortSelectedRange)");
        catalogSource.Should().Contain("new(NativeMenuItemId.SortDescending, context.IsIdle && context.CanSortSelectedRange)");
        catalogSource.Should().Contain("new(NativeMenuItemId.CustomSort, context.IsIdle && context.CanSortSelectedRange)");
        source.Should().Contain("private void SortSelectedRange(bool ascending)");
        source.Should().Contain("var range = _session.SelectedRange;");
        source.Should().Contain("var result = _session.SortSelectedRange(ascending);");
        source.Should().NotContain("QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range)");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Sort failed.\");");
        source.Should().Contain("RefreshShell($\"Sorted {rangeReference} {(ascending ? \"A to Z\" : \"Z to A\")}\");");
        source.Should().Contain("private async Task ShowSortDialogAsync()");
        source.Should().Contain("var selection = await ShowSortInputDialogAsync();");
        source.Should().Contain("var keys = SortDialogPlanner.BuildSortKeys(selection.Levels);");
        source.Should().Contain("CustomSortOrder.TryParse(selection.Options.FirstKeySortOrder, out var customOrder)");
        source.Should().Contain("keys = SortDialogPlanner.ApplyCustomOrderToFirstKey(keys, customOrder);");
        source.Should().Contain("var options = new SortOptions(selection.Options.CaseSensitive, selection.Options.LeftToRight);");
        source.Should().Contain("var result = _session.SortSelectedRange(keys, options, selection.HasHeaders);");
        source.Should().Contain("private async Task<SortDialogResult?> ShowSortInputDialogAsync()");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"SortCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(headersCheck, \"SortHeadersCheckBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(levelsGrid, \"SortLevelsGrid\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(addLevelButton, \"SortAddLevelButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(deleteLevelButton, \"SortDeleteLevelButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(copyLevelButton, \"SortCopyLevelButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(moveUpButton, \"SortMoveUpButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(moveDownButton, \"SortMoveDownButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(optionsButton, \"SortOptionsButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"SortOkButton\");");
        source.Should().Contain("SortDialogPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders: true)");
        source.Should().Contain("SortDialogPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders: false)");
        source.Should().Contain("SortDialogPlanner.BuildRowChoices(range)");
        source.Should().Contain("SortDialogPlanner.BuildActiveColumnChoices(");
        source.Should().Contain("new SortOnChoice(SortDialogPlannerText.Default.SortOnCellValues)");
        source.Should().Contain("new SortOnChoice(SortDialogPlannerText.Default.SortOnCellColor)");
        source.Should().Contain("new SortOnChoice(SortDialogPlannerText.Default.SortOnFontColor)");
        source.Should().Contain("SortDialogPlanner.BuildColorChoices(_session.Workbook, _session.ActiveSheet, range, SortOn.CellColor)");
        source.Should().Contain("SortDialogPlanner.BuildColorChoices(_session.Workbook, _session.ActiveSheet, range, SortOn.FontColor)");
        source.Should().Contain("SortDialogPlanner.BuildColorChoicesForSortOn(");
        source.Should().Contain("SortDialogPlanner.SortOnFromLabel(level.SortOn) is SortOn.CellColor or SortOn.FontColor");
        source.Should().Contain("AutomationProperties.SetAutomationId(sortOnBox, $\"SortLevel{levelIndex + 1}SortOnBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(colorBox, $\"SortLevel{levelIndex + 1}ColorBox\");");
        source.Should().Contain("levels = SortDialogPlanner.AddLevel(levels).ToList();");
        source.Should().Contain("levels = SortDialogPlanner.RemoveLevel(levels, selectedLevelIndex).ToList();");
        source.Should().Contain("levels = SortDialogPlanner.CopyLevel(levels, selectedLevelIndex).ToList();");
        source.Should().Contain("levels = SortDialogPlanner.MoveLevel(levels, selectedLevelIndex, -1).ToList();");
        source.Should().Contain("levels = SortDialogPlanner.MoveLevel(levels, selectedLevelIndex, 1).ToList();");
        source.Should().Contain("levels[levelIndex].ColumnOffset = columnChoice.Value.ColumnOffset;");
        source.Should().Contain("levels[levelIndex].SortOn = sortOnChoice.Value.Label;");
        source.Should().Contain("levels[levelIndex].Ascending = directionChoice.Value.Ascending;");
        source.Should().Contain("levels[levelIndex].TargetColor = colorChoice.Value.Label;");
        source.Should().Contain("private async Task<SortDialogOptions?> ShowSortOptionsDialogAsync(SortDialogOptions current)");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"SortOptionsDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(caseSensitiveBox, \"SortOptionsCaseSensitiveCheckBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(firstKeyBox, \"SortOptionsFirstKeySortOrderBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(leftToRightButton, \"SortOptionsLeftToRightRadio\");");
        source.Should().Contain("Custom sort supports cell values, cell color, font color, custom first-key sort order, case-sensitive sorting, and left-to-right sorting through the shared SortDialogPlanner.");
        source.Should().Contain("HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, NativeMenuItemId.SortAscending)");
        source.Should().Contain("HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, NativeMenuItemId.SortDescending)");
        smokeSource.Should().Contain("HasNativeDataMenu &&");
        smokeSource.Should().Contain("HasNativeReviewMenu &&");
        smokeSource.Should().Contain("HasNativeSortAscendingMenuItem &&");
        smokeSource.Should().Contain("HasNativeSortDescendingMenuItem &&");
        smokeSource.Should().Contain("HasNativeAdvancedFilterMenuItem &&");
        smokeSource.Should().Contain("HasNativeRemoveDuplicatesMenuItem &&");
        smokeSource.Should().Contain("HasNativeDataValidationPreviewMenuItem &&");
        smokeSource.Should().Contain("HasNativeDataValidationMenuItem &&");
        smokeSource.Should().Contain("HasNativeWhatIfAnalysisMenuItem &&");
        smokeSource.Should().Contain("HasNativeGoalSeekMenuItem &&");
        smokeSource.Should().Contain("HasNativeDataTableMenuItem &&");
        smokeSource.Should().Contain("HasNativeScenarioManagerMenuItem &&");
        smokeSource.Should().Contain("HasNativeForecastSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeReviewSummaryMenuItem &&");
        smokeSource.Should().Contain("HasNativeCheckAccessibilityMenuItem &&");
        smokeSource.Should().Contain("HasNativeNextNoteMenuItem &&");
        smokeSource.Should().Contain("HasNativePreviousNoteMenuItem &&");
        smokeSource.Should().Contain("HasNativeNextCommentMenuItem &&");
        smokeSource.Should().Contain("HasNativePreviousCommentMenuItem &&");
        smokeSource.Should().Contain("native_data_menu={FormatBool(snapshot.HasNativeDataMenu)}");
        smokeSource.Should().Contain("native_review_menu={FormatBool(snapshot.HasNativeReviewMenu)}");
        smokeSource.Should().Contain("native_sort_ascending_menu_item={FormatBool(snapshot.HasNativeSortAscendingMenuItem)}");
        smokeSource.Should().Contain("native_sort_descending_menu_item={FormatBool(snapshot.HasNativeSortDescendingMenuItem)}");
        smokeSource.Should().Contain("native_advanced_filter_menu_item={FormatBool(snapshot.HasNativeAdvancedFilterMenuItem)}");
        smokeSource.Should().Contain("native_remove_duplicates_menu_item={FormatBool(snapshot.HasNativeRemoveDuplicatesMenuItem)}");
        smokeSource.Should().Contain("native_data_validation_preview_menu_item={FormatBool(snapshot.HasNativeDataValidationPreviewMenuItem)}");
        smokeSource.Should().Contain("native_data_validation_menu_item={FormatBool(snapshot.HasNativeDataValidationMenuItem)}");
        smokeSource.Should().Contain("native_what_if_analysis_menu_item={FormatBool(snapshot.HasNativeWhatIfAnalysisMenuItem)}");
        smokeSource.Should().Contain("native_goal_seek_menu_item={FormatBool(snapshot.HasNativeGoalSeekMenuItem)}");
        smokeSource.Should().Contain("native_data_table_menu_item={FormatBool(snapshot.HasNativeDataTableMenuItem)}");
        smokeSource.Should().Contain("native_scenario_manager_menu_item={FormatBool(snapshot.HasNativeScenarioManagerMenuItem)}");
        smokeSource.Should().Contain("native_forecast_sheet_menu_item={FormatBool(snapshot.HasNativeForecastSheetMenuItem)}");
        smokeSource.Should().Contain("native_review_summary_menu_item={FormatBool(snapshot.HasNativeReviewSummaryMenuItem)}");
        smokeSource.Should().Contain("native_check_accessibility_menu_item={FormatBool(snapshot.HasNativeCheckAccessibilityMenuItem)}");
        smokeSource.Should().Contain("native_next_note_menu_item={FormatBool(snapshot.HasNativeNextNoteMenuItem)}");
        smokeSource.Should().Contain("native_previous_note_menu_item={FormatBool(snapshot.HasNativePreviousNoteMenuItem)}");
        smokeSource.Should().Contain("native_next_comment_menu_item={FormatBool(snapshot.HasNativeNextCommentMenuItem)}");
        smokeSource.Should().Contain("native_previous_comment_menu_item={FormatBool(snapshot.HasNativePreviousCommentMenuItem)}");

        var homeMenuIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Home, \"Home\")", StringComparison.Ordinal);
        var insertMenuIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Insert, \"Insert\")", StringComparison.Ordinal);
        var pageLayoutMenuIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.PageLayout, \"Page Layout\")", StringComparison.Ordinal);
        var formulasMenuIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Formulas, \"Formulas\")", StringComparison.Ordinal);
        var dataMenuIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Data, \"Data\")", StringComparison.Ordinal);
        homeMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        insertMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        pageLayoutMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        formulasMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        dataMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        homeMenuIndex.Should().BeLessThan(insertMenuIndex);
        insertMenuIndex.Should().BeLessThan(pageLayoutMenuIndex);
        pageLayoutMenuIndex.Should().BeLessThan(formulasMenuIndex);
        formulasMenuIndex.Should().BeLessThan(dataMenuIndex);
    }

    [Fact]
    public void MainWindow_WiresNativeFlashFillDataMenuThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "FlashFillRangePlanner.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        sessionSource.Should().Contain("public WorkbookCellEditResult FlashFillSelectedRange()");
        sessionSource.Should().Contain("var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);");
        sessionSource.Should().Contain("FlashFillRangePlanner.HasFillTargets(sheet, plan)");
        sessionSource.Should().Contain("commands.Add(plan.CreateCommand(sheetId));");
        plannerSource.Should().Contain("public FlashFillCommand CreateCommand(SheetId sheetId)");
        plannerSource.Should().Contain("new FlashFillCommand(sheetId, FillColumn, SourceColumn, StartRow, EndRow)");

        source.Should().Contain("private readonly NativeMenuItem _flashFillMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FlashFill, \"Flash Fill\", NativeMenuGesture(WorkbookShortcutRoute.FlashFill))");
        source.Should().Contain("_flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.FlashFill)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FlashFill, context.IsIdle)");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "FlashFill", "WorkbookShortcutKey.E", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "FlashFill", "FlashFillSelectedRange();");
        source.Should().Contain("private void FlashFillSelectedRange()");
        source.Should().Contain("var result = _session.FlashFillSelectedRange();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Flash Fill failed.\");");
        source.Should().Contain("HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, NativeMenuItemId.FlashFill)");

        smokeSource.Should().Contain("bool HasNativeFlashFillMenuItem,");
        smokeSource.Should().Contain("HasNativeFlashFillMenuItem &&");
        smokeSource.Should().Contain("native_flash_fill_menu_item={FormatBool(snapshot.HasNativeFlashFillMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresNativeDataValidationMutationThroughSharedPresetPlanner()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var presetSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "DataValidationPresetPlanner.cs"));
        var displayTextSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "DataValidationDisplayTextPlanner.cs"));
        var dialogPlannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Dialogs", "DataValidationDialogPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _dataValidationMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataValidation, \"Data Validation...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_dataValidationMenuItem.Click += async (_, _) => await ShowDataValidationDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.DataValidation)");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataValidation, context.IsIdle)");
        source.Should().Contain("private async Task ShowDataValidationDialogAsync()");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var selection = await ShowDataValidationInputDialogAsync();");
        source.Should().Contain("_session.ClearSelectedRangeDataValidation()");
        source.Should().Contain("_session.ApplyDataValidationToSelectedRange(rule)");
        source.Should().Contain("RefreshShell(clearResult.Mutated");
        source.Should().Contain("Applied {DataValidationPresetPlanner.GetDisplayName(rule.Type)} data validation");
        source.Should().Contain("private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync()");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"DataValidationCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(applyButton, \"DataValidationApplyButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(clearButton, \"DataValidationClearButton\");");
        source.Should().Contain("DataValidationPresetPlanner.CreateSelectionSummary(");
        source.Should().Contain("_session.Workbook");
        source.Should().Contain("_session.ActiveSheet");
        source.Should().Contain("_session.ActiveCell");
        source.Should().Contain("_session.SelectedRange");
        source.Should().Contain("CreateDataValidationTypeChoices()");
        source.Should().Contain("DataValidationPresetPlanner.GetRuleTypeMetadata()");
        source.Should().Contain(".Where(metadata => metadata.Type is DvType.WholeNumber or DvType.Decimal or DvType.List or DvType.Date or DvType.Time or DvType.TextLength or DvType.Custom or DvType.Any)");
        source.Should().Contain("DataValidationDialogPlanner.CreateDefaultRule(initialType, _session.SelectedRange)");
        source.Should().Contain("DataValidationDialogPlanner.CreateDefaultRule(type, _session.SelectedRange)");
        source.Should().Contain("DataValidationDialogPlanner.DefaultOperatorForType(SelectedType())");
        source.Should().Contain("DataValidationDialogPlanner.CreateVisibilityPlan(");
        source.Should().Contain("DataValidationDialogPlanner.ValidateCriteria(");
        source.Should().Contain("DataValidationDialogPlanner.CreateRule(new DataValidationRuleEditorInput");
        source.Should().Contain("DataValidationDisplayTextPlanner.GetAlertStyleMetadata()");
        source.Should().NotContain("CreateDefaultDataValidationRule");
        source.Should().NotContain("GetDefaultDataValidationOperator");
        source.Should().NotContain("TryValidateDataValidationCriteria");

        sessionSource.Should().Contain("public WorkbookDataValidationMutationResult ApplyDataValidationToSelectedRange(DataValidation rule)");
        sessionSource.Should().Contain("public WorkbookDataValidationMutationResult ClearSelectedRangeDataValidation()");
        sessionSource.Should().Contain("new SetDataValidationCommand(sheetId, sheetRule)");
        sessionSource.Should().Contain("new ClearDataValidationCommand(sheetId, sheetRange)");

        presetSource.Should().Contain("public static IReadOnlyList<DataValidationRuleTypeMetadata> GetRuleTypeMetadata()");
        presetSource.Should().Contain("public static DataValidationSelectionSummary CreateSelectionSummary(");
        presetSource.Should().Contain("DataValidationDisplayTextPlanner.GetRuleTypeMetadata()");
        displayTextSource.Should().Contain("public static IReadOnlyList<DataValidationRuleTypeMetadata> GetRuleTypeMetadata()");
        displayTextSource.Should().Contain("public static IReadOnlyList<DataValidationAlertStyleMetadata> GetAlertStyleMetadata()");
        dialogPlannerSource.Should().Contain("public static DataValidation CreateDefaultRule(DvType type, GridRange selectedRange)");
        dialogPlannerSource.Should().Contain("public static DvValidationResult ValidateCriteria(");
        dialogPlannerSource.Should().Contain("public static DataValidation CreateRule(DataValidationRuleEditorInput input)");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowDataValidationDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var handlerSource = normalizedSource[handlerIndex..nextMethodIndex];

        handlerSource.Should().NotContain("SetDataValidationCommand");
        handlerSource.Should().NotContain("ClearDataValidationCommand");
        handlerSource.Should().NotContain("PasteDataValidationFromClipboardAtActiveCell");
        handlerSource.Should().NotContain("Clipboard");
        handlerSource.Should().NotContain("DataTransferManager");
        handlerSource.Should().NotContain("WindowInteropHelper");
        handlerSource.Should().NotContain("Microsoft.Win32");
        handlerSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeDataValidationPreviewWithoutWorkbookMutation()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "DataValidationPreviewPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _dataValidationPreviewMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataValidationPreview, \"Data Validation Preview...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_dataValidationPreviewMenuItem.Click += async (_, _) => await ShowDataValidationPreviewDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.DataValidationPreview)");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataValidationPreview, context.IsIdle)");
        source.Should().Contain("HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, NativeMenuItemId.DataValidationPreview)");
        source.Should().Contain("private async Task ShowDataValidationPreviewDialogAsync()");
        source.Should().Contain("DataValidationPreviewPlanner.Create(");
        source.Should().Contain("_session.Workbook");
        source.Should().Contain("_session.ActiveSheet");
        source.Should().Contain("_session.ActiveCell");
        source.Should().Contain("_session.SelectedRange");
        source.Should().Contain("await ShowTextDialogAsync(\"Data Validation Preview\", preview.Text, 520, 360);");

        plannerSource.Should().Contain("DataValidationService.GetApplicable(sheet, activeCell)");
        plannerSource.Should().Contain("DataValidationService.GetListItems(rule, sheet, activeCell, workbook)");
        plannerSource.Should().Contain("DataValidationService.FormatListSourceRange");
        plannerSource.Should().Contain("DataValidationDisplayTextPlanner.FormatAlertStyle");
        plannerSource.Should().NotContain("private static string FormatAlertStyle");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowDataValidationPreviewDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var handlerSource = normalizedSource[handlerIndex..nextMethodIndex];

        handlerSource.Should().NotContain("TryCommitPendingFormulaEdit");
        handlerSource.Should().NotContain("SetDataValidationCommand");
        handlerSource.Should().NotContain("ClearDataValidationCommand");
        handlerSource.Should().NotContain("ApplyDataValidationToSelectedRange");
        handlerSource.Should().NotContain("PasteDataValidationFromClipboardAtActiveCell");
        handlerSource.Should().NotContain("Clipboard");
        handlerSource.Should().NotContain("DataTransferManager");
        handlerSource.Should().NotContain("WindowInteropHelper");
        handlerSource.Should().NotContain("Microsoft.Win32");
        handlerSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeDataValidationDropdownThroughSharedPlannerAndKeyboardRoute()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "DataValidationDropdownPlanner.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private ComboBox? _activeDataValidationDropdown;");
        source.Should().Contain("_activeDataValidationDropdown = null;");
        source.Should().Contain("AddDataValidationDropdownOverlay(overlay, viewport, showHeadings, zoomFactor);");
        source.Should().Contain("private void AddDataValidationDropdownOverlay(");
        source.Should().Contain("DataValidationDropdownPlanner.TryPlan(");
        source.Should().Contain("new DataValidationDropdownCellBounds(left, top, width, height)");
        source.Should().Contain("private ComboBox CreateDataValidationDropdown(DataValidationDropdownPlan plan, double width, double height)");
        source.Should().Contain("ItemsSource = plan.Items");
        source.Should().Contain("SelectedItem = plan.SelectedItem");
        source.Should().Contain("MinWidth = DataValidationDropdownPlanner.MinimumWidth");
        source.Should().Contain("MinHeight = DataValidationDropdownPlanner.MinimumHeight");
        source.Should().Contain("ToolTip.SetTip(dropdown, \"Pick from list\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(dropdown, \"WorksheetDataValidationDropdown\");");
        source.Should().Contain("AutomationProperties.SetName(dropdown, \"Data validation list\");");
        source.Should().Contain("dropdown.SelectionChanged += DataValidationDropdown_SelectionChanged;");
        source.Should().Contain("private static bool IsOpenActiveDropdownShortcut(KeyEventArgs args)");
        source.Should().Contain("args.Key == Key.Down && args.KeyModifiers == KeyModifiers.Alt;");
        // Alt+Down mirrors WPF's OpenActiveDropdown fallback chain: try the data-validation dropdown
        // first, and when the active cell isn't a List-DV cell, fall through to the AutoFilter column
        // dropdown when the active cell is a filter-button cell (review P35 — this shell used to only
        // ever try the data-validation dropdown, silently doing nothing on a plain AutoFilter header).
        source.Should().Contain("e.Handled = OpenActiveDropdown();");
        source.Should().Contain("OpenActiveDataValidationDropdown() ||");
        source.Should().Contain("OpenActiveAutoFilterDropdown() ||");
        source.Should().Contain("OpenTextEntryPickListDropdown();");
        source.Should().Contain("DataValidationDropdownPlanner.GetTextEntryPickListItems(");
        source.Should().Contain("_activeDataValidationDropdown.IsDropDownOpen = true;");
        source.Should().Contain("private void DataValidationDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs e)");
        source.Should().Contain("CommitDataValidationDropdownSelection(selected);");
        source.Should().Contain("_session.CommitCellText(selected, UseR1C1ReferenceStyle)");
        source.Should().Contain("_session.CancelFormulaEdit();");
        source.Should().Contain("_formulaBoxEditOriginalText = null;");
        source.Should().Contain("RefreshShell($\"Picked {selected} for {FormatCellReference(address)}\");");

        plannerSource.Should().Contain("DataValidationService.GetApplicable(sheet, activeCell)");
        plannerSource.Should().Contain("DataValidationService.GetListItems(rule, sheet, activeCell, workbook)");
        plannerSource.Should().Contain("rule.Type == DvType.List && rule.ShowDropdown");

        var overlayIndex = normalizedSource.IndexOf("private void AddDataValidationDropdownOverlay(", StringComparison.Ordinal);
        overlayIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextOverlayMethodIndex = normalizedSource.IndexOf("\n    private Control CreateSelectableDrawingObjectVisual(", overlayIndex, StringComparison.Ordinal);
        nextOverlayMethodIndex.Should().BeGreaterThan(overlayIndex);
        var overlaySource = normalizedSource[overlayIndex..nextOverlayMethodIndex];

        var keyboardIndex = normalizedSource.IndexOf("private bool OpenActiveDataValidationDropdown()", StringComparison.Ordinal);
        keyboardIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextKeyboardMethodIndex = normalizedSource.IndexOf("\n    private void CycleShellFocus(", keyboardIndex, StringComparison.Ordinal);
        nextKeyboardMethodIndex.Should().BeGreaterThan(keyboardIndex);
        var keyboardRouteSource = normalizedSource[keyboardIndex..nextKeyboardMethodIndex];

        overlaySource.Should().NotContain("System.Windows");
        overlaySource.Should().NotContain("WindowInteropHelper");
        overlaySource.Should().NotContain("Microsoft.Win32");
        keyboardRouteSource.Should().NotContain("System.Windows");
        keyboardRouteSource.Should().NotContain("WindowInteropHelper");
        keyboardRouteSource.Should().NotContain("Microsoft.Win32");
        keyboardRouteSource.Should().NotContain("DataTransferManager");
    }

    /// <summary>
    /// Source-contract test for three DV input-message tooltip parity bugs (BM1/BM2/BM3):
    ///
    /// BM1 — The input-message tooltip must render for ANY DV type (Decimal, WholeNumber, etc.),
    ///   not only for List rules.  In the fixed code, AddDvInputMessageOverlay is called BEFORE
    ///   DataValidationDropdownPlanner.TryPlan (which only succeeds for List+ShowDropdown), so the
    ///   tooltip is produced regardless of DV type.
    ///
    /// BM2 — The tooltip must remain visible while the user is editing (FormulaEditAddress is set),
    ///   matching WPF's RefreshDvInputMessage (no edit guard) and Excel.  In the fixed code, the
    ///   FormulaEditAddress guard comes AFTER AddDvInputMessageOverlay, so it gates only the arrow
    ///   button, not the tooltip.
    ///
    /// BM3 — The tooltip clamp must use the visible scroll-viewport dimensions, not the full grid
    ///   canvas extent, so the tooltip flips correctly near the viewport edge (matching WPF's
    ///   CommentOverlay.ActualWidth/Height clamp).  The fixed code reads _sheetScrollViewer.Bounds.
    /// </summary>
    [Fact]
    public void MainWindow_DvInputMessageOverlay_IsTypeAgnosticAndVisibleDuringEditAndClampsToViewport()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        // ── Shared planner wiring ──────────────────────────────────────────
        // GetInputMessagePrompt is DV-type-agnostic (delegates to DataValidationService.GetInputPrompt
        // which only checks ShowInputMessage), so the tooltip works for any DV rule.
        source.Should().Contain("private void AddDvInputMessageOverlay(");
        source.Should().Contain("DataValidationAffordancePlanner.GetInputMessagePrompt(");
        source.Should().Contain("AutomationProperties.SetAutomationId(border, \"WorksheetDvInputMessagePopup\");");
        source.Should().Contain("AutomationProperties.SetName(border, \"Data validation input message\");");

        // ── BM1 + BM2: order within AddDataValidationDropdownOverlay ──────
        // The contract is expressed as a relative ordering of key tokens inside the method body:
        //   1. AddDvInputMessageOverlay call
        //   2. FormulaEditAddress guard   (gates only the arrow button, not the tooltip)
        //   3. DataValidationDropdownPlanner.TryPlan  (gates only the arrow button)
        var overlayMethodStart = normalizedSource.IndexOf(
            "\n    private void AddDataValidationDropdownOverlay(", StringComparison.Ordinal);
        overlayMethodStart.Should().BeGreaterThanOrEqualTo(0,
            "AddDataValidationDropdownOverlay must exist in MainWindow.cs");

        // Find the boundary of the method body (the next private/protected/public method).
        var overlayMethodEnd = normalizedSource.IndexOf(
            "\n    private void AddDvInputMessageOverlay(", overlayMethodStart + 1, StringComparison.Ordinal);
        overlayMethodEnd.Should().BeGreaterThan(overlayMethodStart,
            "AddDvInputMessageOverlay must immediately follow AddDataValidationDropdownOverlay");

        var overlayMethodBody = normalizedSource[overlayMethodStart..overlayMethodEnd];

        var inputMsgCallIdx = overlayMethodBody.IndexOf(
            "AddDvInputMessageOverlay(", StringComparison.Ordinal);
        inputMsgCallIdx.Should().BeGreaterThanOrEqualTo(0,
            "AddDvInputMessageOverlay must be called inside AddDataValidationDropdownOverlay");

        var formulaGuardIdx = overlayMethodBody.IndexOf(
            "FormulaEditAddress is not null", StringComparison.Ordinal);
        formulaGuardIdx.Should().BeGreaterThanOrEqualTo(0,
            "FormulaEditAddress guard must still exist (for the arrow button)");

        var tryPlanIdx = overlayMethodBody.IndexOf(
            "DataValidationDropdownPlanner.TryPlan(", StringComparison.Ordinal);
        tryPlanIdx.Should().BeGreaterThanOrEqualTo(0,
            "DataValidationDropdownPlanner.TryPlan must still exist (gates the arrow button)");

        // BM1: tooltip call precedes TryPlan → tooltip renders for non-list DV cells
        inputMsgCallIdx.Should().BeLessThan(tryPlanIdx,
            "AddDvInputMessageOverlay must be called BEFORE DataValidationDropdownPlanner.TryPlan " +
            "so non-list DV cells (Decimal, WholeNumber, etc.) still get their input-message tooltip (BM1)");

        // BM2: tooltip call precedes FormulaEditAddress guard → tooltip stays visible during edit
        inputMsgCallIdx.Should().BeLessThan(formulaGuardIdx,
            "AddDvInputMessageOverlay must be called BEFORE the FormulaEditAddress guard " +
            "so the tooltip remains visible while the user is editing a cell (BM2)");

        // ── BM3: viewport clamp uses scroll-viewer bounds, not grid canvas extent ──
        // The overlay method body must read _sheetScrollViewer.Bounds before calling AddDvInputMessageOverlay.
        var viewportBoundsIdx = overlayMethodBody.IndexOf(
            "_sheetScrollViewer.Bounds", StringComparison.Ordinal);
        viewportBoundsIdx.Should().BeGreaterThanOrEqualTo(0,
            "_sheetScrollViewer.Bounds must be read in AddDataValidationDropdownOverlay " +
            "so the viewport clamp uses the visible area, not the full grid canvas (BM3)");

        viewportBoundsIdx.Should().BeLessThan(inputMsgCallIdx,
            "_sheetScrollViewer.Bounds must be read BEFORE the AddDvInputMessageOverlay call " +
            "so the viewport dimensions are passed for clamping (BM3)");

        // The old bug: clamping against overlay.Width / overlay.Height (full grid canvas) is gone.
        // The overlay dimensions are still used as fallback when the scroll-viewer has not yet laid out.
        overlayMethodBody.Should().NotContain(
            "AddDvInputMessageOverlay(overlay, left, top + height, overlay.Width, overlay.Height)",
            "the old unclamped call (using full grid canvas dimensions) must no longer be present");
    }

    [Fact]
    public void MainWindow_WiresNativeGoalSeekThroughSharedParserSessionAndStatusDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var parserSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "GoalSeekRequestParser.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _whatIfAnalysisMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _goalSeekMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoalSeek, \"Goal Seek...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_goalSeekMenuItem.Click += async (_, _) => await ShowGoalSeekDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.WhatIfAnalysis, \"What-If Analysis\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_whatIfAnalysisMenuItem.Menu = CreateNativeWhatIfAnalysisMenu();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.WhatIfAnalysis)");
        catalogSource.Should().Contain("new(NativeMenuItemId.WhatIfAnalysis, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoalSeek, context.IsIdle)");

        source.Should().Contain("private async Task ShowGoalSeekDialogAsync()");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var request = await ShowGoalSeekInputDialogAsync();");
        source.Should().Contain("var result = _session.ExecuteGoalSeek(request);");
        source.Should().Contain("var choice = await ShowGoalSeekStatusDialogAsync(result);");
        source.Should().Contain("WorkbookGoalSeekStatus.Applied");
        source.Should().Contain("choice == GoalSeekStatusDialogChoice.RestoreOriginalValues");
        source.Should().Contain("var restoreResult = _session.UndoLastEdit();");
        source.Should().Contain("RefreshShell(FormatGoalSeekStatus(result));");
        source.Should().Contain("ShowEditIssue(FormatGoalSeekStatus(result));");

        source.Should().Contain("private async Task<GoalSeekRequest?> ShowGoalSeekInputDialogAsync(");
        source.Should().Contain("GoalSeekRequestParser.Parse(");
        source.Should().Contain("_session.ActiveSheet.Id");
        source.Should().Contain("setCellBox.Text");
        source.Should().Contain("targetValueBox.Text");
        source.Should().Contain("changingCellBox.Text");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"GoalSeekCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(setCellBox, \"GoalSeekSetCellBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(targetValueBox, \"GoalSeekTargetValueBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(changingCellBox, \"GoalSeekChangingCellBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, automationId);");
        source.Should().Contain("CreateGoalSeekPickerButton(setCellBox, \"GoalSeekSetCellPickerButton\")");
        source.Should().Contain("CreateGoalSeekPickerButton(changingCellBox, \"GoalSeekChangingCellPickerButton\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"GoalSeekErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"GoalSeekOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"GoalSeekCancelButton\");");
        source.Should().Contain("FocusGoalSeekErrorField(parseResult.Error, setCellBox, targetValueBox, changingCellBox);");

        source.Should().Contain("private async Task<GoalSeekStatusDialogChoice> ShowGoalSeekStatusDialogAsync(WorkbookGoalSeekResult result)");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"GoalSeekStatusDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(summaryBlock, \"GoalSeekStatusText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(restoreButton, \"GoalSeekRestoreOriginalValuesButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepButton, \"GoalSeekKeepResultButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"GoalSeekStatusOkButton\");");
        source.Should().Contain("private static string FormatGoalSeekParseError(GoalSeekRequestParseResult result)");
        source.Should().Contain("private static string FormatGoalSeekStatus(WorkbookGoalSeekResult result)");

        sessionSource.Should().Contain("public WorkbookGoalSeekResult ExecuteGoalSeek(GoalSeekRequest request)");
        parserSource.Should().Contain("public static GoalSeekRequestParseResult Parse(");
        parityCaptureSource.Should().Contain("(\"dialog.GoalSeek\", () => ShowGoalSeekParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.GoalSeekStatus\", () => ShowGoalSeekStatusParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private async Task ShowGoalSeekParityDialogAsync()");
        parityCaptureSource.Should().Contain("private Task ShowGoalSeekStatusParityDialogAsync()");
        parityCaptureSource.Should().Contain("initialSetCellText: \"C2\"");
        parityCaptureSource.Should().Contain("initialTargetValueText: \"5000\"");
        parityCaptureSource.Should().Contain("initialChangingCellText: \"E2\"");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowGoalSeekDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowDataTableDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("GoalSeekService.Seek");
        routeSource.Should().NotContain("new GoalSeekCommand");
        routeSource.Should().NotContain("SetCell(");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_RegistersExistingShellDialogsForParityCapture()
    {
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var hyperlinkSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var drawingFormatSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));
        var pictureShapeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs"));
        var selectionPaneSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs"));
        var evaluateFormulaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.EvaluateFormula.cs"));
        var errorCheckingSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ErrorChecking.cs"));
        var ribbonMenuDialogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuDialogs.cs"));
        var insertObjectsSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs"));
        var recommendedPivotTablesSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.RecommendedPivotTables.cs"));

        parityCaptureSource.Should().Contain("(\"dialog.TextToColumns\", () => ShowTextToColumnsParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.CreateTable\", () => ShowCreateTableParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.RecommendedPivotTables\", async () => { await ShowRecommendedPivotTablesDialogAsync(); }),");
        parityCaptureSource.Should().Contain("(\"dialog.Consolidate\", () =>");
        parityCaptureSource.Should().Contain("PrepareConsolidateParityCaptureState();");
        parityCaptureSource.Should().Contain("ShowConsolidateDialogAsync(ConsolidateParityFixture.CreateDialogInitialState())");
        parityCaptureSource.Should().Contain("(\"dialog.Sparkline\", () => ShowSparklineParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.InsertHyperlink\", () => ShowInsertHyperlinkParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.SymbolPicker\", () => ShowSymbolPickerAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.EvaluateFormula\", () => ShowEvaluateFormulaParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ErrorChecking\", () => ShowErrorCheckingParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.WatchWindow\", () => ShowWatchWindowParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.AddWatch\", () => ShowAddWatchParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.WorkbookStatistics\", () => ShowWorkbookStatisticsDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.RenameSheet\", () => ShowRenameSheetParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.UnhideSheet\", () => ShowUnhideSheetParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.About\", () => ShowAboutDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.LegalNotices\", () => ShowLegalNoticesDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.SelectDataSource\", () => ShowSelectDataSourceParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ChangeChartType\", () => ShowChangeChartTypeParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.FormatChartArea\", () => ShowFormatChartAreaParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ShapeEffects\", () => ShowShapeEffectsParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ShapeGradient\", () => ShowShapeGradientParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.Zoom\", () => ShowZoomDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.CustomViews\", () => ShowCustomViewsParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.PrintPreview\", () => ShowPrintPreviewParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.OpenWorkbook\", () => ShowWorkbookFileDialogParitySurfaceAsync(CreateOpenWorkbookDialogSurfacePlan())),");
        parityCaptureSource.Should().Contain("(\"dialog.SaveAsWorkbook\", () => ShowWorkbookFileDialogParitySurfaceAsync(CreateSaveAsWorkbookDialogSurfacePlan())),");
        parityCaptureSource.Should().Contain("(\"dialog.ExportOptions\", () => ShowExportOptionsParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.SelectionPane\", () => ShowSelectionPaneParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.InsertSlicer\", () => ShowInsertSlicerParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.InsertTimeline\", () => ShowInsertTimelineParityDialogAsync()),");
        // The multi-tab dialogs are registered through the per-tab capture table (ParityTabDialogOpeners),
        // which emits one PNG per tab/category in addition to the default surface.
        parityCaptureSource.Should().Contain("private IReadOnlyList<(string SurfaceId, Func<Task> Opener, string[] TabNames)> ParityTabDialogOpeners() =>");
        parityCaptureSource.Should().Contain("results.AddRange(await CaptureModalTabsAsync(");
        parityCaptureSource.Should().Contain("render: !interactionOnly));");
        parityCaptureSource.Should().Contain("private async Task<IReadOnlyList<ParitySurfaceResult>> CaptureModalTabsAsync(");
        parityCaptureSource.Should().Contain("(\"dialog.FormatCells\", () => ShowFormatCellsDialogAsync(),");
        parityCaptureSource.Should().Contain("[\"Number\", \"Alignment\", \"Font\", \"Border\", \"Fill\", \"Protection\"]),");
        parityCaptureSource.Should().Contain("(\"dialog.FindReplace\", () => ShowFindDialogAsync(),");
        // PageSetup is registered per-tab (both shells have Page/Margins/Header-Footer/Sheet in order).
        parityCaptureSource.Should().Contain("(\"dialog.PageSetup\", () => ShowPageSetupDialogAsync(),");
        parityCaptureSource.Should().Contain("[\"Page\", \"Margins\", \"HeaderFooter\", \"Sheet\"]),");
        parityCaptureSource.Should().Contain("(\"dialog.PivotTableOptions\", () => ShowPivotTableOptionsParityDialogAsync(),");
        parityCaptureSource.Should().Contain("(\"dialog.PivotFieldFilter\", () => ShowPivotFieldFilterParityDialogAsync(),");
        parityCaptureSource.Should().Contain("(\"dialog.PivotValueFieldSettings\", () => ShowPivotValueFieldSettingsParityDialogAsync(),");
        parityCaptureSource.Should().Contain("(\"dialog.Options\", () => ShowOptionsDialogAsync(),");
        // Conditional-format manage uses the same deterministic fixture as the WPF capture host.
        parityCaptureSource.Should().Contain("(\"dialog.ConditionalFormatManage\", () => ShowManageConditionalFormatsParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private async Task ShowManageConditionalFormatsParityDialogAsync()");
        parityCaptureSource.Should().Contain("ConditionalFormatManageParityFixture.CreateRange(sheet.Id)");
        parityCaptureSource.Should().Contain("ConditionalFormatManageParityFixture.CreateRules(sheet.Id)");
        parityCaptureSource.Should().Contain("(\"dialog.AllowEditRanges\", () => ShowAllowEditRangesParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ProtectSheet\", () => ShowProtectSheetDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.ProtectWorkbook\", () => ShowProtectWorkbookParityDialogAsync()),");
        parityCaptureSource.Should().Contain("(\"dialog.AccessibilityChecker\", () => ShowAccessibilityCheckerParityDialogAsync()),");

        parityCaptureSource.Should().Contain("private Task ShowInsertHyperlinkParityDialogAsync()");
        parityCaptureSource.Should().Contain("HyperlinkDialogParityFixture.Seed(_session.ActiveSheet, address);");
        parityCaptureSource.Should().Contain("private async Task ShowInsertHyperlinkParityDialogCoreAsync()");
        hyperlinkSource.Should().Contain("HyperlinkDialogPlanner.LinkTypeColumnWidth");
        hyperlinkSource.Should().Contain("HyperlinkDialogPlanner.LabelColumnWidth");
        hyperlinkSource.Should().Contain("HyperlinkDialogPlanner.ActionButtonWidth");
        hyperlinkSource.Should().Contain("new(UiText.Get(\"Hyperlink_LinkTypeCreateNewDocument\"), HyperlinkTargetKind.CreateNewDocument)");
        hyperlinkSource.Should().Contain("new(UiText.Get(\"Hyperlink_LinkTypePlaceInThisDocument\"), HyperlinkTargetKind.PlaceInThisDocument)");
        parityCaptureSource.Should().Contain("private Task ShowEvaluateFormulaParityDialogAsync()");
        parityCaptureSource.Should().Contain("ShowEvaluateFormulaDialogAsync");
        parityCaptureSource.Should().Contain("EvaluateFormulaDialogPlanner.CreateParitySummary(_session.ActiveSheet.Id)");
        evaluateFormulaSource.Should().Contain("EvaluateFormulaDialogPlanner.CreateSummary(_session.Workbook, _session.ActiveCell)");
        evaluateFormulaSource.Should().Contain("EvaluateFormulaDialogPlanner.CreateSession(summary)");
        evaluateFormulaSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"EvaluateFormulaDialog\")");
        evaluateFormulaSource.Should().Contain("UiText.Get(EvaluateFormulaDialogPlanner.TitleKey)");
        evaluateFormulaSource.Should().Contain("EvaluateFormulaDialogPlanner.StepPositionTextKey");
        evaluateFormulaSource.Should().Contain("Width = EvaluateFormulaDialogPlanner.Width");
        evaluateFormulaSource.Should().Contain("Height = EvaluateFormulaDialogPlanner.Height");
        evaluateFormulaSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, EvaluateFormulaDialogChromeStyle)");
        evaluateFormulaSource.Should().Contain("formulaText.Inlines!.Clear()");
        evaluateFormulaSource.Should().NotContain("\"Evaluate Formula\"");
        errorCheckingSource.Should().Contain("private async Task CheckFormulaErrorsAsync()");
        errorCheckingSource.Should().Contain("FormulaAuditingService.FindFormulaErrorIssues(_session.Workbook, _session.ActiveSheet.Id, _session.CyclicCells)");
        errorCheckingSource.Should().Contain("SetFormulaErrorIgnoredCommand(issue.SheetId, issue.Address, ignored: true)");
        errorCheckingSource.Should().Contain("FormulaEvaluationSummaryService.GetSummary(_session.Workbook, issue.Address)");
        errorCheckingSource.Should().Contain("TraceFormulaPrecedents();");
        errorCheckingSource.Should().Contain("ErrorCheckingDialogPlanner.CreateCommandState");
        errorCheckingSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, ErrorCheckingDialogPlanner.DialogAutomationId)");
        errorCheckingSource.Should().Contain("private Task ShowErrorCheckingParityDialogAsync()");
        parityCaptureSource.Should().Contain("private Task ShowWatchWindowParityDialogAsync()");
        parityCaptureSource.Should().Contain("ShowWatchWindowDialogAsync");
        parityCaptureSource.Should().Contain("private Task ShowAddWatchParityDialogAsync()");
        parityCaptureSource.Should().Contain("ShowAddWatchDialogAsync(AddWatchDialogPlanner.ParitySelectedRangeText)");
        ribbonMenuDialogSource.Should().Contain("await ShowAddWatchDialogAsync(FormatRangeReference(_session.SelectedRange), dialog)");
        ribbonMenuDialogSource.Should().Contain("WatchWindowService.AddWatches(_session.Workbook, _session.SelectedRange)");
        ribbonMenuDialogSource.Should().Contain("Width = WatchWindowDialogPlanner.Width");
        ribbonMenuDialogSource.Should().Contain("Height = WatchWindowDialogPlanner.Height");
        ribbonMenuDialogSource.Should().Contain("MinWidth = WatchWindowDialogPlanner.MinWidth");
        ribbonMenuDialogSource.Should().Contain("MinHeight = WatchWindowDialogPlanner.MinHeight");
        ribbonMenuDialogSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, WatchWindowDialogPlanner.DialogAutomationId)");
        ribbonMenuDialogSource.Should().Contain("(\"WatchWindow_Book\", WatchWindowDialogPlanner.BookColumnWidth)");
        ribbonMenuDialogSource.Should().Contain("(\"WatchWindow_Formula\", WatchWindowDialogPlanner.FormulaColumnWidth)");
        ribbonMenuDialogSource.Should().Contain("Title = UiText.Get(AddWatchDialogPlanner.TitleKey)");
        ribbonMenuDialogSource.Should().Contain("Width = AddWatchDialogPlanner.Width");
        ribbonMenuDialogSource.Should().Contain("Height = AddWatchDialogPlanner.Height");
        ribbonMenuDialogSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, AddWatchDialogPlanner.DialogAutomationId)");
        ribbonMenuDialogSource.Should().Contain("AutomationProperties.SetAutomationId(rangeBox, AddWatchDialogPlanner.SelectedRangeAutomationId)");
        parityCaptureSource.Should().Contain("private Task ShowCreateTableParityDialogAsync()");
        parityCaptureSource.Should().Contain("ShowCreateTableDialogAsync(\"Sheet1!$A$1:$D$5\", \"TableStyleMedium2\")");
        insertObjectsSource.Should().Contain("private async Task InsertTableFromSelectionAsync()");
        insertObjectsSource.Should().Contain("TableCreationPlanner.PlanSourceRange(_session.ActiveSheet, _session.SelectedRange)");
        insertObjectsSource.Should().Contain("var defaultStyle = TableStyleGalleryPlanner.GetOption(0, _session.Workbook.Theme)");
        insertObjectsSource.Should().Contain("await ShowCreateTableDialogAsync(defaultRangeText, defaultStyle.StyleName)");
        insertObjectsSource.Should().Contain("TableCreationPlanner.BuildStyledCommand(");
        insertObjectsSource.Should().Contain("CreateTableDialogPlanner.TryParse(");
        insertObjectsSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, CreateTableDialogPlanner.DialogAutomationId)");
        insertObjectsSource.Should().Contain("AutomationProperties.SetAutomationId(rangeBox, CreateTableDialogPlanner.RangeBoxAutomationId)");
        insertObjectsSource.Should().Contain("AutomationProperties.SetAutomationId(headersBox, CreateTableDialogPlanner.HeadersBoxAutomationId)");
        recommendedPivotTablesSource.Should().Contain("UiText.Get(RecommendedPivotTablesDialogPlanner.TitleKey)");
        recommendedPivotTablesSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, RecommendedPivotTablesDialogPlanner.DialogAutomationId)");
        recommendedPivotTablesSource.Should().Contain("AutomationProperties.SetAutomationId(blankPivotTable, RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationId)");
        recommendedPivotTablesSource.Should().Contain("RecommendedPivotTablesDialogResult.BlankPivotTable");
        parityCaptureSource.Should().Contain("private async Task ShowRenameSheetParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowRenameSheetDialogAsync(_session.ActiveSheet.Name);");
        parityCaptureSource.Should().Contain("private async Task ShowUnhideSheetParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowUnhideSheetDialogAsync([new WorkbookHiddenSheet(_session.ActiveSheet.Id, \"Archive\")]);");
        parityCaptureSource.Should().Contain("private async Task ShowSelectDataSourceParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowSelectDataDialogAsync(\"A1:C6\", firstColumnIsCategories: true);");
        parityCaptureSource.Should().Contain("private async Task ShowChangeChartTypeParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowWithSelectedParityChartAsync(ShowChangeChartTypeDialog);");
        parityCaptureSource.Should().Contain("private async Task ShowFormatChartAreaParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowWithSelectedParityChartAsync(ShowFormatChartAreaDialog);");
        parityCaptureSource.Should().Contain("private async Task ShowShapeEffectsParityDialogAsync()");
        parityCaptureSource.Should().Contain("DrawingShapeEffectPreset.Shadow");
        parityCaptureSource.Should().Contain("await OpenShapeEffectsDialogAsync();");
        parityCaptureSource.Should().Contain("private async Task ShowShapeGradientParityDialogAsync()");
        parityCaptureSource.Should().Contain("await ShowWithSelectedParityShapeAsync(OpenShapeGradientDialogAsync);");
        parityCaptureSource.Should().Contain("private Task ShowTextToColumnsParityDialogAsync()");
        parityCaptureSource.Should().Contain("private Task ShowSparklineParityDialogAsync()");
        parityCaptureSource.Should().Contain("initialDataRangeText: \"Sheet1!$D$2:$D$5\"");
        parityCaptureSource.Should().Contain("initialLocationText: \"Sheet1!$H$2:$H$5\"");
        parityCaptureSource.Should().Contain("private async Task ShowSelectionPaneParityDialogAsync()");
        parityCaptureSource.Should().Contain("chart.Name = SelectionPaneParityFixture.ChartName;");
        parityCaptureSource.Should().Contain("shape.Name = SelectionPaneParityFixture.ShapeName;");
        parityCaptureSource.Should().Contain("SelectionPaneParityFixture.CreateDialogItems(");
        parityCaptureSource.Should().Contain("await OpenSelectionPaneDialogAsync(items);");
        parityCaptureSource.Should().Contain("await OpenSelectionPaneDialogAsync();");
        parityCaptureSource.Should().Contain("private async Task ShowWithParityPivotAsync(Func<Task> showDialogAsync)");
        parityCaptureSource.Should().Contain("private async Task ShowPivotTableOptionsParityDialogAsync()");
        parityCaptureSource.Should().Contain("await OpenPivotTableOptionsDialogAsync(pivot);");
        parityCaptureSource.Should().Contain("private async Task ShowPivotFieldFilterParityDialogAsync()");
        parityCaptureSource.Should().Contain("await OpenPivotItemFilterDialogAsync(pivot, headers, target, exposeActiveFilterActions: false);");
        parityCaptureSource.Should().Contain("private async Task ShowPivotValueFieldSettingsParityDialogAsync()");
        parityCaptureSource.Should().Contain("await OpenPivotValueFieldSettingsDialogAsync(pivot, headers, target);");
        parityCaptureSource.Should().Contain("private async Task ShowInsertSlicerParityDialogAsync()");
        parityCaptureSource.Should().Contain("private async Task ShowInsertTimelineParityDialogAsync()");
        parityCaptureSource.Should().Contain("private async Task ShowPivotControlPickerParityDialogAsync(");
        parityCaptureSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, automationId);");
        parityCaptureSource.Should().Contain("private ChartModel? EnsureParityChart()");
        parityCaptureSource.Should().Contain("private DrawingShapeModel? EnsureParityShape()");
        parityCaptureSource.Should().Contain("private PivotTableModel? EnsureParityPivot()");
        parityCaptureSource.Should().Contain("new PivotCacheModel");
        parityCaptureSource.Should().Contain("new PivotDataFieldModel(4, \"Sum of Revenue\", \"sum\")");
        parityCaptureSource.Should().Contain("private async Task ShowCustomViewsParityDialogAsync()");
        parityCaptureSource.Should().Contain("_session.Workbook.CustomViews.Clear();");
        parityCaptureSource.Should().Contain("private async Task ShowAllowEditRangesParityDialogAsync()");
        parityCaptureSource.Should().Contain("_session.ExecuteReviewCommand(new AllowEditRangeCommand(sheetId, existingRange));");

        pictureShapeSource.Should().Contain("DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs()");
        pictureShapeSource.Should().Contain("DrawingObjectContextualCommandAction.ShapeEffectsDialog => () => RunGuarded(OpenShapeEffectsDialogAsync)");
        drawingFormatSource.Should().Contain("private async System.Threading.Tasks.Task OpenShapeEffectsDialogAsync()");
        drawingFormatSource.Should().Contain("ShapeEffectsPlanner.CreatePlan(shape.GetEffectiveEffectPreset())");
        drawingFormatSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ShapeEffectsDialog\");");
        drawingFormatSource.Should().Contain("AutomationProperties.SetAutomationId(effectBox, \"ShapeEffectsPresetBox\");");
        drawingFormatSource.Should().Contain("AutomationProperties.SetAutomationId(descriptionText, \"ShapeEffectsDescriptionText\");");
        drawingFormatSource.Should().Contain("UiText.Get(\"ShapeEffects_EffectLabel\")");
        drawingFormatSource.Should().Contain("new SetDrawingShapeEffectCommand(_session.ActiveSheet.Id, current.Id, normalized)");
        drawingFormatSource.Should().Contain("UiText.Get(\"ShapeGradient_GradientStopsGroup\")");
        drawingFormatSource.Should().Contain("UiText.Get(\"ShapeGradient_Stop1ColorLabel\")");
        drawingFormatSource.Should().Contain("UiText.Get(\"ShapeGradient_Stop2ColorLabel\")");
        drawingFormatSource.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"136,40,*,54\")");
        drawingFormatSource.Should().Contain("Grid.SetColumn(directionBox, 1);");
        drawingFormatSource.Should().Contain("Grid.SetColumnSpan(directionBox, 3);");
        drawingFormatSource.Should().Contain("var ok = CreateGradientDialogButton(UiText.Get(\"Common_Ok\"), isDefault: true);");
        drawingFormatSource.Should().Contain("private static Button CreateGradientDialogButton(string text, bool isDefault)");

        selectionPaneSource.Should().Contain("Width = 520");
        selectionPaneSource.Should().Contain("Height = 440");
        selectionPaneSource.Should().Contain("var searchBox = new TextBox { MinWidth = 160");
        selectionPaneSource.Should().Contain("AutomationProperties.SetAutomationId(searchBox, \"SelectionPaneSearchBox\")");
        selectionPaneSource.Should().Contain("AutomationProperties.SetAutomationId(filterBox, \"SelectionPaneFilterBox\")");
        selectionPaneSource.Should().Contain("AutomationProperties.SetAutomationId(renameBox, \"SelectionPaneRenameBox\")");
        selectionPaneSource.Should().Contain("AutomationProperties.SetAutomationId(toggleVisibilityButton, \"SelectionPaneToggleVisibilityButton\")");
        selectionPaneSource.Should().Contain("ApplySelectionPaneListStyle(listBox)");
        selectionPaneSource.Should().Contain("x.OfType<ListBoxItem>().Class(\":selected\")");
        // R125: the Selection Pane gained a Delete button that routes to the SAME
        // DeleteDrawingObjectCommand the sheet-grid Delete key uses (r121), rather than a second
        // deletion path. Pin the button's presence in the row, its AutomationId, and the shared
        // chrome call -- pinning only the row composition would let a future change drop the
        // automation id or the chrome and still pass.
        selectionPaneSource.Should().Contain("Children = { showAllButton, hideAllButton, moveUpButton, moveDownButton, deleteButton }");
        selectionPaneSource.Should().Contain("AutomationProperties.SetAutomationId(deleteButton, \"SelectionPaneDeleteButton\")");
        selectionPaneSource.Should().Contain("ApplySelectionPaneButtonChrome(deleteButton, 82)");
        selectionPaneSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);");
        selectionPaneSource.Should().Contain("CreateSelectionPaneEyeIcon()");
        selectionPaneSource.Should().NotContain("SelectionPane_Hint");
    }

    [Fact]
    public void AvaloniaPageSetupDialog_UsesSharedPlannerForChoiceSurface()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs"));

        source.Should().Contain("Title = UiText.Get(PageSetupDialogPlanner.TitleResourceKey)");
        source.Should().Contain("Width = PageSetupDialogPlanner.WindowWidth");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, PageSetupDialogPlanner.DialogAutomationId)");
        source.Should().Contain("PageSetupDialogPlanner.PlanSurface(sheet)");
        source.Should().Contain("private async Task<(PageSetupDialogFields Fields, PageSetupDialogAction RequestedAction)?> ShowPageSetupDialogCoreAsync(");
        source.Should().Contain("dialogResult.Value.Fields,");
        source.Should().Contain("dialogResult.Value.RequestedAction);");
        source.Should().Contain("PageSetupDialogSurfacePlan surface,");
        source.Should().Contain("var orientationChoices = PageSetupDialogPlanner.OrientationChoices");
        source.Should().Contain("ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(orientationChoices, UiText.Get)");
        source.Should().Contain("SelectedIndex = surface.ChoiceIndexes.Orientation");
        source.Should().Contain("SelectedIndex = surface.ChoiceIndexes.PaperSize");
        source.Should().Contain("PageSetupDialogPlanner.BuildFields(initial, new PageSetupDialogSurfaceInput");
        source.Should().Contain("var paperSizeChoices = PageSetupDialogPlanner.PaperSizeChoices");
        source.Should().Contain("var pageOrderChoices = PageSetupDialogPlanner.PageOrderChoices");
        source.Should().Contain("var printErrorValueChoices = PageSetupDialogPlanner.PrintErrorValueChoices");
        source.Should().Contain("var printCommentChoices = PageSetupDialogPlanner.PrintCommentChoices");
        source.Should().Contain("AutomationProperties.SetAutomationId(tabs, PageSetupDialogPlanner.TabsAutomationId)");
        source.Should().NotContain("Orientation = orientationChoices.ValueAt(orientationBox.SelectedIndex)");
        source.Should().NotContain("static IReadOnlyList<string> ChoiceLabels");
        source.Should().NotContain("PageSetupDialogModel.ChoiceIndex(");
        source.Should().NotContain("PageSetupDialogModel.ChoiceValue(");
    }

    [Fact]
    public void WpfParityCapture_RegistersSameDialogSurfaceIdsAsAvalonia()
    {
        var avaloniaCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var wpfCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "ParityCapture.cs"));
        var autoFilterFixtureSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Presentation", "Filtering", "AutoFilterParityFixturePlanner.cs"));
        var accessibilityCheckerFixtureSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Presentation", "Accessibility", "AccessibilityCheckerParityFixture.cs"));
        var dialogIds = new[]
        {
            "dialog.FormatCells",
            "dialog.FindReplace",
            "dialog.GoTo",
            "dialog.GoToSpecial",
            "dialog.CreateTable",
            "dialog.RecommendedPivotTables",
            "dialog.Sort",
            "dialog.SortOptions",
            "dialog.AutoFilter",
            "dialog.TextToColumns",
            "dialog.AdvancedFilter",
            "dialog.Consolidate",
            "dialog.RemoveDuplicates",
            "dialog.GoalSeek",
            "dialog.GoalSeekStatus",
            "dialog.DataTable",
            "dialog.ScenarioManager",
            "dialog.ForecastSheet",
            "dialog.Subtotal",
            "dialog.Sparkline",
            "dialog.InsertHyperlink",
            "dialog.SymbolPicker",
            "dialog.EvaluateFormula",
            "dialog.ErrorChecking",
            "dialog.WatchWindow",
            "dialog.AddWatch",
            "dialog.WorkbookStatistics",
            "dialog.RenameSheet",
            "dialog.UnhideSheet",
            "dialog.About",
            "dialog.LegalNotices",
            "dialog.SelectDataSource",
            "dialog.ChangeChartType",
            "dialog.FormatChartArea",
            "dialog.ShapeEffects",
            "dialog.ShapeGradient",
            "dialog.Zoom",
            "dialog.CustomViews",
            "dialog.PrintPreview",
            "dialog.OpenWorkbook",
            "dialog.SaveAsWorkbook",
            "dialog.ExportOptions",
            "dialog.SelectionPane",
            "dialog.PivotTableOptions",
            "dialog.PivotFieldFilter",
            "dialog.PivotValueFieldSettings",
            "dialog.InsertSlicer",
            "dialog.InsertTimeline",
            "dialog.AllowEditRanges",
            "dialog.ProtectSheet",
            "dialog.ProtectWorkbook",
            "dialog.AccessibilityChecker",
            "dialog.DataValidation",
            "dialog.ConditionalFormatNewRule",
            "dialog.ConditionalFormatManage",
            "dialog.PageSetup",
            "dialog.Options",
        };

        foreach (var dialogId in dialogIds)
        {
            avaloniaCaptureSource.Should().Contain(dialogId);
            wpfCaptureSource.Should().Contain(dialogId);
        }

        avaloniaCaptureSource.Should().Contain("AutoFilterParityFixturePlanner.CreateFixturePlan(");
        wpfCaptureSource.Should().Contain("AutoFilterParityFixturePlanner.CreateFixturePlan(");
        avaloniaCaptureSource.Should().NotContain("SeedAutoFilterParityRange");
        wpfCaptureSource.Should().NotContain("SeedAutoFilterParityRange");
        avaloniaCaptureSource.Should().NotContain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        wpfCaptureSource.Should().NotContain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        autoFilterFixtureSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        autoFilterFixtureSource.Should().Contain("WorksheetAutoFilterModel(range.ToString(), null)");

        avaloniaCaptureSource.Should().Contain("SelectionPaneParityFixture.CreateDialogItems(");
        wpfCaptureSource.Should().Contain("SelectionPaneParityFixture.CreateDialogItems(");
        avaloniaCaptureSource.Should().Contain("SelectionPaneParityFixture.ChartName");
        avaloniaCaptureSource.Should().Contain("SelectionPaneParityFixture.ShapeName");
        wpfCaptureSource.Should().Contain("CreateSelectionPaneItems()");
        wpfCaptureSource.Should().NotContain("\"Text Box 1\"");
        wpfCaptureSource.Should().NotContain("\"Picture 1\"");

        avaloniaCaptureSource.Should().Contain("ShowAccessibilityCheckerParityDialogAsync()");
        avaloniaCaptureSource.Should().Contain("AccessibilityCheckerParityFixture.CreateDialogIssues(");
        wpfCaptureSource.Should().Contain("AccessibilityCheckerParityFixture.CreateDialogIssues(");
        wpfCaptureSource.Should().NotContain("CreateAccessibilityIssues(");
        accessibilityCheckerFixtureSource.Should().Contain("AccessibilityIssueKind.DefaultWorksheetName");
        accessibilityCheckerFixtureSource.Should().Contain("AccessibilityIssueKind.MissingAltText");

        // The multi-tab / multi-category dialogs declare an identical, position-ordered tab-name list in
        // each shell, so the comparison runner pairs `dialog.<Name>.<TabName>` one-for-one. The capture
        // builds each per-tab PNG name as $"{surfaceId}.{tabName}", so assert each tab name appears in the
        // tab-name array (the literal `"<TabName>"`) in both sources.
        var tabNamesById = new (string DialogId, string[] TabNames)[]
        {
            ("dialog.FormatCells", ["Number", "Alignment", "Font", "Fill", "Border", "Protection"]),
            ("dialog.PageSetup", ["Page", "Margins", "HeaderFooter", "Sheet"]),
            ("dialog.FindReplace", ["Find", "Replace"]),
            ("dialog.PivotTableOptions", ["LayoutAndFormat", "TotalsAndFilters", "Display", "Printing", "Data", "AltText"]),
            ("dialog.PivotFieldFilter", ["SelectItems", "LabelFilters", "ValueFilters"]),
            ("dialog.PivotValueFieldSettings", ["SummarizeValuesBy", "ShowValuesAs", "NumberFormat"]),
            ("dialog.Options", ["General", "Formulas", "Proofing", "Save", "Language", "EaseOfAccess", "Advanced", "CustomizeRibbon", "QuickAccessToolbar", "AddIns", "TrustCenter", "View"]),
        };

        foreach (var (_, tabNames) in tabNamesById)
        {
            foreach (var tabName in tabNames)
            {
                avaloniaCaptureSource.Should().Contain($"\"{tabName}\"");
                wpfCaptureSource.Should().Contain($"\"{tabName}\"");
            }
        }
    }

    [Fact]
    public void AvaloniaAutoFilterFlyout_InitialFocusMatchesWpfFirstCommand()
    {
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.Controls.cs"));

        wpfSource.Should().Contain("FocusInitialKeyboardTarget()");
        wpfSource.Should().Contain("_sortAscendingButton.Focus();");
        avaloniaSource.Should().Contain("Control? initialFocusTarget = null;");
        avaloniaSource.Should().Contain("item.FocusRole == AutoFilterMenuEntryFocusRole.Command");
        avaloniaSource.Should().Contain("(initialFocusTarget ?? searchBox).Focus();");
        avaloniaSource.Should().Contain("private Flyout? _autoFilterFlyout;");
        avaloniaSource.Should().Contain("CloseAutoFilterFlyout();");
        avaloniaSource.Should().NotContain("flyout.ShowAt(anchor);\r\n        searchBox.Focus();");
    }

    [Fact]
    public void MainWindow_WiresNativeDataTableThroughSharedPlannerSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "DataTablePlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _dataTableMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataTable, \"Data Table...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_dataTableMenuItem.Click += async (_, _) => await ShowDataTableDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DataTable,");
        source.Should().Contain("=> CreateNativeMenu(NativeMenuCatalog.WhatIfAnalysisMenuEntries);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.GoalSeek)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.DataTable)");

        source.Should().Contain("private async Task ShowDataTableDialogAsync()");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var plan = await ShowDataTableInputDialogAsync();");
        source.Should().Contain("var result = _session.ExecuteDataTablePlan(plan);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Data Table failed.\");");
        source.Should().Contain("RefreshShell($\"Created {FormatDataTableMode(plan)} Data Table for {tableRange}\");");

        source.Should().Contain("private async Task<DataTablePlan?> ShowDataTableInputDialogAsync(");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"DataTableCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(rowInputBox, \"DataTableRowInputCellBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(columnInputBox, \"DataTableColumnInputCellBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"DataTableErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"DataTableOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"DataTableCancelButton\");");
        source.Should().Contain("DataTablePlanner.CreatePlan(");
        source.Should().Contain("_session.ActiveSheet");
        source.Should().Contain("_session.SelectedRange");
        source.Should().Contain("rowInputBox.Text");
        source.Should().Contain("columnInputBox.Text");
        source.Should().Contain("sheetName => _session.Workbook.GetSheet(sheetName)?.Id");
        source.Should().Contain("FocusDataTableErrorField(planResult.Status, rowInputBox, columnInputBox);");
        source.Should().Contain("private static string FormatDataTableMode(DataTablePlan plan)");
        source.Should().Contain("private static string FormatDataTablePlanError(DataTablePlanResult result)");
        source.Should().Contain("private static void FocusDataTableErrorField(");
        source.Should().Contain("private static void AddDataTableReferenceRow(");
        source.Should().Contain("CreateDialogRangePickerButton(");
        source.Should().Contain("BuildDialogRangePickerRow(input, picker)");
        source.Should().Contain("AttachDialogRangePicker(dialog, rowInputPicker, rowInputBox, \"range.data-table.row-input-cell\");");
        source.Should().Contain("AttachDialogRangePicker(dialog, columnInputPicker, columnInputBox, \"range.data-table.column-input-cell\");");

        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteDataTablePlan(DataTablePlan plan)");
        sessionSource.Should().Contain("plan.CreateCommand()");
        sessionSource.Should().Contain("ApplySuccessfulRangeEditResult(result, plan.OutputRange);");
        plannerSource.Should().Contain("public static DataTablePlanResult CreatePlan(");
        plannerSource.Should().Contain("public IWorkbookCommand CreateCommand()");
        parityCaptureSource.Should().Contain("(\"dialog.DataTable\", () => ShowDataTableParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private Task ShowDataTableParityDialogAsync()");
        parityCaptureSource.Should().Contain("async () => { await ShowDataTableInputDialogAsync(); }");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowDataTableDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowForecastSheetDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().Contain("Width = 360,");
        routeSource.Should().Contain("Height = 210,");
        routeSource.Should().Contain("MinWidth = 360,");
        routeSource.Should().Contain("MaxHeight = 210,");
        routeSource.Should().NotContain("DataTableRangeSummaryText");
        routeSource.Should().NotContain("Table range:");
        routeSource.Should().NotContain("new OneVariableDataTableCommand");
        routeSource.Should().NotContain("new TwoVariableDataTableCommand");
        routeSource.Should().NotContain("new DataTableDialog");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeForecastSheetThroughSharedPlannerSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _forecastSheetMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ForecastSheet, \"Forecast Sheet...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_forecastSheetMenuItem.Click += async (_, _) => await ShowForecastSheetDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ForecastSheet)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ForecastSheet, context.IsIdle)");

        var whatIfMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.WhatIfAnalysis)", StringComparison.Ordinal);
        var forecastSheetMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.ForecastSheet)", StringComparison.Ordinal);
        whatIfMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        forecastSheetMenuIndex.Should().BeGreaterThan(whatIfMenuIndex);

        var whatIfSubmenuIndex = normalizedSource.IndexOf("private NativeMenu CreateNativeWhatIfAnalysisMenu()", StringComparison.Ordinal);
        whatIfSubmenuIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMenuFactoryIndex = normalizedSource.IndexOf("\n    private NativeMenu CreateNativeClearMenu()", whatIfSubmenuIndex, StringComparison.Ordinal);
        nextMenuFactoryIndex.Should().BeGreaterThan(whatIfSubmenuIndex);
        var whatIfSubmenuSource = normalizedSource[whatIfSubmenuIndex..nextMenuFactoryIndex];
        whatIfSubmenuSource.Should().NotContain("_forecastSheetMenuItem");

        source.Should().Contain("private async Task ShowForecastSheetDialogAsync()");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var plan = await ShowForecastSheetInputDialogAsync();");
        source.Should().Contain("var sourceRange = FormatRangeReference(plan.SourceRange ?? _session.SelectedRange);");
        source.Should().Contain("var result = _session.ExecuteForecastSheetPlan(plan);");
        source.Should().Contain("RefreshShell(_statusText.Text ?? \"Ready\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Forecast Sheet failed.\");");
        source.Should().Contain("RefreshShell($\"Created Forecast Sheet from {sourceRange}\");");

        source.Should().Contain("private async Task<ForecastSheetPlan?> ShowForecastSheetInputDialogAsync()");
        source.Should().Contain("Title = \"Forecast Sheet\",");
        source.Should().Contain("Text = $\"Source range: {FormatRangeReference(_session.SelectedRange)}\",");
        source.Should().Contain("Text = ForecastSheetPlanner.DefaultForecastPeriods.ToString(CultureInfo.InvariantCulture),");
        source.Should().Contain("AutomationProperties.SetName(periodsBox, \"Forecast periods\");");
        source.Should().Contain("AutomationProperties.SetHelpText(periodsBox, \"Enter the positive whole number of periods to forecast.\");");
        source.Should().Contain("Content = \"Create\",");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ForecastSheetCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(sourceRangeText, \"ForecastSheetSourceRangeSummaryText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(periodsBox, \"ForecastPeriodsBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"ForecastSheetErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(createButton, \"ForecastSheetCreateButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"ForecastSheetCancelButton\");");
        source.Should().Contain("ForecastSheetPlanner.CreatePlan(");
        source.Should().Contain("_session.Workbook");
        source.Should().Contain("_session.SelectedRange");
        source.Should().Contain("periodsBox.Text");
        source.Should().Contain("_session.ExecuteForecastSheetPlan(plan)");
        source.Should().Contain("CreateForecastSheetField(\"Forecast periods\", periodsBox)");

        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteForecastSheetPlan(ForecastSheetPlan plan)");
        parityCaptureSource.Should().Contain("(\"dialog.ForecastSheet\", () => ShowForecastSheetParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private Task ShowForecastSheetParityDialogAsync()");
        parityCaptureSource.Should().Contain("async () => { await ShowForecastSheetInputDialogAsync(); }");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowForecastSheetDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowDataValidationDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("new ForecastSheetCommand");
        routeSource.Should().NotContain("new ForecastSheetDialog");
        routeSource.Should().NotContain("TryExecuteCommand(");
        routeSource.Should().NotContain("ForecastSheetInputParser");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("System.Windows");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("AddSheet(");
        routeSource.Should().NotContain("SetCell(");
        routeSource.Should().NotContain("Charts.Add");
        routeSource.Should().NotContain("LastOrDefault");
    }

    [Fact]
    public void MainWindow_WiresNativeScenarioManagerThroughSharedPlannerSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "ScenarioManagerPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _scenarioManagerMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ScenarioManager, \"Scenario Manager...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_scenarioManagerMenuItem.Click += async (_, _) => await ShowScenarioManagerDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.ScenarioManager, context.IsIdle)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ScenarioManager)");

        source.Should().Contain("private async Task ShowScenarioManagerDialogAsync()");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var initialPlan = ScenarioManagerPlanner.CreateDialogPlan(_session.Workbook);");
        source.Should().Contain("await ShowScenarioManagerCompactDialogAsync(initialPlan);");

        source.Should().Contain("private async Task ShowScenarioManagerCompactDialogAsync(ScenarioManagerPlan initialPlan)");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ScenarioManagerCompactDialog\");");
        source.Should().Contain("Width = ScenarioManagerDialogLayout.DialogWidth,");
        source.Should().Contain("Height = ScenarioManagerDialogLayout.DialogHeight,");
        source.Should().Contain("MaxWidth = ScenarioManagerDialogLayout.DialogWidth,");
        source.Should().Contain("MaxHeight = ScenarioManagerDialogLayout.DialogHeight,");
        source.Should().Contain("CanResize = false,");
        source.Should().Contain("AutomationProperties.SetAutomationId(scenarioList, \"ScenarioManagerScenarioList\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(nameBox, \"ScenarioManagerNameBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(commentBox, \"ScenarioManagerCommentBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(saveButton, \"ScenarioManagerSaveButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(showButton, \"ScenarioManagerShowButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(deleteButton, \"ScenarioManagerDeleteButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(summaryButton, \"ScenarioManagerSummaryButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"ScenarioManagerErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(closeButton, \"ScenarioManagerCloseButton\");");

        source.Should().Contain("ScenarioManagerPlanner.CreateSavePlan(_session.Workbook, request);");
        source.Should().Contain("_session.ExecuteScenarioManagerSavePlan(savePlan, request);");
        source.Should().Contain("ScenarioManagerPlanner.CreateShowPlan(_session.Workbook, scenarioName);");
        source.Should().Contain("_session.ExecuteScenarioManagerShowPlan(showPlan);");
        source.Should().Contain("ScenarioManagerPlanner.CreateDeletePlan(_session.Workbook, scenarioName);");
        source.Should().Contain("_session.ExecuteScenarioManagerDeletePlan(deletePlan);");
        source.Should().Contain("ScenarioManagerPlanner.CreateSummaryReportPlan(_session.Workbook, resultCells);");
        source.Should().Contain("_session.ExecuteScenarioManagerSummaryReportPlan(summaryPlan);");
        source.Should().Contain("CaptureScenarioManagerChangingCells(ranges);");
        source.Should().Contain("ScenarioManagerDialogPlanner.BuildItems(_session.Workbook)");
        source.Should().Contain("ScenarioManagerDialogPlanner.ProjectSelectionFields(");
        source.Should().Contain("ScenarioManagerDialogPlanner.ValidateAcceptRequest(");
        source.Should().Contain("ScenarioManagerDialogPlanner.ProjectAcceptResult(");
        source.Should().Contain("new ScenarioManagerSaveRequest(");
        source.Should().Contain("RefreshShell(status);");
        source.Should().Contain("ShowEditIssue(message);");

        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteScenarioManagerSavePlan(");
        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteScenarioManagerShowPlan(ScenarioManagerPlan plan)");
        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteScenarioManagerDeletePlan(ScenarioManagerPlan plan)");
        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteScenarioManagerSummaryReportPlan(ScenarioManagerPlan plan)");
        plannerSource.Should().Contain("public static ScenarioManagerPlan CreateDialogPlan(");
        plannerSource.Should().Contain("public static ScenarioManagerPlan CreateSavePlan(");
        plannerSource.Should().Contain("public static ScenarioManagerPlan CreateShowPlan(");
        plannerSource.Should().Contain("public static ScenarioManagerPlan CreateDeletePlan(");
        plannerSource.Should().Contain("public static ScenarioManagerPlan CreateSummaryReportPlan(");
        parityCaptureSource.Should().Contain("(\"dialog.ScenarioManager\", () => ShowScenarioManagerParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private async Task ShowScenarioManagerParityDialogAsync()");
        parityCaptureSource.Should().Contain("var changingCellsRange = ScenarioManagerParityFixture.ChangingCellsRange(_session.ActiveSheet.Id);");
        parityCaptureSource.Should().Contain("ScenarioManagerParityFixture.Seed(_session.Workbook, _session.ActiveSheet.Id);");
        parityCaptureSource.Should().Contain("ScenarioManagerParityFixture.ScenarioName");
        parityCaptureSource.Should().Contain("await ShowScenarioManagerCompactDialogAsync(plan);");

        var paritySeedIndex = parityCaptureSource.IndexOf(
            "ScenarioManagerParityFixture.Seed(_session.Workbook, _session.ActiveSheet.Id);",
            StringComparison.Ordinal);
        var parityPlanIndex = parityCaptureSource.IndexOf(
            "ScenarioManagerPlanner.CreateDialogPlan(",
            paritySeedIndex,
            StringComparison.Ordinal);
        var parityDialogIndex = parityCaptureSource.IndexOf(
            "await ShowScenarioManagerCompactDialogAsync(plan);",
            parityPlanIndex,
            StringComparison.Ordinal);
        paritySeedIndex.Should().BeGreaterThanOrEqualTo(0);
        parityPlanIndex.Should().BeGreaterThan(paritySeedIndex, "the parity route must seed the scenario before creating the selected dialog plan");
        parityDialogIndex.Should().BeGreaterThan(parityPlanIndex, "the parity route must open the compact dialog from the seeded plan");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowScenarioManagerDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowDataTableDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("_session.SaveScenario(");
        routeSource.Should().NotContain("_session.ShowScenario(");
        routeSource.Should().NotContain("_session.DeleteScenario(");
        routeSource.Should().NotContain("_session.CreateScenarioSummaryReport(");
        routeSource.Should().NotContain("new SaveScenarioCommand");
        routeSource.Should().NotContain("new ApplyScenarioCommand");
        routeSource.Should().NotContain("new DeleteScenarioCommand");
        routeSource.Should().NotContain("new ScenarioSummaryReportCommand");
        routeSource.Should().NotContain("new ScenarioManagerDialog(");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeAdvancedFilterThroughSharedPlannerSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Filtering", "AdvancedFilterPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _advancedFilterMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.AdvancedFilter, \"Advanced Filter...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_advancedFilterMenuItem.Click += async (_, _) => await ShowAdvancedFilterDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.AdvancedFilter)");
        catalogSource.Should().Contain("new(NativeMenuItemId.AdvancedFilter, context.IsIdle)");

        var customSortMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.CustomSort)", StringComparison.Ordinal);
        var advancedFilterMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.AdvancedFilter)", StringComparison.Ordinal);
        var dataValidationMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.DataValidation)", StringComparison.Ordinal);
        customSortMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        advancedFilterMenuIndex.Should().BeGreaterThan(customSortMenuIndex);
        dataValidationMenuIndex.Should().BeGreaterThan(advancedFilterMenuIndex);

        source.Should().Contain("private async Task ShowAdvancedFilterDialogAsync()");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var plan = await ShowAdvancedFilterInputDialogAsync();");
        source.Should().Contain("var result = _session.ExecuteAdvancedFilterPlan(plan);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Advanced Filter failed.\");");
        source.Should().Contain("RefreshShell(FormatAdvancedFilterStatus(plan));");

        source.Should().Contain("private async Task<AdvancedFilterPlan?> ShowAdvancedFilterInputDialogAsync()");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"AdvancedFilterCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(listRangeBox, \"AdvancedFilterListRangeBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(criteriaRangeBox, \"AdvancedFilterCriteriaRangeBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(pickerButton, pickerAutomationId);");
        source.Should().Contain("\"AdvancedFilterSelectListRangeButton\"");
        source.Should().Contain("\"AdvancedFilterSelectCriteriaRangeButton\"");
        source.Should().Contain("\"AdvancedFilterSelectCopyToButton\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(inPlaceButton, \"AdvancedFilterInPlaceButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(copyToAnotherLocationButton, \"AdvancedFilterCopyToAnotherLocationButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(copyToBox, \"AdvancedFilterCopyToBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(uniqueBox, \"AdvancedFilterUniqueRecordsOnlyBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"AdvancedFilterErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"AdvancedFilterOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"AdvancedFilterCancelButton\");");
        source.Should().Contain("Text = FormatRangeReference(AdvancedFilterPlanner.CreateDefaultListRange(_session.ActiveSheet, _session.SelectedRange))");
        source.Should().Contain("var selectedOutputMode = copyToAnotherLocationButton.IsChecked == true");
        source.Should().Contain("AdvancedFilterOutputMode.CopyToAnotherLocation");
        source.Should().Contain("AdvancedFilterOutputMode.FilterInPlace");
        source.Should().Contain("AdvancedFilterPlanner.CreatePlan(");
        source.Should().Contain("_session.ActiveSheet.Id");
        source.Should().Contain("listRangeBox.Text");
        source.Should().Contain("criteriaRangeBox.Text");
        source.Should().Contain("copyToBox.Text");
        source.Should().Contain("selectedOutputMode");
        source.Should().Contain("uniqueBox.IsChecked == true");
        source.Should().Contain("sheetName => _session.Workbook.GetSheet(sheetName)?.Id");
        source.Should().Contain("FocusAdvancedFilterErrorField(planResult.Error, listRangeBox, criteriaRangeBox, copyToBox);");
        source.Should().Contain("private static string FormatAdvancedFilterStatus(AdvancedFilterPlan plan)");
        source.Should().Contain("private static string FormatAdvancedFilterPlanError(AdvancedFilterPlanResult result)");
        source.Should().Contain("private static void FocusAdvancedFilterErrorField(");
        source.Should().Contain("var actionGroup = new AvaloniaGrid");
        source.Should().Contain("Text = \"Action\"");

        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteAdvancedFilterPlan(AdvancedFilterPlan plan)");
        sessionSource.Should().Contain("ApplySuccessfulRangeEditResult(result, GetAdvancedFilterSelectedRange(plan));");
        plannerSource.Should().Contain("public static AdvancedFilterPlanResult CreatePlan(");
        plannerSource.Should().Contain("public static GridRange CreateDefaultListRange(Sheet sheet, GridRange selectedRange)");
        plannerSource.Should().Contain("public AdvancedFilterCommand CreateCommand()");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowAdvancedFilterDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowRemoveDuplicatesDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("new AdvancedFilterCommand");
        routeSource.Should().NotContain("new AdvancedFilterDialog");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeRemoveDuplicatesThroughSharedPlannerSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "RemoveDuplicatesPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _removeDuplicatesMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.RemoveDuplicates, \"Remove Duplicates...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.RemoveDuplicates)");
        catalogSource.Should().Contain("new(NativeMenuItemId.RemoveDuplicates, context.IsIdle && context.SelectedRangeRowCount > 1)");
        source.Should().Contain("HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, NativeMenuItemId.RemoveDuplicates)");

        var advancedFilterMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.AdvancedFilter)", StringComparison.Ordinal);
        var removeDuplicatesMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.RemoveDuplicates)", StringComparison.Ordinal);
        var dataValidationMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.DataValidation)", StringComparison.Ordinal);
        advancedFilterMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        removeDuplicatesMenuIndex.Should().BeGreaterThan(advancedFilterMenuIndex);
        dataValidationMenuIndex.Should().BeGreaterThan(removeDuplicatesMenuIndex);

        source.Should().Contain("private async Task ShowRemoveDuplicatesDialogAsync()");
        source.Should().Contain("if (_isOpening || _isSaving)");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("var plan = await ShowRemoveDuplicatesInputDialogAsync();");
        source.Should().Contain("var result = _session.ExecuteRemoveDuplicatesPlan(plan);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Remove Duplicates failed.\");");
        source.Should().Contain("RefreshShell(status);");
        source.Should().Contain("ShowTextDialogAsync(UiText.Get(\"MainWindowMessage_RemoveDuplicatesTitle\"), status, 420, 220)");

        source.Should().Contain("private async Task<RemoveDuplicatesPlan?> ShowRemoveDuplicatesInputDialogAsync(bool? forceHasHeaders = null)");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"RemoveDuplicatesCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(hasHeadersBox, \"RemoveDuplicatesHasHeadersBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(columnsPanel, \"RemoveDuplicatesColumnsPanel\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(box, $\"RemoveDuplicatesColumn{column.Offset}Box\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(selectAllButton, \"RemoveDuplicatesSelectAllButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(unselectAllButton, \"RemoveDuplicatesUnselectAllButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"RemoveDuplicatesErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"RemoveDuplicatesOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"RemoveDuplicatesCancelButton\");");
        source.Should().Contain("RemoveDuplicatesPlanner.GuessHasHeaders(_session.ActiveSheet, range)");
        source.Should().Contain("RemoveDuplicatesPlanner.BuildColumnChoices(_session.ActiveSheet, range, hasHeaders)");
        source.Should().Contain("RemoveDuplicatesPlanner.CreatePlan(");
        source.Should().Contain("RenderColumns(RemoveDuplicatesPlanner.SelectAll(CaptureColumns()))");
        source.Should().Contain("RenderColumns(RemoveDuplicatesPlanner.ClearAll(CaptureColumns()))");
        source.Should().Contain("ApplyDialogButtonChrome(selectAllButton, width: 88);");
        source.Should().Contain("ApplyDialogButtonChrome(unselectAllButton, width: 88);");
        source.Should().Contain("Child = new StackPanel");
        source.Should().Contain("Height = 160");
        source.Should().Contain("private static string FormatRemoveDuplicatesStatus(");

        sessionSource.Should().Contain("public WorkbookRemoveDuplicatesResult ExecuteRemoveDuplicatesPlan(RemoveDuplicatesPlan plan)");
        sessionSource.Should().Contain("CreateGroupedSheetCommand(");
        sessionSource.Should().Contain("\"Remove Duplicates\"");
        sessionSource.Should().Contain("plan.CreateCommand(sheetId)");
        sessionSource.Should().Contain("ApplySuccessfulRangeEditResult(result, plan.SourceRange);");
        plannerSource.Should().Contain("public static bool GuessHasHeaders(Sheet sheet, GridRange range)");
        plannerSource.Should().Contain("public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)");
        plannerSource.Should().Contain("public GridRange ActiveRangeForSheet(SheetId sheetId)");
        plannerSource.Should().Contain("public RemoveDuplicateRowsCommand CreateCommand(SheetId sheetId, GridRange activeRange)");
        plannerSource.Should().Contain("public RemoveDuplicateRowsCommand CreateCommand(SheetId sheetId)");
        parityCaptureSource.Should().Contain("(\"dialog.RemoveDuplicates\", () => ShowRemoveDuplicatesParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private async Task ShowRemoveDuplicatesParityDialogAsync()");
        parityCaptureSource.Should().Contain("var previousHeaders = CaptureHeaderCells(sheet, row: 1, startColumn: 1, columnCount: 4);");
        parityCaptureSource.Should().Contain("SeedRemoveDuplicatesParityHeaders(sheet);");
        parityCaptureSource.Should().Contain("await ShowRemoveDuplicatesInputDialogAsync(forceHasHeaders: true);");
        parityCaptureSource.Should().Contain("RestoreHeaderCells(sheet, row: 1, startColumn: 1, previousHeaders);");
        parityCaptureSource.Should().Contain("string[] headers = [\"Region\", \"Product\", \"Revenue\", \"Units\"];");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowRemoveDuplicatesDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowScenarioManagerDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("new RemoveDuplicateRowsCommand");
        routeSource.Should().NotContain("new RemoveDuplicatesDialog");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresNativeSubtotalThroughSharedWorkbookSessionAndCompactDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _subtotalMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Subtotal, \"Subtotal...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Subtotal)");
        catalogSource.Should().Contain("NativeMenuItemId.Subtotal,");
        source.Should().Contain("HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, NativeMenuItemId.Subtotal)");

        var removeDuplicatesMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.RemoveDuplicates)", StringComparison.Ordinal);
        var subtotalMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.Subtotal)", StringComparison.Ordinal);
        var dataValidationMenuIndex = catalogSource.IndexOf("Item(NativeMenuItemId.DataValidation)", StringComparison.Ordinal);
        removeDuplicatesMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        subtotalMenuIndex.Should().BeGreaterThan(removeDuplicatesMenuIndex);
        dataValidationMenuIndex.Should().BeGreaterThan(subtotalMenuIndex);

        source.Should().Contain("private async Task ShowSubtotalDialogAsync()");
        source.Should().Contain("var selection = await ShowSubtotalInputDialogAsync();");
        source.Should().Contain("_session.RemoveSelectedRangeSubtotals()");
        source.Should().Contain("_session.ExecuteSubtotalOptions(selection.Options!)");
        source.Should().Contain("private async Task<SubtotalDialogResult?> ShowSubtotalInputDialogAsync(");
        source.Should().Contain("SubtotalParityFixtureState? parityFixture = null");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"SubtotalCompactDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(groupColumnBox, \"SubtotalGroupColumnBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(functionBox, \"SubtotalFunctionBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(columnsList, \"SubtotalColumnsPanel\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(box, $\"SubtotalColumn{column.Offset}Box\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(replaceBox, \"SubtotalReplaceCurrentBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(pageBreakBox, \"SubtotalPageBreakBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(summaryBelowBox, \"SubtotalSummaryBelowBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(errorText, \"SubtotalErrorText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"SubtotalOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(removeAllButton, \"SubtotalRemoveAllButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"SubtotalCancelButton\");");
        source.Should().Contain("SubtotalDialogPlanner.BuildColumnChoices(");
        source.Should().Contain("SubtotalDialogPlanner.CreateFunctionChoices(plannerText)");
        source.Should().Contain("SubtotalDialogPlanner.TryCreateResult(");
        source.Should().Contain("plan.ToInputOptions()");

        sessionSource.Should().Contain("public WorkbookCellEditResult ExecuteSubtotalOptions(SubtotalInputOptions options)");
        sessionSource.Should().Contain("public WorkbookCellEditResult RemoveSelectedRangeSubtotals()");
        sessionSource.Should().Contain("new SubtotalCommand(");
        sessionSource.Should().Contain("new RemoveSubtotalRowsCommand(sheetId, sheetRange)");

        smokeSource.Should().Contain("bool HasNativeSubtotalMenuItem,");
        smokeSource.Should().Contain("HasNativeSubtotalMenuItem &&");
        smokeSource.Should().Contain("native_subtotal_menu_item={FormatBool(snapshot.HasNativeSubtotalMenuItem)}");
        parityCaptureSource.Should().Contain("(\"dialog.Subtotal\", () => ShowSubtotalParityDialogAsync()),");
        parityCaptureSource.Should().Contain("private Task ShowSubtotalParityDialogAsync()");
        parityCaptureSource.Should().Contain("var fixture = SubtotalParityFixture.CreateState(_session.ActiveSheet);");
        parityCaptureSource.Should().Contain("async () => { await ShowSubtotalInputDialogAsync(fixture); }");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowSubtotalDialogAsync()", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowRemoveDuplicatesDialogAsync()", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("new SubtotalCommand");
        routeSource.Should().NotContain("new RemoveSubtotalRowsCommand");
        routeSource.Should().NotContain("new SubtotalDialog(");
        routeSource.Should().NotContain("SubtotalFunctionService.TryParse");
        routeSource.Should().NotContain("new SubtotalInputOptions(");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_SetsCompactDialogVoiceOverNamesAndHelpText()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var markers = new[]
        {
            "AutomationProperties.SetName(groupColumnBox, StripDisplayMnemonic(UiText.Get(\"Subtotal_AtEachChangeInAutomationName\")));",
            "AutomationProperties.SetHelpText(groupColumnBox, UiText.Get(\"Subtotal_AtEachChangeInHelpText\"));",
            "AutomationProperties.SetName(functionBox, StripDisplayMnemonic(UiText.Get(\"Subtotal_UseFunctionAutomationName\")));",
            "AutomationProperties.SetHelpText(functionBox, UiText.Get(\"Subtotal_UseFunctionHelpText\"));",
            "AutomationProperties.SetName(columnsList, StripDisplayMnemonic(UiText.Get(\"Subtotal_AddSubtotalToAutomationName\")));",
            "AutomationProperties.SetHelpText(columnsList, UiText.Get(\"Subtotal_AddSubtotalToHelpText\"));",
            "AutomationProperties.SetName(replaceBox, StripDisplayMnemonic(UiText.Get(\"Subtotal_ReplaceCurrentSubtotalsAutomationName\")));",
            "AutomationProperties.SetHelpText(replaceBox, UiText.Get(\"Subtotal_ReplaceCurrentSubtotalsHelpText\"));",
            "AutomationProperties.SetName(pageBreakBox, StripDisplayMnemonic(UiText.Get(\"Subtotal_PageBreakBetweenGroupsAutomationName\")));",
            "AutomationProperties.SetHelpText(pageBreakBox, UiText.Get(\"Subtotal_PageBreakBetweenGroupsHelpText\"));",
            "AutomationProperties.SetName(summaryBelowBox, StripDisplayMnemonic(UiText.Get(\"Subtotal_SummaryBelowDataAutomationName\")));",
            "AutomationProperties.SetHelpText(summaryBelowBox, UiText.Get(\"Subtotal_SummaryBelowDataHelpText\"));",
            "AutomationProperties.SetName(errorText, \"Subtotal validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Subtotal validation messages.\");",
            "AutomationProperties.SetName(hasHeadersBox, \"My data has headers\");",
            "AutomationProperties.SetHelpText(hasHeadersBox, \"Treat the first row as headers when comparing duplicates.\");",
            "AutomationProperties.SetName(columnsPanel, \"Columns\");",
            "AutomationProperties.SetHelpText(columnsPanel, \"Columns used to identify duplicate rows.\");",
            "AutomationProperties.SetName(selectAllButton, \"Select All\");",
            "AutomationProperties.SetHelpText(selectAllButton, \"Select all columns for duplicate comparison.\");",
            "AutomationProperties.SetName(unselectAllButton, \"Unselect All\");",
            "AutomationProperties.SetHelpText(unselectAllButton, \"Clear all selected duplicate comparison columns.\");",
            "AutomationProperties.SetName(errorText, \"Remove Duplicates validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Remove Duplicates validation messages.\");",
            "AutomationProperties.SetName(inPlaceButton, \"Filter in-place\");",
            "AutomationProperties.SetHelpText(inPlaceButton, \"Filter the list range without copying results.\");",
            "AutomationProperties.SetName(copyToAnotherLocationButton, \"Copy to another location\");",
            "AutomationProperties.SetHelpText(copyToAnotherLocationButton, \"Copy filtered rows to the Copy to range.\");",
            "AutomationProperties.SetName(uniqueBox, \"Unique records only\");",
            "AutomationProperties.SetHelpText(uniqueBox, \"Return only unique matching records.\");",
            "AutomationProperties.SetName(errorText, \"Advanced Filter validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Advanced Filter readiness and validation messages.\");",
            "AutomationProperties.SetName(errorText, \"Goal Seek validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Goal Seek input validation messages.\");",
            "AutomationProperties.SetHelpText(summaryBlock, \"Shows the Goal Seek result status.\");",
            "AutomationProperties.SetName(restoreButton, \"Restore Original Values\");",
            "AutomationProperties.SetHelpText(restoreButton, \"Undo the Goal Seek result and restore the original changing cell value.\");",
            "AutomationProperties.SetName(keepButton, \"Keep Result\");",
            "AutomationProperties.SetHelpText(keepButton, \"Keep the applied Goal Seek result in the workbook.\");",
            "AutomationProperties.SetHelpText(scenarioList, \"Select a saved scenario.\");",
            "AutomationProperties.SetName(saveButton, \"Save/Add\");",
            "AutomationProperties.SetHelpText(saveButton, \"Save the selected cells as a new or updated scenario.\");",
            "AutomationProperties.SetName(showButton, \"Show\");",
            "AutomationProperties.SetHelpText(showButton, \"Apply the selected scenario values to the workbook.\");",
            "AutomationProperties.SetName(deleteButton, \"Delete\");",
            "AutomationProperties.SetHelpText(deleteButton, \"Delete the selected scenario.\");",
            "AutomationProperties.SetName(summaryButton, \"Summary Report\");",
            "AutomationProperties.SetHelpText(summaryButton, \"Create a scenario summary report sheet.\");",
            "AutomationProperties.SetHelpText(closeButton, \"Close Scenario Manager.\");",
            "AutomationProperties.SetName(errorText, \"Data Table validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Data Table readiness and validation messages.\");",
            "AutomationProperties.SetName(sourceRangeText, \"Forecast source range\");",
            "AutomationProperties.SetHelpText(sourceRangeText, \"Shows the selected source range for the forecast.\");",
            "AutomationProperties.SetName(errorText, \"Forecast Sheet validation\");",
            "AutomationProperties.SetHelpText(errorText, \"Shows Forecast Sheet readiness and validation messages.\");",
            "AutomationProperties.SetName(formula1Box, formula1Label.Text);",
            "\"List source range or comma-separated values.\"",
            "\"Minimum value for the validation rule.\"",
            "\"Value for the validation rule.\"",
            "AutomationProperties.SetName(textBox, title);",
            "AutomationProperties.SetHelpText(textBox, $\"Read-only {title} text.\");",
            "AutomationProperties.SetHelpText(closeButton, $\"Close {title}.\");",
        };

        foreach (var marker in markers)
            source.Should().Contain(marker);
    }

    [Fact]
    public void MainWindow_WiresNativeReviewMenuThroughSharedWorkflowPlanAndNavigation()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var controllerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ReviewSessionController.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "ReviewWorkflowPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("private readonly NativeMenuItem _reviewSummaryMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _checkAccessibilityMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _nextNoteMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _previousNoteMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _nextCommentMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _previousCommentMenuItem = new();");

        catalogSource.Should().Contain("new(NativeMenuItemId.ReviewSummary, \"Review Summary...\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.CheckAccessibility, \"Check Accessibility...\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.NextNote, \"Next Note\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.PreviousNote, \"Previous Note\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.NextComment, \"Next Comment\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.PreviousComment, \"Previous Comment\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_reviewSummaryMenuItem.Click += async (_, _) => await ShowReviewSummaryDialogAsync();");
        source.Should().Contain("_checkAccessibilityMenuItem.Click += async (_, _) => await ShowAccessibilityCheckerDialogAsync();");
        source.Should().Contain("_nextNoteMenuItem.Click += (_, _) => NavigateReviewNote(previous: false);");
        source.Should().Contain("_previousNoteMenuItem.Click += (_, _) => NavigateReviewNote(previous: true);");
        source.Should().Contain("_nextCommentMenuItem.Click += (_, _) => NavigateReviewThreadedComment(previous: false);");
        source.Should().Contain("_previousCommentMenuItem.Click += (_, _) => NavigateReviewThreadedComment(previous: true);");

        source.Should().Contain("var reviewMenu = CreateNativeMenu(NativeMenuTopLevelId.Review);");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> ReviewMenuEntries");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ReviewSummary)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.CheckAccessibility)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.NextNote)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.PreviousNote)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.NextComment)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.PreviousComment)");

        var nativeMenuIndex = normalizedSource.IndexOf("NativeMenuCatalog.TopLevelMenus", StringComparison.Ordinal);
        nativeMenuIndex.Should().BeGreaterThanOrEqualTo(0);
        var dataIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Data, \"Data\")", StringComparison.Ordinal);
        var reviewIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.Review, \"Review\")", StringComparison.Ordinal);
        var viewIndex = catalogSource.IndexOf("new(NativeMenuTopLevelId.View, \"View\")", StringComparison.Ordinal);
        dataIndex.Should().BeGreaterThanOrEqualTo(0);
        reviewIndex.Should().BeGreaterThan(dataIndex);
        viewIndex.Should().BeGreaterThan(reviewIndex);

        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.ReviewSummary, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.CheckAccessibility, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.NextNote, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.PreviousNote, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.NextComment, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.PreviousComment, context.IsIdle)");

        source.Should().Contain("private async Task ShowReviewSummaryDialogAsync(bool focusAccessibility = false)");
        source.Should().Contain("var plan = _session.GetReviewWorkflowPlan();");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ReviewSummaryDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(summaryBlock, \"ReviewSummaryText\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(list, automationId);");
        source.Should().Contain("ReviewSpellingIssuesList");
        source.Should().Contain("ReviewAccessibilityIssuesList");
        source.Should().Contain("ReviewNotesList");
        source.Should().Contain("ReviewCommentsList");
        source.Should().Contain("AutomationProperties.SetAutomationId(closeButton, \"ReviewCloseButton\");");
        source.Should().Contain("var display = ReviewWorkflowPlanner.CreateDisplayModel(plan);");
        source.Should().Contain("Text = display.Summary");
        source.Should().Contain("display.SpellingIssues");
        source.Should().Contain("display.AccessibilityIssues");
        source.Should().Contain("display.Notes");
        source.Should().Contain("display.ThreadedComments");
        source.Should().NotContain("private static string FormatReviewWorkflowSummary(");
        source.Should().NotContain("private static IReadOnlyList<string> FormatReviewSpellingIssues(");

        source.Should().Contain("private void NavigateReviewNote(bool previous)");
        source.Should().Contain("() => ReviewSessionController.NavigateNote(previous)");
        source.Should().Contain("private void NavigateReviewThreadedComment(bool previous)");
        source.Should().Contain("() => ReviewSessionController.NavigateThreadedComment(previous)");
        source.Should().Contain("private void NavigateReviewTarget(");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? fallbackMessage);");
        source.Should().Contain("ApplyReviewRefreshPlan(");
        controllerSource.Should().Contain("PresentationReviewSessionController");
        controllerSource.Should().Contain("_session.ExecuteReviewCommand(plan.CreateCommand(fallbackRange))");
        controllerSource.Should().Contain("RefreshShell(status)");

        sessionSource.Should().Contain("public ReviewWorkflowPlan GetReviewWorkflowPlan(");
        sessionSource.Should().Contain("ReviewWorkflowPlanner.CreatePlan(");
        sessionSource.Should().Contain("public WorkbookNavigationResult GoToNextNote(bool previous = false)");
        sessionSource.Should().Contain("public WorkbookNavigationResult GoToNextThreadedComment(bool previous = false)");
        plannerSource.Should().Contain("public sealed record ReviewWorkflowPlan(");
        plannerSource.Should().Contain("public sealed record ReviewWorkflowDisplayModel(");
        plannerSource.Should().Contain("public static ReviewWorkflowDisplayModel CreateDisplayModel(");
        plannerSource.Should().Contain("WorkbookStatisticsService.GetStatistics(workbook)");
        plannerSource.Should().Contain("AccessibilityCheckerService.FindIssues(workbook)");
        plannerSource.Should().Contain("SpellCheckService.FindIssues(workbook, activeSheetId, customDictionary)");
        plannerSource.Should().Contain("SpellCheckWorkflowPlanner.FilterIssues(");
        plannerSource.Should().Contain("CommentNavigationPlanner.FindNext(");
        plannerSource.Should().Contain("public static ReviewNavigationPlan FindNextNote(");
        plannerSource.Should().Contain("public static ReviewNavigationPlan FindNextThreadedComment(");

        var handlerIndex = normalizedSource.IndexOf("private async Task ShowReviewSummaryDialogAsync(bool focusAccessibility = false)", StringComparison.Ordinal);
        handlerIndex.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodIndex = normalizedSource.IndexOf("\n    private async Task ShowFormatCellsDialogAsync(int initialTabIndex = 0)", handlerIndex, StringComparison.Ordinal);
        nextMethodIndex.Should().BeGreaterThan(handlerIndex);
        var routeSource = normalizedSource[handlerIndex..nextMethodIndex];

        routeSource.Should().NotContain("new AccessibilityCheckerDialog");
        routeSource.Should().NotContain("FreeX.App.Host");
        routeSource.Should().NotContain("DataTransferManager");
        routeSource.Should().NotContain("WindowInteropHelper");
        routeSource.Should().NotContain("Microsoft.Win32");
        routeSource.Should().NotContain("System.Windows");
    }

    [Fact]
    public void MainWindow_WiresFillMenuThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        sessionSource.Should().Contain("public bool CanFillSelectedRange(FillCellsDirection direction)");
        sessionSource.Should().Contain("public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)");
        sessionSource.Should().Contain("new FillCellsCommand(sheetId, sheetRange, direction)");
        sessionSource.Should().Contain("private static string GetFillCellsTitle(FillCellsDirection direction)");
        sessionSource.Should().Contain("FillCellsDirection.Down => \"Fill Down\"");
        sessionSource.Should().Contain("FillCellsDirection.Right => \"Fill Right\"");
        sessionSource.Should().Contain("FillCellsDirection.Up => \"Fill Up\"");
        sessionSource.Should().Contain("FillCellsDirection.Left => \"Fill Left\"");

        source.Should().Contain("private readonly DropDownButton _fillCellsButton = new();");
        source.Should().Contain("private readonly MenuItem _fillDownFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _fillRightFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _fillUpFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _fillLeftFlyoutItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillCellsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillDownMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillRightMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillUpMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillLeftMenuItem = new();");
        source.Should().Contain("_fillCellsButton.Content = \"Fill Cells\";");
        source.Should().Contain("_fillCellsButton.Flyout = CreateFillCellsFlyout();");
        source.Should().Contain("AutomationProperties.SetAutomationId(_fillCellsButton, \"HomeFillCellsButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_fillCellsButton, \"Copy the edge cells across the selected range.\");");
        source.Should().Contain("_fillDownFlyoutItem.Header = \"Down\";");
        source.Should().Contain("_fillDownFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);");
        source.Should().Contain("_fillRightFlyoutItem.Header = \"Right\";");
        source.Should().Contain("_fillRightFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);");
        source.Should().Contain("_fillUpFlyoutItem.Header = \"Up\";");
        source.Should().Contain("_fillUpFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);");
        source.Should().Contain("_fillLeftFlyoutItem.Header = \"Left\";");
        source.Should().Contain("_fillLeftFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillCells, \"Fill\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_fillCellsMenuItem.Menu = CreateNativeFillCellsMenu();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillDown, \"Down\", NativeMenuGesture(WorkbookShortcutRoute.FillDown))");
        source.Should().Contain("_fillDownMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillRight, \"Right\", NativeMenuGesture(WorkbookShortcutRoute.FillRight))");
        source.Should().Contain("_fillRightMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillUp, \"Up\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_fillUpMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillLeft, \"Left\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_fillLeftMenuItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.FillCells)");
        source.Should().Contain("private MenuFlyout CreateFillCellsFlyout()");
        source.Should().Contain("private NativeMenu CreateNativeFillCellsMenu()");
        source.Should().Contain("_fillDownFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Down);");
        source.Should().Contain("_fillRightFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Right);");
        source.Should().Contain("_fillUpFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Up);");
        source.Should().Contain("_fillLeftFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Left);");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillCells, context.CanFillCells)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillDown, context.CanFillDown)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillRight, context.CanFillRight)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillUp, context.CanFillUp)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FillLeft, context.CanFillLeft)");
        source.Should().Contain("_fillCellsButton,");
        source.Should().Contain("private void FillSelectedRange(FillCellsDirection direction)");
        source.Should().Contain("var result = _session.FillSelectedRange(direction);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? $\"{FormatFillCellsAction(direction)} failed.\");");
        source.Should().Contain("RefreshShell($\"{FormatFillCellsAction(direction)} in {rangeReference}\");");
        source.Should().Contain("private static string FormatFillCellsAction(FillCellsDirection direction)");
        source.Should().Contain("e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "FillDown", "WorkbookShortcutKey.D", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "FillRight", "WorkbookShortcutKey.R", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "FillDown", "FillSelectedRange(FillCellsDirection.Down);");
        AssertWorkbookShortcutRouteHandled(source, "FillRight", "FillSelectedRange(FillCellsDirection.Right);");
        source.Should().Contain("HasFillCellsButton: _fillCellsButton.Content?.ToString() == \"Fill Cells\"");
        source.Should().Contain("HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, \"Down\")");
        source.Should().Contain("HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, \"Right\")");
        source.Should().Contain("HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, \"Up\")");
        source.Should().Contain("HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, \"Left\")");
        source.Should().Contain("HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, NativeMenuItemId.FillCells)");
        source.Should().Contain("HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillDown)");
        source.Should().Contain("HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillRight)");
        source.Should().Contain("HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillUp)");
        source.Should().Contain("HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillLeft)");

        smokeSource.Should().Contain("bool HasFillCellsButton,");
        smokeSource.Should().Contain("bool HasFillDownMenuItem,");
        smokeSource.Should().Contain("bool HasFillRightMenuItem,");
        smokeSource.Should().Contain("bool HasFillUpMenuItem,");
        smokeSource.Should().Contain("bool HasFillLeftMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFillCellsMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFillDownMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFillRightMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFillUpMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFillLeftMenuItem,");
        smokeSource.Should().Contain("HasFillCellsButton &&");
        smokeSource.Should().Contain("HasFillDownMenuItem &&");
        smokeSource.Should().Contain("HasFillRightMenuItem &&");
        smokeSource.Should().Contain("HasFillUpMenuItem &&");
        smokeSource.Should().Contain("HasFillLeftMenuItem &&");
        smokeSource.Should().Contain("HasNativeFillCellsMenuItem &&");
        smokeSource.Should().Contain("HasNativeFillDownMenuItem &&");
        smokeSource.Should().Contain("HasNativeFillRightMenuItem &&");
        smokeSource.Should().Contain("HasNativeFillUpMenuItem &&");
        smokeSource.Should().Contain("HasNativeFillLeftMenuItem &&");
        smokeSource.Should().Contain("toolbar_fill_cells_button={FormatBool(snapshot.HasFillCellsButton)}");
        smokeSource.Should().Contain("toolbar_fill_down_menu_item={FormatBool(snapshot.HasFillDownMenuItem)}");
        smokeSource.Should().Contain("toolbar_fill_right_menu_item={FormatBool(snapshot.HasFillRightMenuItem)}");
        smokeSource.Should().Contain("toolbar_fill_up_menu_item={FormatBool(snapshot.HasFillUpMenuItem)}");
        smokeSource.Should().Contain("toolbar_fill_left_menu_item={FormatBool(snapshot.HasFillLeftMenuItem)}");
        smokeSource.Should().Contain("native_fill_cells_menu_item={FormatBool(snapshot.HasNativeFillCellsMenuItem)}");
        smokeSource.Should().Contain("native_fill_down_menu_item={FormatBool(snapshot.HasNativeFillDownMenuItem)}");
        smokeSource.Should().Contain("native_fill_right_menu_item={FormatBool(snapshot.HasNativeFillRightMenuItem)}");
        smokeSource.Should().Contain("native_fill_up_menu_item={FormatBool(snapshot.HasNativeFillUpMenuItem)}");
        smokeSource.Should().Contain("native_fill_left_menu_item={FormatBool(snapshot.HasNativeFillLeftMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresClearMenuThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        sessionSource.Should().Contain("public WorkbookCellEditResult ClearSelectedRangeAll()");
        sessionSource.Should().Contain("public WorkbookCellEditResult ClearSelectedRangeFormats()");
        sessionSource.Should().Contain("public WorkbookCellEditResult ClearSelectedRangeComments()");
        sessionSource.Should().Contain("public WorkbookCellEditResult ClearSelectedRangeHyperlinks()");
        sessionSource.Should().Contain("new ClearContentsCommand(sheetId, sheetRange)");
        sessionSource.Should().Contain("CellStyleDiffPlanner.ClearFormatsDiff()");
        sessionSource.Should().Contain("new ClearConditionalFormatsCommand(sheetId, sheetRange)");
        sessionSource.Should().Contain("new ClearDataValidationCommand(sheetId, sheetRange)");
        sessionSource.Should().Contain("new ClearCommentsCommand(sheetId, sheetRange)");
        sessionSource.Should().Contain("new ClearHyperlinksCommand(sheetId, sheetRange)");

        source.Should().Contain("private readonly DropDownButton _clearButton = new();");
        source.Should().Contain("private readonly MenuItem _clearAllFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _clearFormatsFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _clearContentsFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _clearCommentsFlyoutItem = new();");
        source.Should().Contain("private readonly MenuItem _clearHyperlinksFlyoutItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearAllMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearFormatsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearContentsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearCommentsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearHyperlinksMenuItem = new();");
        source.Should().Contain("_clearButton.Content = \"Clear\";");
        source.Should().Contain("_clearButton.Flyout = CreateClearFlyout();");
        source.Should().Contain("_clearButton.Click += ClearButton_Click;");
        source.Should().Contain("AutomationProperties.SetAutomationId(_clearButton, \"HomeClearButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_clearButton, \"Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.\");");
        source.Should().Contain("_clearAllFlyoutItem.Header = \"Clear All\";");
        source.Should().Contain("_clearAllFlyoutItem.Click += (_, _) => ClearSelectedRangeAll();");
        source.Should().Contain("_clearFormatsFlyoutItem.Header = \"Clear Formats\";");
        source.Should().Contain("_clearFormatsFlyoutItem.Click += (_, _) => ClearSelectedRangeFormats();");
        source.Should().Contain("_clearContentsFlyoutItem.Header = \"Clear Contents\";");
        source.Should().Contain("_clearContentsFlyoutItem.Click += (_, _) => ClearSelectedRangeContents();");
        source.Should().Contain("_clearCommentsFlyoutItem.Header = \"Clear Comments and Notes\";");
        source.Should().Contain("_clearCommentsFlyoutItem.Click += (_, _) => ClearSelectedRangeComments();");
        source.Should().Contain("_clearHyperlinksFlyoutItem.Header = \"Clear Hyperlinks\";");
        // Home > Clear > Clear Hyperlinks strips the hyperlink AND its blue/underline formatting in
        // Excel (unlike the right-click "Remove Hyperlink" item, which keeps the formatting and stays
        // wired to ClearSelectedRangeHyperlinks) -- so this flyout item is wired through
        // RemoveSelectedRangeHyperlinks (see WorkbookSession.cs).
        source.Should().Contain("_clearHyperlinksFlyoutItem.Click += (_, _) => RemoveSelectedRangeHyperlinks();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Clear, \"Clear\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_clearMenuItem.Menu = CreateNativeClearMenu();");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Clear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearAll, \"Clear All\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearFormats, \"Clear Formats\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearContents, \"Clear Contents\", new NativeMenuGesturePlan(NativeMenuGestureKey.Delete))");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearComments, \"Clear Comments and Notes\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearHyperlinks, \"Clear Hyperlinks\", RequiresGestureInSmoke: false)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ClearAll)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ClearFormats)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ClearContents)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ClearComments)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ClearHyperlinks)");
        source.Should().Contain("private MenuFlyout CreateClearFlyout()");
        source.Should().Contain("private NativeMenu CreateNativeClearMenu()");
        source.Should().Contain("=> CreateNativeMenu(NativeMenuCatalog.ClearMenuEntries);");
        source.Should().Contain("_clearButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Clear, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearAll, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearFormats, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearContents, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearComments, context.CanClear)");
        catalogSource.Should().Contain("new(NativeMenuItemId.ClearHyperlinks, context.CanClear)");
        source.Should().Contain("private void ClearButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void ClearSelectedRangeAll()");
        source.Should().Contain("var result = _session.ClearSelectedRangeAll();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear All failed.\");");
        source.Should().Contain("private void ClearSelectedRangeFormats()");
        source.Should().Contain("var result = _session.ClearSelectedRangeFormats();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear Formats failed.\");");
        source.Should().Contain("private void ClearSelectedRangeContents()");
        source.Should().Contain("var result = _session.ClearSelectedRangeContents();");
        source.Should().Contain("RefreshShell($\"Cleared {rangeReference}\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear Contents failed.\");");
        source.Should().Contain("private void ClearSelectedRangeComments()");
        source.Should().Contain("var result = _session.ClearSelectedRangeComments();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear Comments and Notes failed.\");");
        source.Should().Contain("private void ClearSelectedRangeHyperlinks()");
        source.Should().Contain("var result = _session.ClearSelectedRangeHyperlinks();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear Hyperlinks failed.\");");
        source.Should().Contain("if (e.Key == Key.Delete)");
        source.Should().Contain("ClearSelectedRangeContents();");

        smokeSource.Should().Contain("bool HasClearButton,");
        smokeSource.Should().Contain("bool HasClearAllMenuItem,");
        smokeSource.Should().Contain("bool HasClearFormatsMenuItem,");
        smokeSource.Should().Contain("bool HasClearContentsMenuItem,");
        smokeSource.Should().Contain("bool HasClearCommentsMenuItem,");
        smokeSource.Should().Contain("bool HasClearHyperlinksMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearAllMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearFormatsMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearContentsMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearCommentsMenuItem,");
        smokeSource.Should().Contain("bool HasNativeClearHyperlinksMenuItem,");
        smokeSource.Should().Contain("toolbar_clear_button={FormatBool(snapshot.HasClearButton)}");
        smokeSource.Should().Contain("native_clear_menu_item={FormatBool(snapshot.HasNativeClearMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresCompactHyperlinkRouteThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "HyperlinkDialogPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        plannerSource.Should().Contain("public sealed record HyperlinkDialogPrefill(");
        sessionSource.Should().Contain("public HyperlinkDialogPrefill GetSelectedRangeHyperlinkDialogPrefill()");
        sessionSource.Should().Contain("public WorkbookCellEditResult SetSelectedRangeHyperlink(HyperlinkDialogPlan plan)");
        sessionSource.Should().Contain("new HyperlinkMetadata(plan.LinkType, plan.ScreenTip, plan.Bookmark)");
        sessionSource.Should().Contain("new SetHyperlinkCommand(");
        sessionSource.Should().Contain("return ToCommand(\"Insert Hyperlink\", commands);");

        source.Should().Contain("private readonly NativeMenuItem _insertHyperlinkMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.InsertHyperlink, \"Hyperlink...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_insertHyperlinkMenuItem.Click += async (_, _) => await ShowInsertHyperlinkDialogAsync();");
        source.Should().Contain("var insertMenu = CreateNativeMenu(NativeMenuTopLevelId.Insert);");
        catalogSource.Should().Contain("Item(NativeMenuItemId.InsertHyperlink)");
        catalogSource.Should().Contain("new(NativeMenuItemId.InsertHyperlink, context.IsIdle)");
        source.Should().Contain("private async Task ShowInsertHyperlinkDialogAsync()");
        source.Should().Contain("private async Task<HyperlinkDialogPlan?> ShowInsertHyperlinkInputDialogAsync()");
        source.Should().Contain("var prefill = _session.GetSelectedRangeHyperlinkDialogPrefill();");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"HyperlinkCompactDialog\");");
        source.Should().Contain("HyperlinkDialogPlanner.TryPlan(");
        source.Should().Contain("var result = _session.SetSelectedRangeHyperlink(plan);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Insert Hyperlink failed.\");");
    }

    [Fact]
    public void MainWindow_WiresOpenHyperlinkRouteThroughWorkbookSessionAndExternalLauncher()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "HyperlinkNavigationPlanner.cs"));
        var launcherSource = File.ReadAllText(RepositoryFileLocator.Find("shared", "Free.Shared.AppServices", "ExternalUriLauncher.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        plannerSource.Should().Contain("public enum HyperlinkNavigationKind");
        plannerSource.Should().Contain("HyperlinkTargetKind.PlaceInThisDocument");
        plannerSource.Should().Contain("HyperlinkNavigationKind.LocalFile");
        plannerSource.Should().Contain("HyperlinkNavigationKind.External");
        plannerSource.Should().Contain("\"http\", \"https\", \"mailto\", \"ftp\"");
        launcherSource.Should().Contain("public static class ExternalUriLauncher");
        launcherSource.Should().Contain("Func<Uri, Task<bool>>? launchAsync");
        launcherSource.Should().Contain("\"http\", \"https\", \"mailto\", \"ftp\"");

        sessionSource.Should().Contain("public bool CanOpenSelectedHyperlink");
        // R112-model-active-cell-vs-selection-1-1 sibling fix: resolves against ActiveCell, not
        // SelectedRange.Start -- Excel opens the ACTIVE cell's hyperlink, which differs from the
        // selection's normalized top-left whenever the selection was made upward/leftward.
        sessionSource.Should().Contain("HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, ActiveCell, CurrentFilePath, out _)");
        sessionSource.Should().Contain("public bool TryGetSelectedHyperlinkPlan(out HyperlinkNavigationPlan? plan)");
        sessionSource.Should().Contain("public bool TryGetHyperlinkPlan(CellAddress address, out HyperlinkNavigationPlan? plan)");
        sessionSource.Should().Contain("public WorkbookNavigationResult OpenSelectedHyperlink()");
        sessionSource.Should().Contain("HyperlinkNavigationKind.WorksheetCell => GoToReference(plan.Target)");
        sessionSource.Should().Contain("Local file hyperlinks require a platform file-opening route.");
        sessionSource.Should().Contain("External hyperlinks are not supported on this platform.");

        source.Should().Contain("private readonly NativeMenuItem _openHyperlinkMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.OpenHyperlink, \"Open Hyperlink\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_openHyperlinkMenuItem.Click += async (_, _) => await OpenSelectedHyperlinkAsync();");
        source.Should().Contain("NativeMenuItemId.OpenHyperlink => _openHyperlinkMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.OpenHyperlink)");
        catalogSource.Should().Contain("new(NativeMenuItemId.OpenHyperlink, context.IsIdle && context.CanOpenSelectedHyperlink)");
        source.Should().Contain("private async Task OpenSelectedHyperlinkAsync()");
        // ...and the shell's own call site passes the ACTIVE cell, not _session.SelectedRange.Start:
        // an upward/leftward selection (drag D4 -> A1) pins ActiveCell at D4 while the normalized
        // Start collapses to A1, and Excel opens the active cell's hyperlink.
        source.Should().Contain("=> await OpenHyperlinkAsync(_session.ActiveCell);");
        source.Should().NotContain("=> await OpenHyperlinkAsync(_session.SelectedRange.Start);");
        source.Should().Contain("private async Task OpenHyperlinkAsync(CellAddress address)");
        source.Should().Contain("if (!_session.TryGetHyperlinkPlan(address, out var plan) || plan is null)");
        source.Should().Contain("await OpenExternalHyperlinkAsync(plan.Target);");
        source.Should().Contain("await OpenLocalFileHyperlinkAsync(plan);");
        source.Should().Contain("var result = _session.OpenHyperlink(address);");
        source.Should().Contain("private async Task OpenLocalFileHyperlinkAsync(HyperlinkNavigationPlan plan)");
        source.Should().Contain("private async Task OpenExternalHyperlinkAsync(string target)");
        source.Should().Contain("private async Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target)");
        source.Should().Contain("ExternalUriLauncher.OpenAsync(target, launchAsync)");

        var openHyperlinkSource = ExtractSourceBlock(
            source,
            "private async Task OpenHyperlinkAsync(CellAddress address)",
            "private async Task OpenLocalFileHyperlinkAsync(HyperlinkNavigationPlan plan)");
        openHyperlinkSource.Should().Contain("await OpenExternalHyperlinkAsync(plan.Target);");
        openHyperlinkSource.Should().Contain("await OpenLocalFileHyperlinkAsync(plan);");
        openHyperlinkSource.Should().NotContain("Process.Start");

        var localFileHyperlinkSource = ExtractSourceBlock(
            source,
            "private async Task OpenLocalFileHyperlinkAsync(HyperlinkNavigationPlan plan)",
            "private async Task OpenExternalHyperlinkAsync(string target)");
        localFileHyperlinkSource.Should().Contain("_fileWorkflow.TryResolveOpenTarget(plan.LocalPath, out var target, out var message)");
        localFileHyperlinkSource.Should().Contain("await OpenWorkbookPathAsync(target.Path);");
        localFileHyperlinkSource.Should().NotContain("OpenExternalUriAsync");
        localFileHyperlinkSource.Should().NotContain("LaunchFile");
        localFileHyperlinkSource.Should().NotContain("LaunchUriAsync");
    }

    [Fact]
    public void MainWindow_WiresBoldThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly ToggleButton _boldButton = new();");
        source.Should().Contain("_boldButton.Content = \"B\";");
        source.Should().Contain("_boldButton.FontWeight = FontWeight.Bold;");
        source.Should().Contain("_boldButton.Click += BoldButton_Click;");
        source.Should().Contain("_boldButton.IsChecked = _session.IsSelectedRangeStartBold;");
        source.Should().Contain("_boldButton.IsEnabled = isIdle;");
        source.Should().Contain("private void BoldButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeBold(_boldButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeBold()");
        source.Should().Contain("private void ApplySelectedRangeBold(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeBold(enabled);");
        source.Should().Contain("_boldButton.IsChecked = _session.IsSelectedRangeStartBold;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Bold failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Bolded\" : \"Unbolded\")} {rangeReference}\");");
        source.Should().Contain("private static bool HasOnlyCommandModifier(KeyModifiers modifiers)");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ToggleBold", "WorkbookShortcutKey.B", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "ToggleBold", "ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: e.Key == Key.B);");
    }

    [Fact]
    public void MainWindow_WiresItalicThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly ToggleButton _italicButton = new();");
        source.Should().Contain("_italicButton.Content = \"I\";");
        source.Should().Contain("_italicButton.FontStyle = FontStyle.Italic;");
        source.Should().Contain("_italicButton.Click += ItalicButton_Click;");
        source.Should().Contain("_italicButton.IsChecked = _session.IsSelectedRangeStartItalic;");
        source.Should().Contain("_italicButton.IsEnabled = isIdle;");
        source.Should().Contain("private void ItalicButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeItalic(_italicButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeItalic()");
        source.Should().Contain("private void ApplySelectedRangeItalic(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeItalic(enabled);");
        source.Should().Contain("_italicButton.IsChecked = _session.IsSelectedRangeStartItalic;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Italic failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Italicized\" : \"Unitalicized\")} {rangeReference}\");");
        source.Should().Contain("FontStyle = fontStyle,");
        source.Should().Contain("var fontStyle = style?.Italic == true ? FontStyle.Italic : FontStyle.Normal;");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ToggleItalic", "WorkbookShortcutKey.I", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "ToggleItalic", "ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: e.Key == Key.I);");
    }

    [Fact]
    public void MainWindow_WiresUnderlineThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly ToggleButton _underlineButton = new();");
        source.Should().Contain("_underlineButton.Content = new TextBlock");
        source.Should().Contain("TextDecorations = CreateTextDecorations(TextDecorationLocation.Underline),");
        source.Should().Contain("private static TextDecorationCollection CreateTextDecorations(TextDecorationLocation location)");
        source.Should().Contain("[new TextDecoration { Location = location }]");
        source.Should().Contain("_underlineButton.Click += UnderlineButton_Click;");
        source.Should().Contain("_underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;");
        source.Should().Contain("_underlineButton.IsEnabled = isIdle;");
        source.Should().Contain("private void UnderlineButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeUnderline(_underlineButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeUnderline()");
        source.Should().Contain("private void ApplySelectedRangeUnderline(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeUnderline(enabled);");
        source.Should().Contain("_underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Underline failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Underlined\" : \"Removed underline from\")} {rangeReference}\");");
        source.Should().Contain("var textDecorations = BuildTextDecorations(style);");
        source.Should().Contain("if (style.Underline || style.DoubleUnderline)");
        source.Should().Contain("textBlock.TextDecorations = textDecorations;");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ToggleUnderline", "WorkbookShortcutKey.U", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ToggleUnderline", "WorkbookShortcutKey.D4", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "ToggleUnderline", "ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: e.Key == Key.U);");
    }

    [Fact]
    public void MainWindow_WiresDoubleUnderlineThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _doubleUnderlineButton = new();");
        source.Should().Contain("_doubleUnderlineButton.Content = new StackPanel");
        source.Should().Contain("Text = \"U\",");
        source.Should().Contain("new Border");
        source.Should().Contain("_doubleUnderlineButton.Click += DoubleUnderlineButton_Click;");
        source.Should().Contain("_doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;");
        source.Should().Contain("_doubleUnderlineButton.IsEnabled = isIdle;");
        source.Should().Contain("private void DoubleUnderlineButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeDoubleUnderline(_doubleUnderlineButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeDoubleUnderline()");
        source.Should().Contain("private void ApplySelectedRangeDoubleUnderline(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeDoubleUnderline(enabled);");
        source.Should().Contain("_doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Double Underline failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Double underlined\" : \"Removed double underline from\")} {rangeReference}\");");
        source.Should().Contain("var textDecorations = BuildTextDecorations(style);");
        source.Should().Contain("private const double DoubleUnderlineSecondStrokeOffset = 2;");
        source.Should().Contain("if (style.Underline || style.DoubleUnderline)");
        source.Should().Contain("if (style.DoubleUnderline)");
        source.Should().Contain("Location = TextDecorationLocation.Underline,");
        source.Should().Contain("StrokeThickness = 1,");
        source.Should().Contain("StrokeThicknessUnit = TextDecorationUnit.Pixel,");
        source.Should().Contain("StrokeOffset = DoubleUnderlineSecondStrokeOffset,");
        source.Should().Contain("StrokeOffsetUnit = TextDecorationUnit.Pixel,");
        source.Should().Contain("ToggleSelectedRangeDoubleUnderline();");
    }

    [Fact]
    public void MainWindow_WiresStrikethroughThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly ToggleButton _strikethroughButton = new();");
        source.Should().Contain("_strikethroughButton.Content = new TextBlock");
        source.Should().Contain("TextDecorations = CreateTextDecorations(TextDecorationLocation.Strikethrough),");
        source.Should().Contain("_strikethroughButton.Click += StrikethroughButton_Click;");
        source.Should().Contain("_strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;");
        source.Should().Contain("_strikethroughButton.IsEnabled = isIdle;");
        source.Should().Contain("private void StrikethroughButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeStrikethrough(_strikethroughButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeStrikethrough()");
        source.Should().Contain("private void ApplySelectedRangeStrikethrough(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeStrikethrough(enabled);");
        source.Should().Contain("_strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Strikethrough failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Struck through\" : \"Removed strikethrough from\")} {rangeReference}\");");
        source.Should().Contain("private static TextDecorationCollection? BuildTextDecorations(CellStyle? style)");
        source.Should().Contain("if (style.Strikethrough)");
        source.Should().Contain("decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ToggleStrikethrough", "WorkbookShortcutKey.D5", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "ToggleStrikethrough", "ToggleSelectedRangeStrikethrough();");
    }

    [Fact]
    public void MainWindow_WiresFontSizeThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly Button _increaseFontSizeButton = new();");
        source.Should().Contain("private readonly Button _decreaseFontSizeButton = new();");
        source.Should().Contain("_increaseFontSizeButton.Content = \"A+\";");
        source.Should().Contain("_decreaseFontSizeButton.Content = \"A-\";");
        source.Should().Contain("_increaseFontSizeButton.Click += IncreaseFontSizeButton_Click;");
        source.Should().Contain("_decreaseFontSizeButton.Click += DecreaseFontSizeButton_Click;");
        source.Should().Contain("_increaseFontSizeButton.IsEnabled = isIdle;");
        source.Should().Contain("_decreaseFontSizeButton.IsEnabled = isIdle;");
        source.Should().Contain("private void IncreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseSelectedRangeFontSize()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeFontSize();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Font Size failed.\");");
        source.Should().Contain("RefreshShell($\"Increased font size for {rangeReference}\");");
        source.Should().Contain("private void DecreaseSelectedRangeFontSize()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeFontSize();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Font Size failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased font size for {rangeReference}\");");
        source.Should().Contain("var fontSize = (style?.FontSize ?? CellStyle.Default.FontSize) + WorksheetFontSizeDisplayOffset;");
        source.Should().Contain("var scaledFontSize = Math.Max(1, fontSize * zoomFactor);");
        source.Should().Contain("FontSize = adjustedFontSize,");
    }

    [Fact]
    public void MainWindow_WiresFillAndFontColorThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var paletteSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "RibbonColorPaletteFlyout.cs"));

        source.Should().NotContain("DefaultFillColor");
        source.Should().NotContain("DefaultFontColor");
        source.Should().Contain("private enum ColorPaletteTarget");
        source.Should().Contain("private readonly DropDownButton _fillColorButton = new();");
        source.Should().Contain("private readonly DropDownButton _fontColorButton = new();");
        source.Should().Contain("_fillColorButton.Content = \"Fill\";");
        source.Should().Contain("_fontColorButton.Content = \"A\";");
        source.Should().Contain("_fillColorButton.Flyout = CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);");
        source.Should().Contain("_fontColorButton.Flyout = CreateColorPaletteFlyout(ColorPaletteTarget.Font, includeClearFill: false);");
        source.Should().Contain("_fillColorButton.IsEnabled = isIdle;");
        source.Should().Contain("_fontColorButton.IsEnabled = isIdle;");
        source.Should().Contain("private Flyout CreateColorPaletteFlyout(ColorPaletteTarget target, bool includeClearFill)");
        source.Should().Contain("return RibbonColorPaletteFlyout.Create(");
        paletteSource.Should().Contain("CellColorPalettePlanner.BuildMenuPlan(");
        paletteSource.Should().Contain("CreateThemeGrid(themeSection.ThemeColumns, Apply)");
        paletteSource.Should().Contain("CreateSwatchRow(standardSection.Swatches, Apply, \"RibbonStandardColor\")");
        source.Should().Contain("private NativeMenu CreateNativeColorPaletteMenu(ColorPaletteTarget target, bool includeClearFill)");
        source.Should().Contain("menu.Items.Add(CreateNativeColorSwatchMenuItem(swatch, target));");
        source.Should().Contain("private NativeMenuItem CreateNativeColorSwatchMenuItem(CellColorSwatch swatch, ColorPaletteTarget target)");
        source.Should().Contain("private void ApplySelectedRangePaletteColor(CellColor color, ColorPaletteTarget target)");
        source.Should().Contain("case ColorPaletteTarget.Fill:");
        source.Should().Contain("ApplySelectedRangeFillColor(color);");
        source.Should().Contain("case ColorPaletteTarget.Font:");
        source.Should().Contain("ApplySelectedRangeFontColor(color);");
        source.Should().Contain("private static Border CreateColorSwatchIcon(CellColor color)");
        source.Should().Contain("private void ApplySelectedRangeFillColor(CellColor fillColor)");
        source.Should().Contain("var result = _session.SetSelectedRangeFillColor(fillColor);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Fill Color failed.\");");
        source.Should().Contain("RefreshShell($\"Applied fill color to {rangeReference}\");");
        source.Should().Contain("private void ClearSelectedRangeFill()");
        source.Should().Contain("var result = _session.ClearSelectedRangeFill();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"No Fill failed.\");");
        source.Should().Contain("RefreshShell($\"Cleared fill from {rangeReference}\");");
        source.Should().Contain("private void ApplySelectedRangeFontColor(CellColor fontColor)");
        source.Should().Contain("var result = _session.SetSelectedRangeFontColor(fontColor);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Font Color failed.\");");
        source.Should().Contain("RefreshShell($\"Applied font color to {rangeReference}\");");
        source.Should().Contain("var nativeFillColorSwatchCount = CountNativeColorPaletteSwatches(_fillColorMenuItem.Menu);");
        source.Should().Contain("var nativeFontColorSwatchCount = CountNativeColorPaletteSwatches(_fontColorMenuItem.Menu);");
        source.Should().Contain("private static int CountNativeColorPaletteSwatches(NativeMenu? menu)");
        source.Should().Contain("else if (style?.ResolveFillColor(_session.Workbook.Theme) is { } fillColor)");
        source.Should().Contain(": Brush(style.ResolveFontColor(_session.Workbook.Theme));");
    }

    [Fact]
    public void MainWindow_WiresBordersThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("private readonly DropDownButton _bordersButton = new();");
        source.Should().Contain("private readonly NativeMenuItem _bordersMenuItem = new();");
        source.Should().Contain("_bordersButton.Content = \"Borders\";");
        source.Should().Contain("_bordersButton.Flyout = CreateBorderPresetFlyout();");
        source.Should().Contain("AutomationProperties.SetAutomationId(_bordersButton, \"HomeBordersButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_bordersButton, \"Apply or change borders on the selected cells.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.Borders, \"Borders\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_bordersMenuItem.Menu = CreateNativeBorderPresetMenu();");
        source.Should().Contain("NativeMenuItemId.Borders => _bordersMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Borders)");
        source.Should().Contain("_bordersButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.Borders, context.CanBorders)");
        source.Should().Contain("_bordersButton,");
        source.Should().Contain("private MenuFlyout CreateBorderPresetFlyout()");
        source.Should().Contain(".GetValues<CellBorderPreset>()");
        source.Should().Contain(".Select(CreateBorderPresetMenuItem)");
        source.Should().Contain("private MenuItem CreateBorderPresetMenuItem(CellBorderPreset preset)");
        source.Should().Contain("CellBorderPresetPlanner.GetDisplayName(preset)");
        source.Should().Contain("AutomationProperties.SetAutomationId(menuItem, $\"HomeBorders{preset}MenuItem\");");
        source.Should().Contain("private NativeMenu CreateNativeBorderPresetMenu()");
        source.Should().Contain("menu.Items.Add(CreateNativeBorderPresetMenuItem(preset));");
        source.Should().Contain("private NativeMenuItem CreateNativeBorderPresetMenuItem(CellBorderPreset preset)");
        source.Should().Contain("ApplySelectedRangeBorderPreset(preset);");
        source.Should().Contain("private void ApplySelectedRangeBorderPreset(CellBorderPreset preset)");
        source.Should().Contain("var result = _session.ApplySelectedRangeCompactFormat(");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Borders failed.\");");
        source.Should().Contain("RefreshShell($\"Applied {presetName} to {rangeReference}\");");
        source.Should().Contain("var nativeBordersPresetCount = _bordersMenuItem.Menu?");
        source.Should().Contain("HasBordersButton: _bordersButton.Content?.ToString() == \"Borders\"");
        source.Should().Contain("HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, NativeMenuItemId.Borders)");
        source.Should().Contain("NativeBordersPresetCount: nativeBordersPresetCount");

        smokeSource.Should().Contain("bool HasBordersButton,");
        smokeSource.Should().Contain("bool HasNativeBordersMenuItem,");
        smokeSource.Should().Contain("int NativeBordersPresetCount,");
        smokeSource.Should().Contain("HasBordersButton &&");
        smokeSource.Should().Contain("HasNativeBordersMenuItem &&");
        smokeSource.Should().Contain("NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length");
        smokeSource.Should().Contain("toolbar_borders_button={FormatBool(snapshot.HasBordersButton)}");
        smokeSource.Should().Contain("native_borders_menu_item={FormatBool(snapshot.HasNativeBordersMenuItem)}");
        smokeSource.Should().Contain("native_borders_preset_count={snapshot.NativeBordersPresetCount}");
    }

    [Fact]
    public void MainWindow_WiresMergeAndCenterThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("private readonly Button _mergeAndCenterButton = new();");
        source.Should().Contain("private readonly NativeMenuItem _mergeAndCenterMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _unmergeCellsMenuItem = new();");
        source.Should().Contain("_mergeAndCenterButton.Content = \"Merge & Center\";");
        source.Should().Contain("_mergeAndCenterButton.Click += MergeAndCenterButton_Click;");
        source.Should().Contain("AutomationProperties.SetAutomationId(_mergeAndCenterButton, \"HomeMergeAndCenterButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_mergeAndCenterButton, \"Merge and center the selected cells.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.MergeAndCenter, \"Merge & Center\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_mergeAndCenterMenuItem.Click += async (_, _) => await MergeAndCenterSelectedRangeAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.UnmergeCells, \"Unmerge Cells\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();");
        source.Should().Contain("NativeMenuItemId.MergeAndCenter => _mergeAndCenterMenuItem,");
        source.Should().Contain("NativeMenuItemId.UnmergeCells => _unmergeCellsMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.MergeAndCenter)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.UnmergeCells)");
        source.Should().Contain("_mergeAndCenterButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.MergeAndCenter, context.CanMergeAndCenter)");
        catalogSource.Should().Contain("new(NativeMenuItemId.UnmergeCells, context.IsIdle && context.IsSelectedRangeMerged)");
        source.Should().Contain("_mergeAndCenterButton,");
        source.Should().Contain("private async void MergeAndCenterButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private async Task MergeAndCenterSelectedRangeAsync()");
        // R127-avalonia-mainwindow-multiarea-2: the content-loss analysis must cover every disjoint
        // Ctrl+click area (`areas`, resolved via SelectionStyleCommandPlanner.ResolveRanges) the merge
        // will actually touch, not just the single active `range`.
        source.Should().Contain("var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);");
        // R128-avalonia-mainwindow-groupedsheet-merge-1: the analysis must now be widened on BOTH
        // axes the execution was widened on -- every disjoint Ctrl+click area AND every grouped-edit
        // sheet the merge fans out to. AnalyzeGroupedSheetMergeContent remaps `areas` onto each
        // grouped sheet and unions the result; the older single-sheet
        // CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas) covered only the active sheet
        // and let a grouped sheet's content be merged away with no warning.
        source.Should().Contain("AnalyzeGroupedSheetMergeContent(areas)");
        source.Should().NotContain("CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas)");
        source.Should().Contain("await ShowMergeCellsContentWarningDialogAsync(contentPlan)");
        source.Should().Contain("var result = _session.MergeAndCenterSelectedRange(contentResolution);");
        source.Should().Contain("private async Task<MergeCellsWarningChoice> ShowMergeCellsContentWarningDialogAsync(MergeCellContentPlan contentPlan)");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"MergeCellsContentWarningDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepFirstButton, \"MergeCellsKeepFirstButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(concatenateButton, \"MergeCellsConcatenateButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"MergeCellsCancelButton\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Merge & Center failed.\");");
        source.Should().Contain("RefreshShell(isUnmergeToggle");
        source.Should().Contain("? $\"Unmerged cells in {rangeReference}\"");
        source.Should().Contain(": $\"Merged and centered {rangeReference}\");");
        source.Should().Contain("private void UnmergeSelectedRange()");
        source.Should().Contain("var result = _session.UnmergeSelectedRange();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Unmerge Cells failed.\");");
        source.Should().Contain("RefreshShell($\"Unmerged cells in {rangeReference}\");");
        source.Should().Contain("HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == \"Merge & Center\"");
        source.Should().Contain("HomeMergeAndCenterButton");
        source.Should().Contain("HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, NativeMenuItemId.MergeAndCenter)");
        source.Should().Contain("HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, NativeMenuItemId.UnmergeCells)");

        smokeSource.Should().Contain("bool HasMergeAndCenterButton,");
        smokeSource.Should().Contain("bool HasNativeMergeAndCenterMenuItem,");
        smokeSource.Should().Contain("bool HasNativeUnmergeCellsMenuItem,");
        smokeSource.Should().Contain("HasMergeAndCenterButton &&");
        smokeSource.Should().Contain("HasNativeMergeAndCenterMenuItem &&");
        smokeSource.Should().Contain("HasNativeUnmergeCellsMenuItem &&");
        smokeSource.Should().Contain("toolbar_merge_and_center_button={FormatBool(snapshot.HasMergeAndCenterButton)}");
        smokeSource.Should().Contain("native_merge_and_center_menu_item={FormatBool(snapshot.HasNativeMergeAndCenterMenuItem)}");
        smokeSource.Should().Contain("native_unmerge_cells_menu_item={FormatBool(snapshot.HasNativeUnmergeCellsMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresCellStylesThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("private readonly DropDownButton _cellStylesButton = new();");
        source.Should().Contain("private readonly NativeMenuItem _cellStylesMenuItem = new();");
        source.Should().Contain("_cellStylesButton.Content = \"Styles\";");
        source.Should().Contain("_cellStylesButton.Flyout = CreateCellStylesFlyout();");
        catalogSource.Should().Contain("new(NativeMenuItemId.CellStyles, \"Cell Styles\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_cellStylesMenuItem.Menu = CreateNativeCellStylesMenu();");
        source.Should().Contain("NativeMenuItemId.CellStyles => _cellStylesMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.CellStyles)");
        source.Should().Contain("_cellStylesButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.CellStyles, context.CanCellStyles)");
        source.Should().Contain("private MenuFlyout CreateCellStylesFlyout()");
        source.Should().Contain("Enum");
        source.Should().Contain(".GetValues<CellStylePreset>()");
        source.Should().Contain(".Select(CreateCellStyleMenuItem)");
        source.Should().Contain("private NativeMenu CreateNativeCellStylesMenu()");
        source.Should().Contain("menu.Items.Add(CreateNativeCellStyleMenuItem(preset));");
        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset)");
        source.Should().Contain("ApplySelectedRangeCellStylePreset(preset);");
        source.Should().Contain("private void ApplySelectedRangeCellStylePreset(CellStylePreset preset)");
        source.Should().Contain("var result = _session.SetSelectedRangeCellStylePreset(preset);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Cell Style failed.\");");
        source.Should().Contain("RefreshShell($\"Applied {presetName} style to {rangeReference}\");");
        source.Should().Contain("var nativeCellStylesPresetCount = _cellStylesMenuItem.Menu?");
        source.Should().Contain("HasNativeCellStylesMenuItem: HasNativeMenuItem(_cellStylesMenuItem, NativeMenuItemId.CellStyles)");
        source.Should().Contain("NativeCellStylesPresetCount: nativeCellStylesPresetCount");

        smokeSource.Should().Contain("bool HasNativeCellStylesMenuItem,");
        smokeSource.Should().Contain("int NativeCellStylesPresetCount,");
        smokeSource.Should().Contain("HasNativeCellStylesMenuItem &&");
        smokeSource.Should().Contain("NativeCellStylesPresetCount == Enum.GetValues<CellStylePreset>().Length");
        smokeSource.Should().Contain("native_cell_styles_menu_item={FormatBool(snapshot.HasNativeCellStylesMenuItem)}");
        smokeSource.Should().Contain("native_cell_styles_preset_count={snapshot.NativeCellStylesPresetCount}");
    }

    [Fact]
    public void MainWindow_WiresTextRotationThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("private readonly NativeMenuItem _horizontalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleCounterclockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleClockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _verticalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextUpMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextDownMenuItem = new();");
        source.Should().Contain("private readonly DropDownButton _orientationButton = new();");
        source.Should().Contain("_orientationButton.Content = \"Orient\";");
        source.Should().Contain("_orientationButton.Flyout = CreateTextRotationFlyout();");
        source.Should().Contain("_orientationButton.IsEnabled = isIdle;");
        source.Should().Contain("_orientationButton,");
        catalogSource.Should().Contain("new(NativeMenuItemId.HorizontalText, \"Horizontal\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(0, \"Set horizontal text for\", \"Horizontal Text failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.AngleCounterclockwise, \"Angle Counterclockwise\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.AngleClockwise, \"Angle Clockwise\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(-45, \"Angled text clockwise for\", \"Angle Clockwise failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.VerticalText, \"Vertical Text\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(255, \"Set vertical text for\", \"Vertical Text failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.RotateTextUp, \"Rotate Text Up\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(90, \"Rotated text up for\", \"Rotate Text Up failed.\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.RotateTextDown, \"Rotate Text Down\", RequiresGestureInSmoke: false)");
        source.Should().Contain("ApplySelectedRangeTextRotation(-90, \"Rotated text down for\", \"Rotate Text Down failed.\");");
        source.Should().Contain("private void ApplySelectedRangeTextRotation(int textRotation, string successAction, string failureMessage)");
        source.Should().Contain("var result = _session.SetSelectedRangeTextRotation(textRotation);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? failureMessage);");
        source.Should().Contain("RefreshShell($\"{successAction} {rangeReference}\");");
        source.Should().Contain("var textRotation = style?.TextRotation ?? CellStyle.Default.TextRotation;");
        source.Should().Contain("using FreeX.Core.Calc;");
        // The grid builder now resolves merge spans before creating the cell control (H29):
        // CreateCell carries the merge region and the control is added after span assignment.
        source.Should().Contain("var cellControl = CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight, mergeRegion);");
        source.Should().Contain("AddGridChild(grid, cellControl, rowIndex + headerOffset, colIndex + headerOffset);");
        source.Should().Contain("CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation)");
        source.Should().Contain("CreateOrientedCellContent(");
        source.Should().Contain("CellTextOrientationLayoutPlanner.CalculateLayout(");
        source.Should().Contain("Canvas.SetLeft(textBlock, layout.TextPoint.X);");
        source.Should().Contain("Canvas.SetTop(textBlock, layout.TextPoint.Y);");
        source.Should().Contain("private static string FormatTextForRotation(string text, int textRotation)");
        source.Should().Contain("CellTextOrientationLayoutPlanner.PrepareDisplayText(text, textRotation)");
        source.Should().Contain("private static int NormalizeTextRotationForDisplay(int textRotation)");
        source.Should().Contain("CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(textRotation)");
        source.Should().Contain("CreateTextRotationTransform(layout.TransformAngle)");
        source.Should().Contain("private static RotateTransform? CreateTextRotationTransform(double transformAngle)");
        source.Should().Contain("Math.Abs(transformAngle) <= 0.001 ? null : new RotateTransform(transformAngle);");
        source.Should().Contain("textBlock.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);");
        source.Should().Contain("textBlock.RenderTransform = transform;");
        source.Should().Contain("ClipToBounds = true,");
        source.Should().Contain("private MenuFlyout CreateTextRotationFlyout()");
        source.Should().Contain("CreateTextRotationMenuItem(\"Horizontal\", 0, \"Set horizontal text for\", \"Horizontal Text failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Angle Counterclockwise\", 45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Angle Clockwise\", -45, \"Angled text clockwise for\", \"Angle Clockwise failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Vertical Text\", 255, \"Set vertical text for\", \"Vertical Text failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Rotate Text Up\", 90, \"Rotated text up for\", \"Rotate Text Up failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Rotate Text Down\", -90, \"Rotated text down for\", \"Rotate Text Down failed.\")");
        source.Should().Contain("private MenuItem CreateTextRotationMenuItem(");
        source.Should().Contain("menuItem.Click += (_, _) => ApplySelectedRangeTextRotation(textRotation, successAction, failureMessage);");
    }

    [Fact]
    public void MainWindow_WiresNumberFormatsThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const string CurrencyNumberFormat = \"$#,##0.00\";");
        source.Should().Contain("private const string PercentNumberFormat = \"0%\";");
        source.Should().Contain("private const string CommaNumberFormat = \"#,##0.00\";");
        source.Should().Contain("private readonly Button _currencyFormatButton = new();");
        source.Should().Contain("private readonly Button _percentFormatButton = new();");
        source.Should().Contain("private readonly Button _commaStyleButton = new();");
        source.Should().Contain("private readonly Button _increaseDecimalButton = new();");
        source.Should().Contain("private readonly Button _decreaseDecimalButton = new();");
        source.Should().Contain("_currencyFormatButton.Content = \"$\";");
        source.Should().Contain("_percentFormatButton.Content = \"%\";");
        source.Should().Contain("_commaStyleButton.Content = \",\";");
        source.Should().Contain("_increaseDecimalButton.Content = \"+.0\";");
        source.Should().Contain("_decreaseDecimalButton.Content = \"-.0\";");
        source.Should().Contain("_currencyFormatButton.Click += CurrencyFormatButton_Click;");
        source.Should().Contain("_percentFormatButton.Click += PercentFormatButton_Click;");
        source.Should().Contain("_commaStyleButton.Click += CommaStyleButton_Click;");
        source.Should().Contain("_increaseDecimalButton.Click += IncreaseDecimalButton_Click;");
        source.Should().Contain("_decreaseDecimalButton.Click += DecreaseDecimalButton_Click;");
        source.Should().Contain("_currencyFormatButton.IsEnabled = isIdle;");
        source.Should().Contain("_percentFormatButton.IsEnabled = isIdle;");
        source.Should().Contain("_commaStyleButton.IsEnabled = isIdle;");
        source.Should().Contain("_increaseDecimalButton.IsEnabled = isIdle;");
        source.Should().Contain("_decreaseDecimalButton.IsEnabled = isIdle;");
        source.Should().Contain("private void CurrencyFormatButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void PercentFormatButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void CommaStyleButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseDecimalButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseDecimalButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void ApplySelectedRangeCurrencyFormat()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(CurrencyNumberFormat, \"Applied currency format to\", \"Currency format failed.\");");
        source.Should().Contain("private void ApplySelectedRangePercentFormat()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(PercentNumberFormat, \"Applied percent format to\", \"Percent format failed.\");");
        source.Should().Contain("private void ApplySelectedRangeCommaStyle()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(CommaNumberFormat, \"Applied comma style to\", \"Comma style failed.\");");
        source.Should().Contain("private void ApplySelectedRangeNumberFormat(string numberFormat, string successAction, string failureMessage)");
        source.Should().Contain("var result = _session.SetSelectedRangeNumberFormat(numberFormat);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? failureMessage);");
        source.Should().Contain("RefreshShell($\"{successAction} {rangeReference}\");");
        source.Should().Contain("private void IncreaseSelectedRangeDecimalPlaces()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Decimal failed.\");");
        source.Should().Contain("RefreshShell($\"Increased decimals for {rangeReference}\");");
        source.Should().Contain("private void DecreaseSelectedRangeDecimalPlaces()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Decimal failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased decimals for {rangeReference}\");");
        source.Should().Contain("cell.DisplayText,");
    }

    [Fact]
    public void MainWindow_WiresWrapTextThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _wrapTextButton = new();");
        source.Should().Contain("_wrapTextButton.Content = \"Wrap\";");
        source.Should().Contain("_wrapTextButton.Click += WrapTextButton_Click;");
        source.Should().Contain("AutomationProperties.SetAutomationId(_wrapTextButton, \"HomeWrapTextButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_wrapTextButton, \"Wrap text within the selected cells.\");");
        source.Should().Contain("_wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;");
        source.Should().Contain("_wrapTextButton.IsEnabled = isIdle;");
        source.Should().Contain("private void WrapTextButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeWrapText(_wrapTextButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeWrapText()");
        source.Should().Contain("ApplySelectedRangeWrapText(!_session.IsSelectedRangeStartWrapText);");
        source.Should().Contain("private void ApplySelectedRangeWrapText(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeWrapText(enabled);");
        source.Should().Contain("_wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Wrap Text failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Wrapped\" : \"Unwrapped\")} {rangeReference}\");");
        source.Should().Contain("var textWrapping = style?.WrapText == true ? TextWrapping.Wrap : TextWrapping.NoWrap;");
        source.Should().Contain("var effectiveTextWrapping = textRotation == 255 ? TextWrapping.NoWrap : textWrapping;");
        source.Should().Contain("TextWrapping = isFillAlign ? TextWrapping.NoWrap : effectiveTextWrapping,");
        source.Should().Contain("TextTrimming = TextTrimming.None,");
        source.Should().Contain("AddCellTextOverflowOverlayToGrid(");
        source.Should().Contain("CellTextOverflowPlanner.CanOverflowCellText(");
    }

    [Fact]
    public void MainWindow_WiresIndentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const double CellIndentLevelWidth = 12;");
        source.Should().Contain("private readonly Button _decreaseIndentButton = new();");
        source.Should().Contain("private readonly Button _increaseIndentButton = new();");
        source.Should().Contain("_decreaseIndentButton.Content = \"Out\";");
        source.Should().Contain("_increaseIndentButton.Content = \"In\";");
        source.Should().Contain("_decreaseIndentButton.Click += DecreaseIndentButton_Click;");
        source.Should().Contain("_increaseIndentButton.Click += IncreaseIndentButton_Click;");
        source.Should().Contain("_decreaseIndentButton.IsEnabled = isIdle;");
        source.Should().Contain("_increaseIndentButton.IsEnabled = isIdle;");
        source.Should().Contain("private void DecreaseIndentButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseIndentButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseSelectedRangeIndent()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeIndent();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Indent failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased indent for {rangeReference}\");");
        source.Should().Contain("private void IncreaseSelectedRangeIndent()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeIndent();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Indent failed.\");");
        source.Should().Contain("RefreshShell($\"Increased indent for {rangeReference}\");");
        source.Should().Contain("var indentPadding = GetCellIndentPadding(style) + GetPivotRowLabelTextPadding(address.Row, address.Col);");
        source.Should().Contain("private static double GetCellIndentPadding(CellStyle? style)");
        source.Should().Contain("Math.Clamp(style.IndentLevel, 0, 15) * CellIndentLevelWidth;");
        // The indent insets from the side the text is anchored to: for a right-anchored (right-aligned
        // or RTL) cell the indent is added to the RIGHT margin, otherwise to the left.
        source.Should().Contain("scaledHorizontalPadding + (isRightAnchored ? 0 : scaledIndentPadding),");
        source.Should().Contain("scaledHorizontalPadding + (isRightAnchored ? scaledIndentPadding : 0),");
    }

    [Fact]
    public void MainWindow_WiresVerticalAlignmentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("using CellVAlign = FreeX.Core.Model.VerticalAlignment;");
        source.Should().Contain("private readonly ToggleButton _alignTopButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignMiddleButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignBottomButton = new();");
        source.Should().Contain("_alignTopButton.Content = \"Top\";");
        source.Should().Contain("_alignMiddleButton.Content = \"Mid\";");
        source.Should().Contain("_alignBottomButton.Content = \"Bot\";");
        source.Should().Contain("_alignTopButton.Click += AlignTopButton_Click;");
        source.Should().Contain("_alignMiddleButton.Click += AlignMiddleButton_Click;");
        source.Should().Contain("_alignBottomButton.Click += AlignBottomButton_Click;");
        source.Should().Contain("_alignTopButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Top;");
        source.Should().Contain("_alignMiddleButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Center;");
        source.Should().Contain("_alignBottomButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Bottom;");
        source.Should().Contain("_alignTopButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignMiddleButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignBottomButton.IsEnabled = isIdle;");
        source.Should().Contain("private void AlignTopButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Top);");
        source.Should().Contain("private void AlignMiddleButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Center);");
        source.Should().Contain("private void AlignBottomButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);");
        source.Should().Contain("private void ApplySelectedRangeVerticalAlignment(CellVAlign alignment)");
        source.Should().Contain("var result = _session.SetSelectedRangeVerticalAlignment(alignment);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Vertical alignment failed.\");");
        source.Should().Contain("RefreshShell($\"Aligned {rangeReference} {FormatVerticalAlignmentStatus(alignment)}\");");
        source.Should().Contain("var verticalAlignmentModel = style?.VerticalAlignment ?? CellVAlign.Bottom;");
        source.Should().Contain("var verticalAlignment = MapCellVerticalAlignment(verticalAlignmentModel);");
        source.Should().Contain("verticalAlignmentModel,");
        source.Should().Contain("private static AvaloniaVerticalAlignment MapCellVerticalAlignment(CellVAlign verticalAlignment)");
        source.Should().Contain("CellVAlign.Top => AvaloniaVerticalAlignment.Top,");
        source.Should().Contain("CellVAlign.Bottom => AvaloniaVerticalAlignment.Bottom,");
        source.Should().Contain("_ => AvaloniaVerticalAlignment.Center");
        source.Should().Contain("VerticalAlignment = verticalAlignment,");
        source.Should().Contain("private static string FormatVerticalAlignmentStatus(CellVAlign alignment)");
    }

    [Fact]
    public void MainWindow_WiresHorizontalAlignmentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("using CellHAlign = FreeX.Core.Model.HorizontalAlignment;");
        source.Should().Contain("private readonly ToggleButton _alignLeftButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignCenterButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignRightButton = new();");
        source.Should().Contain("_alignLeftButton.Content = \"L\";");
        source.Should().Contain("_alignCenterButton.Content = \"C\";");
        source.Should().Contain("_alignRightButton.Content = \"R\";");
        source.Should().Contain("_alignLeftButton.Click += AlignLeftButton_Click;");
        source.Should().Contain("_alignCenterButton.Click += AlignCenterButton_Click;");
        source.Should().Contain("_alignRightButton.Click += AlignRightButton_Click;");
        source.Should().Contain("_alignLeftButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Left;");
        source.Should().Contain("_alignCenterButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Center;");
        source.Should().Contain("_alignRightButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Right;");
        source.Should().Contain("_alignLeftButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignCenterButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignRightButton.IsEnabled = isIdle;");
        source.Should().Contain("private void AlignLeftButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);");
        source.Should().Contain("private void AlignCenterButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);");
        source.Should().Contain("private void AlignRightButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);");
        source.Should().Contain("private void ApplySelectedRangeHorizontalAlignment(CellHAlign alignment)");
        source.Should().Contain("var result = _session.SetSelectedRangeHorizontalAlignment(alignment);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Alignment failed.\");");
        source.Should().Contain("RefreshShell($\"Aligned {rangeReference} {FormatHorizontalAlignmentStatus(alignment)}\");");
        source.Should().Contain("MapCellTextAlignment(");
        source.Should().Contain("style?.HorizontalAlignment ?? CellHAlign.General");
        source.Should().Contain("private static TextAlignment MapCellTextAlignment(CellHAlign horizontalAlignment, bool isNumericOrDate, bool isEffectivelyRightToLeft)");
        source.Should().Contain("CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,");
        // K33 (RTL): General alignment now mirrors Left/Right based on the cell's effective reading
        // order (CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft) instead of always
        // resolving numeric/date content to the right and text to the left.
        source.Should().Contain("CellHAlign.General when isNumericOrDate => isEffectivelyRightToLeft ? TextAlignment.Left : TextAlignment.Right,");
        source.Should().Contain("private static string FormatHorizontalAlignmentStatus(CellHAlign alignment)");
    }

    [Fact]
    public void MainWindow_WiresCompactFormatCellsRouteThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var fillEditorSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FormatCellsFillEditor.cs"));
        var parityCaptureSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "FormatCellsCompactPlanner.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n");
        var normalizedParityCaptureSource = parityCaptureSource.Replace("\r\n", "\n");

        source.Should().Contain("private readonly NativeMenuItem _formatCellsMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FormatCells, \"Format Cells...\", NativeMenuGesture(WorkbookShortcutRoute.OpenFormatCells))");
        source.Should().Contain("_formatCellsMenuItem.Click += async (_, _) => await ShowFormatCells");
        source.Should().Contain("NativeMenuItemId.FormatCells => _formatCellsMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.FormatCells)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FormatCells, context.IsIdle)");
        source.Should().Contain("Key.D1");
        source.Should().Contain("HasOnlyCommandModifier(e.KeyModifiers)");
        source.Should().Contain("await ShowFormatCells");
        source.Should().Contain("private async Task ShowFormatCells");
        source.Should().Contain("FormatCellsCompactPlanner.TryPlan");
        source.Should().Contain("_session.ApplySelectedRangeCompactFormat(");
        source.Should().Contain("\"FormatCellsCompactDialog\"");
        source.Should().Contain("\"FormatCellsNumberFormatBox\"");
        source.Should().Contain("\"FormatCellsHorizontalAlignmentBox\"");
        source.Should().Contain("\"FormatCellsVerticalAlignmentBox\"");
        source.Should().Contain("new(\"Justify\", CellHAlign.Justify)");
        source.Should().Contain("new(\"Distributed\", CellHAlign.Distributed)");
        source.Should().Contain("new(\"Justify\", CellVAlign.Justify)");
        source.Should().Contain("new(\"Distributed\", CellVAlign.Distributed)");
        source.Should().Contain("\"FormatCellsWrapTextBox\"");
        source.Should().Contain("\"FormatCellsMergeCellsBox\"");
        source.Should().Contain("\"FormatCellsFontSizeBox\"");
        source.Should().Contain("\"FormatCellsFontColorBox\"");
        fillEditorSource.Should().Contain("\"FormatCellsFillColorBox\"");
        fillEditorSource.Should().Contain("\"FormatCellsFillPatternStyleBox\"");
        fillEditorSource.Should().Contain("\"FormatCellsFillPatternColorBox\"");
        source.Should().Contain("\"FormatCellsBorderPresetBox\"");
        source.Should().Contain("\"FormatCellsBorderStyleBox\"");
        source.Should().Contain("\"FormatCellsBorderColorBox\"");
        source.Should().Contain("\"FormatCellsBorderTopToggle\"");
        source.Should().Contain("\"FormatCellsBorderBottomToggle\"");
        source.Should().Contain("\"FormatCellsBorderLeftToggle\"");
        source.Should().Contain("\"FormatCellsBorderRightToggle\"");
        source.Should().Contain("\"FormatCellsBorderInsideHorizontalToggle\"");
        source.Should().Contain("\"FormatCellsBorderInsideVerticalToggle\"");
        source.Should().Contain("\"FormatCellsBorderPreview\"");
        // Per-edge "Individual border details": each outer edge has its OWN style box + color picker.
        source.Should().Contain("\"FormatCellsBorderTopStyleBox\"");
        source.Should().Contain("\"FormatCellsBorderTopColorBox\"");
        source.Should().Contain("\"FormatCellsBorderRightStyleBox\"");
        source.Should().Contain("\"FormatCellsBorderRightColorBox\"");
        source.Should().Contain("\"FormatCellsBorderBottomStyleBox\"");
        source.Should().Contain("\"FormatCellsBorderBottomColorBox\"");
        source.Should().Contain("\"FormatCellsBorderLeftStyleBox\"");
        source.Should().Contain("\"FormatCellsBorderLeftColorBox\"");
        source.Should().Contain("\"FormatCellsFontPreview\"");
        fillEditorSource.Should().Contain("\"FormatCellsFillSamplePreview\"");
        source.Should().Contain("\"FormatCellsBorderPresetNoneButton\"");
        source.Should().Contain("\"FormatCellsBorderPresetOutlineButton\"");
        source.Should().Contain("\"FormatCellsBorderPresetInsideButton\"");
        // Border tab rebuilt to match Windows: the line style is a scrollable textual list
        // (keeps the FormatCellsBorderStyleBox automation id), the preset buttons,
        // toggles and preview are arranged into Presets / Line / Border groups, plus an
        // "Individual border details" section. Verify the rebuilt structure is present.
        source.Should().Contain("CreateFormatCellsBorderStyleListBox(");
        source.Should().Contain("CreateFormatCellsBorderGroup(");
        source.Should().Contain("UiText.Get(\"FormatCells_Presets\")");
        source.Should().Contain("UiText.Get(\"FormatCells_Line\")");
        source.Should().Contain("UiText.Get(\"FormatCells_Border\")");
        source.Should().Contain("UiText.Get(\"FormatCells_IndividualBorderDetails\")");
        source.Should().Contain("ConfigureCompactPickButton();");
        source.Should().Contain("FormatCellsBorderColorTextBox");
        source.Should().Contain("CreateBorderPalette(borderColorBox)");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"122,190,*\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"80,*,*\")");
        source.Should().Contain("ShowFormatCellsError(message)");
        normalizedSource.Should().Contain(
            "ItemsSource = new[]\n" +
            "            {\n" +
            "                numberTab,\n" +
            "                alignmentTab,\n" +
            "                fontTab,\n" +
            "                borderTab,\n" +
            "                fillTab,\n" +
            "                protectionTab,\n" +
            "            }");
        normalizedParityCaptureSource.Should().Contain("[\"Number\", \"Alignment\", \"Font\", \"Border\", \"Fill\", \"Protection\"]");
        // Font tab: Font name + size are now selectable lists driving the existing boxes.
        source.Should().Contain("\"FormatCellsFontNameList\"");
        source.Should().Contain("\"FormatCellsFontSizeList\"");
        source.Should().Contain("BorderTop: borderTopSide");
        source.Should().Contain("BorderRight: borderRightSide");
        source.Should().Contain("BorderBottom: borderBottomSide");
        source.Should().Contain("BorderLeft: borderLeftSide");
        // Each outer edge reads ITS OWN per-edge style box + color picker (not the shared Line),
        // diffed against the value the dialog seeded from the cell's current border.
        source.Should().Contain("var borderTopSide = ReadBorderSide(borderTopStyleBox, borderTopColorBox, seededTopBorder);");
        source.Should().Contain("var borderRightSide = ReadBorderSide(borderRightStyleBox, borderRightColorBox, seededRightBorder);");
        source.Should().Contain("var borderBottomSide = ReadBorderSide(borderBottomStyleBox, borderBottomColorBox, seededBottomBorder);");
        source.Should().Contain("var borderLeftSide = ReadBorderSide(borderLeftStyleBox, borderLeftColorBox, seededLeftBorder);");
        // The dialog seeds per-edge controls from the active cell's existing borders (WPF-parity)…
        source.Should().Contain("SeedBorderEdge(borderTopStyleBox, borderTopColorBox, borderTopToggle, currentStyle.BorderTop);");
        // …and an edge still at its seeded value reads back as "no change"; an edit applies style+color.
        source.Should().Contain("CellBorder? ReadBorderSide(ComboBox styleBox, FormatCellsColorPicker colorBox, (BorderStyle? Style, CellColor Color) seeded)");
        source.Should().Contain("if (style == seeded.Style && (style is null || color == seeded.Color))");
        source.Should().Contain("\"FormatCellsDoubleUnderlineBox\"");
        source.Should().Contain("\"FormatCellsShrinkToFitBox\"");
        source.Should().Contain("\"FormatCellsIndentLevelBox\"");
        source.Should().Contain("\"FormatCellsTextRotationBox\"");
        source.Should().Contain("\"FormatCellsFontNameBox\"");
        source.Should().Contain("\"FormatCellsNormalFontBox\"");
        source.Should().Contain("\"FormatCellsSuperscriptBox\"");
        source.Should().Contain("\"FormatCellsSubscriptBox\"");
        source.Should().Contain("\"FormatCellsLockedBox\"");
        source.Should().Contain("\"FormatCellsHiddenBox\"");
        source.Should().Contain("\"FormatCellsProtectionExplanationText\"");
        // The protection-explanation text is now localized: it lives in the shared Strings.resx
        // catalog and is routed through UiText, so the source references the key, not the literal.
        source.Should().Contain("UiText.Get(\"FormatCells_ProtectionExplanation\")");
        source.Should().Contain("var currentMergeCells = _session.IsSelectedRangeMerged;");
        source.Should().Contain("var currentUnderline = currentStyle.Underline ?? CellStyle.Default.Underline;");
        source.Should().Contain("Underline: normalFont ? normalStyle.Underline : ReadChangedFormatCellsBool(currentUnderline, underlineBox)");
        source.Should().Contain("var normalStyle = CellStyle.Default;");
        source.Should().Contain("Bold: normalFont ? normalStyle.Bold : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartBold, boldBox)");
        source.Should().Contain("FontName: normalFont ? normalStyle.FontName : ReadChangedFormatCellsText(currentFontName, fontNameBox)");
        source.Should().Contain("FontColor: normalFont ? normalStyle.FontColor : (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color");
        source.Should().Contain("SelectFormatCellsColor(fontColorBox, normal.FontColor)");
        source.Should().Contain("MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox)");
        source.Should().Contain("FillPatternStyle: clearFill ? null : ReadChangedFormatCellsValue(currentFillStyle.FillPatternStyle, fillPatternStyleBox)");
        source.Should().Contain("FillPatternColor: clearFill ? null : fillEditor.PatternColor");
        source.Should().Contain("selection.Request.MergeCells");
        source.Should().Contain("var mergeContentResolution = MergeCellContentResolution.KeepFirstCell;");
        source.Should().Contain("if (selection.Request.MergeCells == true)");
        // R128-avalonia-formatcells-groupedsheet-merge-1: the Format Cells "Merge cells" checkbox
        // fans its merge across every disjoint Ctrl+click area AND every grouped-edit sheet
        // (CreateFormatCellsMergeCommands loops CurrentGroupedEditSheetIds), so its content-loss
        // warning must be widened on both axes too. The older single-sheet, single-range
        // CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range) was narrower than the
        // operation it gated.
        source.Should().Contain("AnalyzeGroupedSheetMergeContent(areas)");
        source.Should().NotContain("CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range)");
        source.Should().Contain("await ShowMergeCellsContentWarningDialogAsync(contentPlan)");
        source.Should().Contain("selection.BorderStyle");
        source.Should().Contain("selection.BorderColor");

        sessionSource.Should().Contain("public WorkbookCellEditResult ApplySelectedRangeCompactFormat(");
        sessionSource.Should().Contain("BorderStyle borderStyle = BorderStyle.Thin");
        sessionSource.Should().Contain("CellColor? borderColor = null");
        sessionSource.Should().Contain("bool? mergeCells = null");
        sessionSource.Should().Contain("MergeCellContentResolution mergeContentResolution = MergeCellContentResolution.KeepFirstCell");
        // R128-services-multiarea-compactformat-1: ApplySelectedRangeCompactFormat now builds its
        // border-preset/merge commands per disjoint area of the selection (GetSelectionSizingRanges()),
        // not just the single active SelectedRange -- matching Excel's Ctrl+click multi-area formatting
        // and the already-fixed sibling ApplySelectedRangeStyle (R127-cellscmds-multiarea-style-1).
        sessionSource.Should().Contain("CreateBorderPresetCommand(area, preset, borderStyle, borderColor)");
        sessionSource.Should().Contain("CreateFormatCellsMergeCommands(area, shouldMerge, mergeContentResolution)");
        sessionSource.Should().Contain("GetSelectionSizingRanges()");
        sessionSource.Should().Contain("CellMergePlanner.CreateMergeCommands(");
        sessionSource.Should().Contain("CellMergePlanner.CreateMergeAndCenterCommands(");
        sessionSource.Should().Contain("CellBorderPresetPlanner.Plan(preset, range, address, borderStyle, borderColor)");
        plannerSource.Should().Contain("bool? MergeCells = null");
        plannerSource.Should().Contain("bool? DoubleUnderline = null");
        plannerSource.Should().Contain("bool? ShrinkToFit = null");
        plannerSource.Should().Contain("int? IndentLevel = null");
        plannerSource.Should().Contain("int? TextRotation = null");
        plannerSource.Should().Contain("string? FontName = null");
        plannerSource.Should().Contain("bool? Superscript = null");
        plannerSource.Should().Contain("bool? Subscript = null");
        plannerSource.Should().Contain("bool? Locked = null");
        plannerSource.Should().Contain("bool? Hidden = null");
        plannerSource.Should().Contain("CellFillPatternStyle? FillPatternStyle = null");
        plannerSource.Should().Contain("CellColor? FillPatternColor = null");
        plannerSource.Should().Contain("DoubleUnderline: request.DoubleUnderline");
        plannerSource.Should().Contain("ShrinkToFit: request.ShrinkToFit");
        plannerSource.Should().Contain("IndentLevel: NormalizeIndentLevel(request.IndentLevel)");
        plannerSource.Should().Contain("TextRotation: NormalizeTextRotation(request.TextRotation)");
        plannerSource.Should().Contain("FontName: NormalizeFontName(request.FontName)");
        plannerSource.Should().Contain("Superscript: request.Superscript");
        plannerSource.Should().Contain("Subscript: request.Subscript");
        plannerSource.Should().Contain("Locked: request.Locked");
        plannerSource.Should().Contain("Hidden: request.Hidden");
        plannerSource.Should().Contain("FillPatternStyle: request.ClearFill ? null : request.FillPatternStyle");
        plannerSource.Should().Contain("FillPatternColor: request.ClearFill ? null : request.FillPatternColor");
        plannerSource.Should().Contain("CellBorder? BorderTop = null");
        plannerSource.Should().Contain("CellBorder? BorderRight = null");
        plannerSource.Should().Contain("CellBorder? BorderBottom = null");
        plannerSource.Should().Contain("CellBorder? BorderLeft = null");
        plannerSource.Should().Contain("var borderTop = request.BorderTop ?? borderDiff?.BorderTop");
        plannerSource.Should().Contain("var borderRight = request.BorderRight ?? borderDiff?.BorderRight");
        plannerSource.Should().Contain("var borderBottom = request.BorderBottom ?? borderDiff?.BorderBottom");
        plannerSource.Should().Contain("var borderLeft = request.BorderLeft ?? borderDiff?.BorderLeft");
    }

    [Fact]
    public void MainWindow_RendersCellStyleBordersFromWorkbookStyles()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AddStyledCellBorderOverlay(content, style, borderNeighbors, zoomFactor);");
        source.Should().Contain("private static void AddStyledCellBorderOverlay(");
        source.Should().Contain("private static bool HasVisibleCellBorder(CellStyle? style)");
        source.Should().Contain("style.BorderTop.Style != BorderStyle.None");
        source.Should().Contain("style.BorderRight.Style != BorderStyle.None");
        source.Should().Contain("style.BorderBottom.Style != BorderStyle.None");
        source.Should().Contain("style.BorderLeft.Style != BorderStyle.None");
        source.Should().Contain("style.BorderDiagonalDown.Style != BorderStyle.None");
        source.Should().Contain("style.BorderDiagonalUp.Style != BorderStyle.None");
        // Border drawing is delegated to CellBorderPanel (stroked Lines with dash + per-style
        // thickness from portable CellBorderVisualPlanner), replacing the old inline
        // solid-Border-strip approach that could not dash and had wrong Hair/SlantDashDot thickness.
        // borderNeighbors carries the touching neighbor cells' opposing edge styles so a shared grid
        // edge resolves via CellBorderVisualPlanner.ResolveEdgeWinner (R66-render-gridlines-borders-6-1).
        source.Should().Contain("content.Children.Add(new CellBorderPanel(visibleStyle, borderNeighbors, zoomFactor));");
    }

    [Fact]
    public void MainWindow_WiresSheetLifecycleThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly Button _newSheetButton = new();");
        source.Should().Contain("private readonly NativeMenuItem _newSheetMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _renameSheetMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _duplicateSheetMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _moveSheetLeftMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _moveSheetRightMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _tabColorMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _selectAllSheetsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _ungroupSheetsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _hideSheetMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _unhideSheetMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _deleteSheetMenuItem = new();");
        source.Should().Contain("_newSheetButton.Content = \"+\";");
        source.Should().Contain("_newSheetButton.Click += (_, _) => AddNewSheet();");
        source.Should().Contain("AutomationProperties.SetName(_newSheetButton, \"New Sheet\");");
        catalogSource.Should().Contain("new(NativeMenuItemId.NewSheet, \"AvaloniaNativeMenu_NewSheet\", NativeMenuGesture(WorkbookShortcutRoute.InsertWorksheet), UsesResourceKey: true)");
        source.Should().Contain("_newSheetMenuItem.Click += (_, _) => AddNewSheet();");
        catalogSource.Should().Contain("new(NativeMenuItemId.RenameSheet, \"Rename Sheet...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_renameSheetMenuItem.Click += async (_, _) => await RenameActiveSheetAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DuplicateSheet, \"Duplicate Sheet\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_duplicateSheetMenuItem.Click += (_, _) => DuplicateActiveSheet();");
        catalogSource.Should().Contain("new(NativeMenuItemId.MoveSheetLeft, \"Move Sheet Left\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_moveSheetLeftMenuItem.Click += (_, _) => MoveActiveSheetLeft();");
        catalogSource.Should().Contain("new(NativeMenuItemId.MoveSheetRight, \"Move Sheet Right\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_moveSheetRightMenuItem.Click += (_, _) => MoveActiveSheetRight();");
        catalogSource.Should().Contain("new(NativeMenuItemId.TabColor, \"Tab Color\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_tabColorMenuItem.Menu = CreateNativeSheetTabColorMenu();");
        catalogSource.Should().Contain("new(NativeMenuItemId.SelectAllSheets, \"Select All Sheets\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_selectAllSheetsMenuItem.Click += (_, _) => SelectAllVisibleSheets();");
        catalogSource.Should().Contain("new(NativeMenuItemId.UngroupSheets, \"Ungroup Sheets\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_ungroupSheetsMenuItem.Click += (_, _) => UngroupSheets();");
        catalogSource.Should().Contain("new(NativeMenuItemId.HideSheet, \"Hide Sheet\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_hideSheetMenuItem.Click += (_, _) => HideActiveSheet();");
        catalogSource.Should().Contain("new(NativeMenuItemId.UnhideSheet, \"Unhide Sheet...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_unhideSheetMenuItem.Click += async (_, _) => await UnhideSheetAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.DeleteSheet, \"Delete Sheet\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_deleteSheetMenuItem.Click += (_, _) => DeleteActiveSheet();");
        source.Should().Contain("var sheetMenu = CreateNativeMenu(NativeMenuTopLevelId.Sheet);");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> SheetMenuEntries");
        catalogSource.Should().Contain("Item(NativeMenuItemId.NewSheet)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.RenameSheet)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.DuplicateSheet)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.MoveSheetLeft)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.MoveSheetRight)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.TabColor)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.SelectAllSheets)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.UngroupSheets)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.HideSheet)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.UnhideSheet)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.DeleteSheet)");
        catalogSource.Should().Contain("new(NativeMenuTopLevelId.Sheet, \"Sheet\")");
        source.Should().Contain("_newSheetButton.IsEnabled = isIdle;");
        source.Should().Contain("ApplyNativeMenuAvailability(isIdle);");
        catalogSource.Should().Contain("new(NativeMenuItemId.NewSheet, context.CanAddSheet)");
        catalogSource.Should().Contain("new(NativeMenuItemId.RenameSheet, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.DuplicateSheet, context.IsIdle)");
        source.Should().Contain("ActiveSheetTabIndex: FindActiveSheetTabIndex(),");
        catalogSource.Should().Contain("new(NativeMenuItemId.MoveSheetLeft, context.IsIdle && context.ActiveSheetTabIndex > 0)");
        source.Should().Contain("SheetTabCount: _session.SheetTabs.Count,");
        catalogSource.Should().Contain("NativeMenuItemId.MoveSheetRight");
        catalogSource.Should().Contain("context.ActiveSheetTabIndex < context.SheetTabCount - 1");
        catalogSource.Should().Contain("new(NativeMenuItemId.TabColor, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.SelectAllSheets, context.IsIdle && context.SheetTabCount > 1)");
        catalogSource.Should().Contain("new(NativeMenuItemId.UngroupSheets, context.IsIdle && context.IsWorkbookGrouped)");
        catalogSource.Should().Contain("new(NativeMenuItemId.HideSheet, context.IsIdle && context.CanHideActiveSheet)");
        catalogSource.Should().Contain("new(NativeMenuItemId.UnhideSheet, context.IsIdle && context.HiddenSheetCount > 0)");
        catalogSource.Should().Contain("new(NativeMenuItemId.DeleteSheet, context.IsIdle)");
        source.Should().Contain("private int FindActiveSheetTabIndex()");
        source.Should().Contain("private void AddNewSheet()");
        source.Should().Contain("var result = _session.AddSheet();");
        source.Should().Contain("RefreshShell($\"Inserted {_session.ActiveSheet.Name}\");");
        source.Should().Contain("private async Task RenameActiveSheetAsync()");
        source.Should().Contain("var newName = await ShowRenameSheetDialogAsync(currentName);");
        source.Should().Contain("var result = _session.RenameActiveSheet(newName);");
        source.Should().Contain("RefreshShell(string.Equals(currentName, _session.ActiveSheet.Name, StringComparison.Ordinal)");
        source.Should().Contain("private async Task<string?> ShowRenameSheetDialogAsync(string currentName)");
        source.Should().Contain("AutomationProperties.SetAutomationId(nameBox, \"RenameSheetNameBox\");");
        source.Should().Contain("var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);");
        source.Should().Contain("nameBox.SelectAll();");
        source.Should().Contain("private const string SheetTabContextHelpText = \"Selects this sheet. Press F6 repeatedly to reach sheet tabs, use arrow keys to switch sheets, or right-click/press Shift+F10 for sheet tab options.\";");
        source.Should().Contain("Focusable = true,");
        source.Should().Contain("Tag = tab.Id,");
        source.Should().Contain("button.ContextMenu = CreateSheetTabContextMenu(tab);");
        var pointerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.SheetTabPointer.cs"));
        source.Should().Contain("button.AddHandler(");
        source.Should().Contain("(_, args) => BeginSheetTabPointer(tab.Id, args)");
        source.Should().Contain("RoutingStrategies.Tunnel");
        source.Should().Contain("button.KeyDown += (_, args) => HandleSheetTabKeyDown(tab.Id, button, args);");
        source.Should().Contain("AutomationProperties.SetName(button, tab.Name);");
        source.Should().Contain("AutomationProperties.SetHelpText(button, SheetTabContextHelpText);");
        source.Should().Contain("private ContextMenu CreateSheetTabContextMenu(WorkbookSheetTab tab)");
        source.Should().Contain("ItemsSource = CreateSheetTabContextMenuItems(tab, isIdle, sheetTabIndex).ToArray()");
        source.Should().Contain("private IEnumerable<Control> CreateSheetTabContextMenuItems(WorkbookSheetTab tab, bool isIdle, int sheetTabIndex)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Rename...\", async () => await RenameActiveSheetAsync(), isIdle)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Insert Sheet\", AddNewSheet, isIdle)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Duplicate\", DuplicateActiveSheet, isIdle)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Delete Sheet\", DeleteActiveSheet, isIdle)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Hide\", HideActiveSheet, isIdle && _session.SheetTabs.Count > 1)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Unhide...\", async () => await UnhideSheetAsync(), isIdle && _session.HiddenSheets.Count > 0)");
        source.Should().Contain("CreateSheetTabColorContextMenuItem(tab, isIdle)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Select All Sheets\", SelectAllVisibleSheets, isIdle && _session.SheetTabs.Count > 1)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Ungroup Sheets\", UngroupSheets, isIdle && _session.IsWorkbookGrouped)");
        source.Should().Contain("CreateSheetTabContextMenuItem(tab, \"Move Left\", MoveActiveSheetLeft, isIdle && sheetTabIndex > 0)");
        source.Should().Contain("\"Move Right\"");
        source.Should().Contain("internal bool SelectSheetForContextCommand(SheetId sheetId)");
        pointerSource.Should().Contain("private void BeginSheetTabPointer(SheetId sheetId, PointerPressedEventArgs args)");
        pointerSource.Should().Contain("if (args.ClickCount >= 2)");
        pointerSource.Should().Contain("if (SelectSheetForContextCommand(sheetId))");
        pointerSource.Should().Contain("_ = RenameActiveSheetAsync();");
        source.Should().Contain("private void HandleSheetTabKeyDown(SheetId sheetId, Button button, KeyEventArgs args)");
        source.Should().Contain("NavigateSheetTabFromKeyboard(sheetId, args);");
        source.Should().Contain("private void OpenSheetTabContextMenuFromKeyboard(SheetId sheetId, Button button, KeyEventArgs args)");
        source.Should().Contain("private static bool IsSheetTabContextMenuKey(KeyEventArgs args)");
        source.Should().Contain("args.Key == Key.Apps");
        source.Should().Contain("args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift");
        source.Should().Contain("contextMenu.Opened -= SheetTabContextMenu_Opened;");
        source.Should().Contain("contextMenu.Opened += SheetTabContextMenu_Opened;");
        source.Should().Contain("contextMenu.Open(button);");
        source.Should().Contain("private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args)");
        source.Should().Contain("args.KeyModifiers != KeyModifiers.None");
        source.Should().Contain("Key.Left => GetAdjacentSheetTabId(sheetId, direction: -1)");
        source.Should().Contain("Key.Right => GetAdjacentSheetTabId(sheetId, direction: 1)");
        source.Should().Contain("Key.Home => GetEdgeSheetTabId(first: true)");
        source.Should().Contain("Key.End => GetEdgeSheetTabId(first: false)");
        source.Should().Contain("private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange)");
        source.Should().Contain("private void SelectSheetTabFromKeyboard(SheetId sheetId, bool selectRange)");
        source.Should().Contain("private SheetId? GetAdjacentSheetTabId(SheetId sheetId, int direction)");
        source.Should().Contain("SheetTabFocusPlanner.AdjacentTab(_session.SheetTabs, sheetId, direction, static tab => tab.Id)");
        source.Should().Contain("private SheetId? GetEdgeSheetTabId(bool first)");
        source.Should().Contain("SheetTabFocusPlanner.EdgeTab(_session.SheetTabs, first, static tab => tab.Id)");
        source.Should().Contain("private bool FocusActiveSheetTab()");
        source.Should().Contain("private bool FocusSheetTab(SheetId sheetId)");
        source.Should().Contain("private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args)");
        source.Should().Contain("FocusFirstEnabledSheetTabMenuItem(items);");
        source.Should().Contain("private static void FocusFirstEnabledSheetTabMenuItem(IEnumerable<Control> items)");
        source.Should().Contain("foreach (var item in items)");
        source.Should().Contain("item is MenuItem { IsEnabled: true } menuItem");
        source.Should().Contain("menuItem.Focus();");
        source.Should().Contain("private Button? FindSheetTabButton(SheetId sheetId)");
        source.Should().Contain("button.Tag is SheetId tag &&");
        source.Should().Contain("tag == sheetId");
        pointerSource.Should().Contain("private bool BeginSheetTabPointer(SheetId sheetId, KeyModifiers modifiers)");
        pointerSource.Should().Contain("var selectRange = modifiers.HasFlag(KeyModifiers.Shift);");
        pointerSource.Should().Contain("var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);");
        pointerSource.Should().Contain("SelectSheet(sheetId, selectRange, toggle);");
        pointerSource.Should().Contain("args.Pointer.Capture(_sheetTabsHost);");
        source.Should().Contain("private void DuplicateActiveSheet()");
        source.Should().Contain("var result = _session.DuplicateActiveSheet();");
        source.Should().Contain("RefreshShell($\"Duplicated {sourceName}\");");
        source.Should().Contain("private void MoveActiveSheetLeft()");
        source.Should().Contain("var result = _session.MoveActiveSheetLeft();");
        source.Should().Contain("RefreshShell($\"Moved {sheetName} left\");");
        source.Should().Contain("private void MoveActiveSheetRight()");
        source.Should().Contain("var result = _session.MoveActiveSheetRight();");
        source.Should().Contain("RefreshShell($\"Moved {sheetName} right\");");
        source.Should().Contain("tab.TabColor is { } tabColor ? Brush(tabColor) : Brushes.Transparent");
        source.Should().Contain("private NativeMenu CreateNativeSheetTabColorMenu()");
        source.Should().Contain("var clearColorItem = new NativeMenuItem { Header = \"No Color\" };");
        source.Should().Contain("clearColorItem.Click += (_, _) => ApplyActiveSheetTabColor(null);");
        source.Should().Contain("private NativeMenuItem CreateNativeSheetTabColorSwatchMenuItem(CellColorSwatch swatch)");
        source.Should().Contain("ApplyActiveSheetTabColor(swatch.Color);");
        source.Should().Contain("private void ApplyActiveSheetTabColor(CellColor? color)");
        source.Should().Contain("var result = _session.SetActiveSheetTabColor(color);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Tab Color failed.\");");
        source.Should().Contain("RefreshShell(color is null");
        source.Should().Contain("private void SelectAllVisibleSheets()");
        source.Should().Contain("var changed = _session.SelectAllVisibleSheets();");
        source.Should().Contain("RefreshShell(\"Selected all visible sheets\");");
        source.Should().Contain("private void UngroupSheets()");
        source.Should().Contain("var changed = _session.UngroupSheets();");
        source.Should().Contain("RefreshShell($\"Ungrouped sheets to {_session.ActiveSheet.Name}\");");
        source.Should().Contain("private string FormatWindowWorkbookTitle()");
        source.Should().Contain("WindowTitlePlanner.Compose(");
        source.Should().Contain("applicationName: ApplicationTitle");
        source.Should().Contain("groupSuffix: _session.IsWorkbookGrouped ? GroupTitleSuffix : \"\"");
        source.Should().Contain("applicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication");
        source.Should().Contain("Title = FormatWindowWorkbookTitle();");
        source.Should().Contain("var isGroupedTab = tab.IsGrouped && _session.IsWorkbookGrouped;");
        source.Should().Contain(": isGroupedTab");
        source.Should().Contain("private void HideActiveSheet()");
        source.Should().Contain("var result = _session.HideActiveSheet();");
        source.Should().Contain("RefreshShell($\"Hid {sheetName}\");");
        source.Should().Contain("private async Task UnhideSheetAsync()");
        source.Should().Contain("var hiddenSheets = _session.HiddenSheets;");
        source.Should().Contain("var sheet = await ShowUnhideSheetDialogAsync(hiddenSheets);");
        source.Should().Contain("var result = _session.UnhideSheet(sheet.Id);");
        source.Should().Contain("RefreshShell($\"Unhid {sheet.Name}\");");
        source.Should().Contain("private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets)");
        source.Should().Contain("AutomationProperties.SetAutomationId(sheetBox, \"UnhideSheetList\");");
        source.Should().Contain("private void DeleteActiveSheet()");
        source.Should().Contain("var result = _session.DeleteActiveSheet();");
        source.Should().Contain("RefreshShell($\"Deleted {sheetName}\");");
        source.Should().Contain("e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.Shift");
        source.Should().Contain("private static bool HasCommandAndShiftModifiers(KeyModifiers modifiers)");
        source.Should().Contain("ShellFocusTarget.Worksheet");
        source.Should().Contain("ShellFocusTarget.Ribbon");
        source.Should().Contain("ShellFocusTarget.FormulaBar");
        source.Should().Contain("ShellFocusTarget.SheetTabs");
        source.Should().Contain("ShellFocusTarget.TaskPane");
        source.Should().Contain("ShellFocusTarget.StatusBar");
        source.Should().Contain("_sheetGridHost.Focusable = true;");
        source.Should().Contain("AutomationProperties.SetName(_sheetGridHost, \"Worksheet\");");
        source.Should().Contain("_zoomText.Focusable = true;");
        source.Should().Contain("AutomationProperties.SetName(_zoomText, \"Zoom\");");
        source.Should().Contain("private static bool IsShellFocusCycleKey(KeyEventArgs args)");
        source.Should().Contain("args.Key == Key.F6 &&");
        source.Should().Contain("if (IsShellFocusCycleKey(e))");
        source.Should().Contain("CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);");
        source.Should().Contain("private void CycleShellFocus(bool reverse)");
        source.Should().Contain("ShellFocusCyclePlanner.GetNextAvailable(current, reverse, IsShellFocusTargetAvailable)");
        source.Should().Contain("private bool IsShellFocusTargetAvailable(ShellFocusTarget target)");
        source.Should().Contain("private ShellFocusTarget GetCurrentShellFocusTarget()");
        source.Should().Contain("private bool FocusShellRegion(ShellFocusTarget target)");
        source.Should().Contain("ShellFocusTarget.Ribbon => FocusFirstEnabledToolbarControl()");
        source.Should().Contain("ShellFocusTarget.FormulaBar => FocusControl(_formulaBox)");
        source.Should().Contain("ShellFocusTarget.SheetTabs => FocusActiveSheetTab()");
        source.Should().Contain("target != ShellFocusTarget.TaskPane ||");
        source.Should().Contain("_pivotFieldPaneHost.IsVisible");
        source.Should().Contain("if (IsPivotFieldPaneFocused())");
        source.Should().Contain("ShellFocusTarget.TaskPane => FocusVisibleTaskPane()");
        source.Should().Contain("_pivotFieldPaneSearchBox is { } searchBox && FocusControl(searchBox)");
        source.Should().Contain("ShellFocusTarget.StatusBar => FocusControl(_zoomText)");
        source.Should().Contain("_ => FocusControl(_sheetGridHost)");
        source.Should().Contain("private bool FocusFirstEnabledToolbarControl()");
        source.Should().Contain("private IReadOnlyList<Control> GetToolbarFocusTargets()");
        source.Should().Contain("_openButton,");
        source.Should().Contain("_alignRightButton");
        source.Should().Contain("private bool IsAnyToolbarControlFocused()");
        source.Should().Contain("private bool IsAnySheetTabFocused()");
        source.Should().Contain("private static bool FocusControl(Control control)");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "SelectPreviousSheetGroup", "WorkbookShortcutKey.PageUp", "WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "SelectNextSheetGroup", "WorkbookShortcutKey.PageDown", "WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ActivatePreviousSheet", "WorkbookShortcutKey.PageUp", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "ActivateNextSheet", "WorkbookShortcutKey.PageDown", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutRouteHandled(source, "SelectPreviousSheetGroup", "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true)");
        AssertWorkbookShortcutRouteHandled(source, "SelectNextSheetGroup", "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true)");
        AssertWorkbookShortcutRouteHandled(source, "ActivatePreviousSheet", "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false)");
        AssertWorkbookShortcutRouteHandled(source, "ActivateNextSheet", "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false)");
        source.Should().Contain("HasNewSheetButton: _newSheetButton.Content?.ToString() == \"+\"");
        source.Should().Contain("HasNativeSheetMenu: hasNativeSheetMenu");
        source.Should().Contain("HasNativeNewSheetMenuItem: HasNativeMenuItem(_newSheetMenuItem, NativeMenuItemId.NewSheet)");
        source.Should().Contain("HasNativeRenameSheetMenuItem: HasNativeMenuItem(_renameSheetMenuItem, NativeMenuItemId.RenameSheet)");
        source.Should().Contain("HasNativeDuplicateSheetMenuItem: HasNativeMenuItem(_duplicateSheetMenuItem, NativeMenuItemId.DuplicateSheet)");
        source.Should().Contain("HasNativeMoveSheetLeftMenuItem: HasNativeMenuItem(_moveSheetLeftMenuItem, NativeMenuItemId.MoveSheetLeft)");
        source.Should().Contain("HasNativeMoveSheetRightMenuItem: HasNativeMenuItem(_moveSheetRightMenuItem, NativeMenuItemId.MoveSheetRight)");
        source.Should().Contain("HasNativeTabColorMenuItem: HasNativeMenuItem(_tabColorMenuItem, NativeMenuItemId.TabColor)");
        source.Should().Contain("HasNativeClearTabColorMenuItem: HasNativeSubmenuItem(_tabColorMenuItem.Menu, \"No Color\")");
        source.Should().Contain("NativeTabColorSwatchCount: nativeTabColorSwatchCount");
        source.Should().Contain("private bool HasSheetTabButton(Func<Button, bool> predicate)");
        source.Should().Contain("HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable)");
        source.Should().Contain("HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true");
        source.Should().Contain("HasSheetTabContextKeyboardHelp: HasSheetTabButton(button =>");
        source.Should().Contain("string.Equals(AutomationProperties.GetHelpText(button), SheetTabContextHelpText, StringComparison.Ordinal))");
        source.Should().Contain("HasSheetTabContextRenameMenuItem: HasSheetTabContextMenuItem(\"Rename...\")");
        source.Should().Contain("HasSheetTabContextTabColorMenuItem: HasSheetTabContextMenuItem(\"Tab Color\")");
        source.Should().Contain("HasSheetTabContextNoColorMenuItem: HasSheetTabContextSubmenuItem(\"Tab Color\", \"No Color\")");
        source.Should().Contain("HasSheetTabContextSelectAllSheetsMenuItem: HasSheetTabContextMenuItem(\"Select All Sheets\")");
        source.Should().Contain("HasSheetTabContextUngroupSheetsMenuItem: HasSheetTabContextMenuItem(\"Ungroup Sheets\")");
        source.Should().Contain("HasNativeSelectAllSheetsMenuItem: HasNativeMenuItem(_selectAllSheetsMenuItem, NativeMenuItemId.SelectAllSheets)");
        source.Should().Contain("HasNativeUngroupSheetsMenuItem: HasNativeMenuItem(_ungroupSheetsMenuItem, NativeMenuItemId.UngroupSheets)");
        source.Should().Contain("HasNativeHideSheetMenuItem: HasNativeMenuItem(_hideSheetMenuItem, NativeMenuItemId.HideSheet)");
        source.Should().Contain("HasNativeUnhideSheetMenuItem: HasNativeMenuItem(_unhideSheetMenuItem, NativeMenuItemId.UnhideSheet)");
        source.Should().Contain("HasNativeDeleteSheetMenuItem: HasNativeMenuItem(_deleteSheetMenuItem, NativeMenuItemId.DeleteSheet)");
        smokeSource.Should().Contain("SheetTabCount > 0");
        smokeSource.Should().Contain("HasNewSheetButton &&");
        smokeSource.Should().Contain("HasNativeSheetMenu &&");
        smokeSource.Should().Contain("HasNativeNewSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeRenameSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeDuplicateSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeMoveSheetLeftMenuItem &&");
        smokeSource.Should().Contain("HasNativeMoveSheetRightMenuItem &&");
        smokeSource.Should().Contain("HasNativeTabColorMenuItem &&");
        smokeSource.Should().Contain("HasNativeClearTabColorMenuItem &&");
        smokeSource.Should().Contain("NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count");
        smokeSource.Should().Contain("HasFocusableSheetTab &&");
        smokeSource.Should().Contain("HasFocusableActiveSheetTab &&");
        smokeSource.Should().Contain("HasShellFocusCycleTargets &&");
        smokeSource.Should().Contain("HasSheetTabContextKeyboardHelp &&");
        smokeSource.Should().Contain("HasSheetTabContextRenameMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextTabColorMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextNoColorMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextSelectAllSheetsMenuItem &&");
        smokeSource.Should().Contain("HasSheetTabContextUngroupSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeSelectAllSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeUngroupSheetsMenuItem &&");
        smokeSource.Should().Contain("HasNativeHideSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeUnhideSheetMenuItem &&");
        smokeSource.Should().Contain("HasNativeDeleteSheetMenuItem &&");
        smokeSource.Should().Contain("new_sheet_button={FormatBool(snapshot.HasNewSheetButton)}");
        smokeSource.Should().Contain("native_sheet_menu={FormatBool(snapshot.HasNativeSheetMenu)}");
        smokeSource.Should().Contain("native_new_sheet_menu_item={FormatBool(snapshot.HasNativeNewSheetMenuItem)}");
        smokeSource.Should().Contain("native_rename_sheet_menu_item={FormatBool(snapshot.HasNativeRenameSheetMenuItem)}");
        smokeSource.Should().Contain("native_duplicate_sheet_menu_item={FormatBool(snapshot.HasNativeDuplicateSheetMenuItem)}");
        smokeSource.Should().Contain("native_move_sheet_left_menu_item={FormatBool(snapshot.HasNativeMoveSheetLeftMenuItem)}");
        smokeSource.Should().Contain("native_move_sheet_right_menu_item={FormatBool(snapshot.HasNativeMoveSheetRightMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_menu_item={FormatBool(snapshot.HasNativeTabColorMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_clear_item={FormatBool(snapshot.HasNativeClearTabColorMenuItem)}");
        smokeSource.Should().Contain("native_tab_color_swatch_count={snapshot.NativeTabColorSwatchCount}");
        smokeSource.Should().Contain("focusable_active_sheet_tab={FormatBool(snapshot.HasFocusableActiveSheetTab)}");
        smokeSource.Should().Contain("shell_focus_cycle_targets={FormatBool(snapshot.HasShellFocusCycleTargets)}");
        smokeSource.Should().Contain("native_select_all_sheets_menu_item={FormatBool(snapshot.HasNativeSelectAllSheetsMenuItem)}");
        smokeSource.Should().Contain("native_ungroup_sheets_menu_item={FormatBool(snapshot.HasNativeUngroupSheetsMenuItem)}");
        smokeSource.Should().Contain("native_hide_sheet_menu_item={FormatBool(snapshot.HasNativeHideSheetMenuItem)}");
        smokeSource.Should().Contain("native_unhide_sheet_menu_item={FormatBool(snapshot.HasNativeUnhideSheetMenuItem)}");
        smokeSource.Should().Contain("native_delete_sheet_menu_item={FormatBool(snapshot.HasNativeDeleteSheetMenuItem)}");
    }

    [Fact]
    public void MainWindow_RendersSelectedRangeStatsThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly TextBlock _selectionStatsText = new();");
        source.Should().Contain("_selectionStatsText.FontSize = 12;");
        source.Should().Contain("_selectionStatsText.MaxWidth = 420;");
        source.Should().Contain("_selectionStatsText.TextTrimming = TextTrimming.CharacterEllipsis;");
        source.Should().Contain("_statusText,");
        source.Should().Contain("_selectionStatsText,");
        source.Should().Contain("_statusText.Text = status;");
        source.Should().Contain("_selectionStatsText.Text = _session.SelectionStatsText;");
        source.Should().Contain("_session.SelectAnchoredRange(anchor, address);");
        source.Should().Contain("RefreshShell(\"Ready\");");
    }

    [Fact]
    public void MainWindow_ExposesFormulaAndStatusAccessibilityMetadataToLaunchSmoke()
    {
        // Normalize CRLF -> LF so multi-line pinned snippets match regardless of the checkout's
        // line endings.
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));

        source.Should().Contain("AutomationProperties.SetAutomationId(_formulaBox, \"FormulaBox\");");
        source.Should().Contain("AutomationProperties.SetName(_formulaBox, FormulaBarText(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey));");
        source.Should().Contain("AutomationProperties.SetHelpText(_formulaBox, FormulaBarText(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_statusText, \"StatusText\");");
        source.Should().Contain("AutomationProperties.SetName(_statusText, \"Status\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_statusText, \"Shows the current workbook status.\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cellAddressText, \"CellAddressText\");");
        source.Should().Contain("AutomationProperties.SetName(_cellAddressText, \"Cell address\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cellAddressText, \"Shows the active cell address.\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_selectionStatsText, \"SelectionStatsText\");");
        source.Should().Contain("AutomationProperties.SetName(_selectionStatsText, \"Selection statistics\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_selectionStatsText, \"Shows statistics for the current selection.\");");
        source.Should().Contain("HasFormulaBoxAutomationName: string.Equals(");
        source.Should().Contain("FormulaBarText(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey)");
        source.Should().Contain("HasFormulaBoxAutomationHelp: string.Equals(");
        source.Should().Contain("FormulaBarText(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey)");
        source.Should().Contain("HasFormulaBoxAutomationId: string.Equals(AutomationProperties.GetAutomationId(_formulaBox), \"FormulaBox\", StringComparison.Ordinal)");
        source.Should().Contain("HasStatusTextAutomationName: string.Equals(AutomationProperties.GetName(_statusText), \"Status\", StringComparison.Ordinal)");
        source.Should().Contain("HasStatusTextAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_statusText), \"Shows the current workbook status.\", StringComparison.Ordinal)");
        source.Should().Contain("HasStatusTextAutomationId: string.Equals(AutomationProperties.GetAutomationId(_statusText), \"StatusText\", StringComparison.Ordinal)");
        source.Should().Contain("HasStatusTextValue: HasStatusBarAccessibleValue()");
        source.Should().Contain("private bool HasStatusBarAccessibleValue() =>");
        source.Should().Contain("HasCellAddressAutomationName: string.Equals(AutomationProperties.GetName(_cellAddressText), \"Cell address\", StringComparison.Ordinal)");
        source.Should().Contain("HasCellAddressAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_cellAddressText), \"Shows the active cell address.\", StringComparison.Ordinal)");
        source.Should().Contain("HasCellAddressAutomationId: string.Equals(AutomationProperties.GetAutomationId(_cellAddressText), \"CellAddressText\", StringComparison.Ordinal)");
        source.Should().Contain("HasSelectionStatsAutomationName: string.Equals(AutomationProperties.GetName(_selectionStatsText), \"Selection statistics\", StringComparison.Ordinal)");
        source.Should().Contain("HasSelectionStatsAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_selectionStatsText), \"Shows statistics for the current selection.\", StringComparison.Ordinal)");
        source.Should().Contain("HasSelectionStatsAutomationId: string.Equals(AutomationProperties.GetAutomationId(_selectionStatsText), \"SelectionStatsText\", StringComparison.Ordinal)");

        smokeSource.Should().Contain("public bool HasAccessibilitySmokeEvidence =>");
        smokeSource.Should().Contain("HasAccessibilitySmokeEvidence &&");
        smokeSource.Should().Contain("macos_accessibility_smoke={(snapshot.HasAccessibilitySmokeEvidence ? \"passed\" : \"failed\")}");
        smokeSource.Should().Contain("a11y_formula_box_name={FormatBool(snapshot.HasFormulaBoxAutomationName)}");
        smokeSource.Should().Contain("a11y_formula_box_help={FormatBool(snapshot.HasFormulaBoxAutomationHelp)}");
        smokeSource.Should().Contain("a11y_status_text_name={FormatBool(snapshot.HasStatusTextAutomationName)}");
        smokeSource.Should().Contain("a11y_status_text_value={FormatBool(snapshot.HasStatusTextValue)}");
        smokeSource.Should().Contain("a11y_cell_address_name={FormatBool(snapshot.HasCellAddressAutomationName)}");
        smokeSource.Should().Contain("a11y_selection_stats_name={FormatBool(snapshot.HasSelectionStatsAutomationName)}");
    }

    [Fact]
    public void MainWindow_WiresFindGoToThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookSession.cs"));
        var findReplaceWorkflowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Services", "FindReplaceWorkflowSession.cs"));
        var findReplaceServiceSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.Core.Commands", "FindReplaceService.cs"));
        var findReplaceSearchPlannerSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.Core.Commands", "FindReplaceSearchPlanner.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly NativeMenuItem _findMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _findNextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _replaceMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _goToMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _goToSpecialMenuItem = new();");
        source.Should().Contain("private enum FindDialogAction");
        source.Should().Contain("private sealed record FindDialogResult(");
        source.Should().Contain("FindOptions Options,");
        source.Should().Contain("bool MatchCase,");
        source.Should().Contain("bool MatchEntireCell);");
        source.Should().Contain("private enum ReplaceDialogAction");
        source.Should().Contain("private sealed record ReplaceDialogResult(");
        source.Should().Contain("ReplaceDialogAction Action,");
        source.Should().Contain("StyleDiff? ReplacementFormat);");
        source.Should().Contain("private sealed record FindOptionsControls(");
        source.Should().Contain("private sealed record GoToSpecialDialogResult(GoToSpecialKind Kind, GoToSpecialOptions Options);");
        source.Should().Contain("GoToDialogPlanner.BuildReferenceChoices(");
        source.Should().Contain("GoToSpecialDialogPlanner.BuildChoices().ToArray()");
        source.Should().Contain("GoToSpecialDialogPlanner.BuildOptions(choice.Kind, GetValueTypes())");
        catalogSource.Should().Contain("new(NativeMenuItemId.Find, \"Find...\", NativeMenuGesture(WorkbookShortcutRoute.Find))");
        source.Should().Contain("_findMenuItem.Click += async (_, _) => await ShowFindDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.FindNext, \"Find Next\", new NativeMenuGesturePlan(NativeMenuGestureKey.G, NativeMenuGestureModifiers.Meta))");
        source.Should().Contain("_findNextMenuItem.Click += (_, _) => FindNext();");
        catalogSource.Should().Contain("new(NativeMenuItemId.Replace, \"Replace...\", NativeMenuGesture(WorkbookShortcutRoute.Replace))");
        source.Should().Contain("_replaceMenuItem.Click += async (_, _) => await ShowReplaceDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoTo, \"Go To...\", NativeMenuGesture(WorkbookShortcutRoute.GoTo))");
        source.Should().Contain("_goToMenuItem.Click += async (_, _) => await ShowGoToDialogAsync();");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoToSpecial, \"Go To Special...\", RequiresGestureInSmoke: false)");
        source.Should().Contain("_goToSpecialMenuItem.Click += async (_, _) => await ShowGoToSpecialDialogAsync();");
        source.Should().Contain("NativeMenuItemId.Find => _findMenuItem,");
        source.Should().Contain("NativeMenuItemId.FindNext => _findNextMenuItem,");
        source.Should().Contain("NativeMenuItemId.Replace => _replaceMenuItem,");
        source.Should().Contain("NativeMenuItemId.GoTo => _goToMenuItem,");
        source.Should().Contain("NativeMenuItemId.GoToSpecial => _goToSpecialMenuItem,");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Find)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.FindNext)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.Replace)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.GoTo)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.GoToSpecial)");
        catalogSource.Should().Contain("new(NativeMenuItemId.Find, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.FindNext, context.IsIdle && context.CanFindNext)");
        catalogSource.Should().Contain("new(NativeMenuItemId.Replace, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoTo, context.IsIdle)");
        catalogSource.Should().Contain("new(NativeMenuItemId.GoToSpecial, context.IsIdle)");
        source.Should().Contain("private async Task ShowFindDialogAsync()");
        source.Should().Contain("private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogSmokeProbe>? launchSmokeProbe = null)");
        source.Should().Contain("private void NavigateToFindAllMatch(WorkbookFindAllMatch match)");
        source.Should().Contain("FindOptions? options = null,");
        source.Should().Contain("private Task ShowReplaceDialogAsync()");
        source.Should().Contain("private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogSmokeProbe>? launchSmokeProbe = null)");
        source.Should().Contain("private async Task ShowGoToDialogAsync()");
        source.Should().Contain("private async Task ShowGoToSpecialDialogAsync()");
        source.Should().Contain("private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(Action<GoToSpecialDialogSmokeProbe>? launchSmokeProbe = null)");
        source.Should().Contain("private static CheckBox CreateGoToSpecialValueTypeBox(string label, string automationId)");
        source.Should().Contain("private static AvaloniaGrid CreateGoToSpecialChoiceGrid(");
        source.Should().Contain("private static GoToSpecialChoice[] CreateGoToSpecialChoices()");
        source.Should().Contain("private bool SelectGoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)");
        source.Should().Contain("private async Task<string?> ShowSingleInputDialogAsync(");
        source.Should().Contain("\"FindTextBox\"");
        source.Should().Contain("\"FindNextButton\"");
        source.Should().Contain("\"FindAllButton\"");
        source.Should().Contain("CreateFindOptionsControls(\"Find\", defaultLookInIndex: 0)");
        source.Should().Contain("var chooseFormatButton = CreateFindReplaceFormatButton(\"FindChooseFormatFromCellButton\", \"Choose From Cell\");");
        source.Should().Contain("var clearFormatButton = CreateFindReplaceFormatButton(\"FindClearFormatButton\", \"Clear Format\");");
        source.Should().Contain("var findFormatRow = CreateFindReplaceFormatRow(\"Find format\", chooseFormatButton, clearFormatButton);");
        // CreateFindOptions now threads the selection-scope-at-open through as a third argument
        // (Excel restricts Find All/Replace All to the selection that existed when the dialog opened).
        source.Should().Contain("CreateFindOptions(optionsControls, findFormat, selectionScopeAtOpen)");
        source.Should().Contain("{automationPrefix}WithinBox");
        source.Should().Contain("{automationPrefix}SearchBox");
        source.Should().Contain("{automationPrefix}LookInBox");
        source.Should().Contain("{automationPrefix}MatchCaseBox");
        source.Should().Contain("{automationPrefix}MatchEntireCellBox");
        source.Should().Contain("\"FindReplaceResultsList\"");
        source.Should().Contain("\"ReplaceFindTextBox\"");
        source.Should().Contain("\"ReplaceWithTextBox\"");
        source.Should().Contain("\"ReplaceButton\"");
        source.Should().Contain("\"ReplaceAllButton\"");
        source.Should().Contain("CreateFindOptionsControls(\"Replace\", defaultLookInIndex: 1)");
        source.Should().Contain("var chooseFindFormatButton = CreateFindReplaceFormatButton(\"ReplaceFindChooseFormatFromCellButton\", \"Choose From Cell\");");
        source.Should().Contain("var clearFindFormatButton = CreateFindReplaceFormatButton(\"ReplaceFindClearFormatButton\", \"Clear Format\");");
        source.Should().Contain("var chooseReplaceFormatButton = CreateFindReplaceFormatButton(\"ReplaceWithChooseFormatFromCellButton\", \"Choose From Cell\");");
        source.Should().Contain("var clearReplaceFormatButton = CreateFindReplaceFormatButton(\"ReplaceWithClearFormatButton\", \"Clear Format\");");
        source.Should().Contain("var findFormatRow = CreateFindReplaceFormatRow(\"Find format\", chooseFindFormatButton, clearFindFormatButton);");
        source.Should().Contain("var replaceFormatRow = CreateFindReplaceFormatRow(\"Replace format\", chooseReplaceFormatButton, clearReplaceFormatButton);");
        source.Should().Contain("\"GoToReferenceBox\"");
        source.Should().Contain("\"GoToSpecialKindBox\"");
        source.Should().Contain("\"GoToSpecialNumbersBox\"");
        source.Should().Contain("\"GoToSpecialTextBox\"");
        source.Should().Contain("\"GoToSpecialLogicalsBox\"");
        source.Should().Contain("\"GoToSpecialErrorsBox\"");
        source.Should().Contain("\"GoToSpecialOkButton\"");
        source.Should().Contain("Header = \"Go To Special\"");
        source.Should().Contain("Header = \"Values for constants and formulas\"");
        // CreateFindOptions wraps onto multiple lines now that it takes a third parameter for the
        // selection-scope-at-open (see the CreateFindOptions(optionsControls, findFormat,
        // selectionScopeAtOpen) call-site assertion above) -- assert each parameter line separately
        // instead of one single-line signature string.
        source.Should().Contain("private FindOptions CreateFindOptions(");
        source.Should().Contain("FindOptionsControls controls,");
        source.Should().Contain("StyleDiff? requiredFormat = null,");
        source.Should().Contain("IReadOnlyList<GridRange>? selectionScope = null) =>");
        source.Should().Contain("private static FindOptionsControls CreateFindOptionsControls(string automationPrefix, int defaultLookInIndex)");
        source.Should().Contain("private static Button CreateFindReplaceFormatButton(string automationId, string content)");
        source.Should().Contain("private static StackPanel CreateFindReplaceFormatRow(string label, Button chooseButton, Button clearButton)");
        source.Should().Contain("private static void UpdateFindReplaceFormatState(StyleDiff? format, Button chooseButton, Button clearButton)");
        source.Should().Contain("chooseButton.Content = format is null ? \"Choose From Cell\" : \"Format Set\";");
        source.Should().Contain("clearButton.IsVisible = format is not null;");
        source.Should().Contain("RequiredFormat: requiredFormat");
        source.Should().Contain("FindLookIn.Formulas");
        source.Should().Contain("FindLookIn.Notes");
        source.Should().Contain("FindLookIn.Comments");
        source.Should().Contain("var result = _session.FindNext(searchText, options, matchCase, matchEntireCell);");
        source.Should().Contain("var result = _session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);");
        // The Find command now opens the tabbed Find & Replace dialog (parity with the WPF FindReplaceDialog),
        // which renders Find All matches inline rather than in a separate results window.
        source.Should().Contain("private Task ShowFindReplaceTabbedDialogAsync(bool replaceMode = false)");
        source.Should().Contain("return ShowFindReplaceTabbedDialogAsync(replaceMode: true);");
        source.Should().Contain("ShowOwnedModelessWindow(");
        source.Should().Contain("_findReplaceDialog = dialog;");
        source.Should().Contain("resultsList.ItemsSource = result.Matches;");
        source.Should().Contain("var result = _session.GoToCell(match.Address);");
        source.Should().Contain("_session.ReplaceNextValue(");
        source.Should().Contain("_session.ReplaceAllValues(");
        source.Should().Contain("CreateFindOptions(options, findFormat, selectionScopeAtOpen)");
        source.Should().Contain("MatchEntire(), replacementFormat)");
        source.Should().Contain("var result = _session.GoToReference(reference);");
        source.Should().Contain("var result = _session.GoToSpecial(kind, options);");
        source.Should().Contain("result.SelectedRanges.Count == 1");
        source.Should().Contain("FormatRangeReference(result.SelectedRange!.Value)");
        source.Should().Contain("args.Key == Key.Oem1 && args.KeyModifiers == KeyModifiers.Alt;");
        source.Should().Contain("SelectGoToSpecial(GoToSpecialKind.VisibleCellsOnly);");
        source.Should().Contain("e.Key == Key.G && e.KeyModifiers == KeyModifiers.Meta");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Find", "WorkbookShortcutKey.F", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "Replace", "WorkbookShortcutKey.H", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "GoTo", "WorkbookShortcutKey.G", "WorkbookShortcutModifiers.Control");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "GoTo", "WorkbookShortcutKey.F5");
        AssertWorkbookShortcutRouteHandled(source, "Find", "await ShowFindDialogAsync();");
        AssertWorkbookShortcutRouteHandled(source, "Replace", "await ShowReplaceDialogAsync();");
        AssertWorkbookShortcutRouteHandled(source, "GoTo", "await ShowGoToDialogAsync();");
        source.Should().Contain("HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, NativeMenuItemId.Find)");
        source.Should().Contain("HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, NativeMenuItemId.FindNext)");
        source.Should().Contain("HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, NativeMenuItemId.Replace)");
        source.Should().Contain("HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, NativeMenuItemId.GoTo)");
        source.Should().Contain("HasNativeGoToSpecialMenuItem: HasNativeMenuItem(_goToSpecialMenuItem, NativeMenuItemId.GoToSpecial)");
        sessionSource.Should().Contain("public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];");
        sessionSource.Should().Contain("public StyleDiff? CreateFormatDiffFromActiveCell()");
        sessionSource.Should().Contain("public StyleDiff? CreateFormatDiffFromCell(CellAddress address)");
        sessionSource.Should().Contain("public WorkbookFindAllResult FindAll(");
        sessionSource.Should().Contain("WorkbookFindAllResult.Found(result.Matches.Select(CreateFindAllMatch).ToList())");
        sessionSource.Should().Contain("private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)");
        sessionSource.Should().Contain("private string FindNameForAddress(CellAddress address)");
        sessionSource.Should().Contain("public WorkbookReplaceResult ReplaceNextValue(");
        sessionSource.Should().Contain("FindOptions? options,");
        sessionSource.Should().Contain("StyleDiff? replacementFormat = null");
        sessionSource.Should().Contain("var result = _findReplaceWorkflow.ReplaceNext(");
        sessionSource.Should().Contain("var result = _findReplaceWorkflow.ReplaceAll(");
        sessionSource.Should().Contain("result.ReplacedCount,");
        sessionSource.Should().Contain("result.MatchIndex,");
        findReplaceWorkflowSource.Should().Contain("var effectiveOptions = ResolveFindOptions(workbook, options, FindLookIn.Values);");
        findReplaceWorkflowSource.Should().Contain("GetReplaceTargetIndex(workbook, matches, effectiveOptions.SearchOrder, sameSearch)");
        findReplaceWorkflowSource.Should().Contain("FindReplaceService.TryCreateReplacementCommand(");
        findReplaceWorkflowSource.Should().Contain("effectiveOptions.LookIn,");
        findReplaceServiceSource.Should().Contain("new ApplyStyleCommand(");
        findReplaceServiceSource.Should().Contain("FindLookIn.Notes when");
        findReplaceServiceSource.Should().Contain("match.Target == FindResultTarget.Note");
        findReplaceServiceSource.Should().Contain("new SetCommentCommand(");
        findReplaceServiceSource.Should().Contain("new UpdateThreadedCommentTextCommand(");
        findReplaceServiceSource.Should().Contain("match.Target == FindResultTarget.ThreadedCommentReply");
        findReplaceServiceSource.Should().Contain("match.ReplyIndex is { } replyIndex");
        findReplaceServiceSource.Should().Contain("new UpdateThreadedCommentReplyCommand(");
        sessionSource.Should().Contain("public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)");
        // CurrentRegion/Precedents/Dependents trace from the true selection; content kinds use the
        // (auto-expanded) search range.
        sessionSource.Should().Contain("kind is GoToSpecialKind.CurrentRegion or GoToSpecialKind.Precedents or GoToSpecialKind.Dependents");
        sessionSource.Should().Contain("ResolveGoToSpecialSearchRange();");
        sessionSource.Should().Contain("GoToSpecialService.Find(Workbook, ActiveSheet, searchRange, kind, ActiveCell, options)");
        sessionSource.Should().Contain("SelectionRangeService.CompressAddresses(matches)");
        sessionSource.Should().Contain("SelectRanges(selectedRange, ranges);");
        findReplaceServiceSource.Should().Contain("public enum FindResultTarget");
        // r48: FindOptions gained a trailing optional SelectionScope parameter (Excel: Replace All
        // within an active multi-cell selection), so RequiredFormat is no longer the terminal param.
        findReplaceServiceSource.Should().Contain("StyleDiff? RequiredFormat = null,");
        findReplaceServiceSource.Should().Contain("IReadOnlyList<GridRange>? SelectionScope = null);");
        findReplaceServiceSource.Should().Contain("FindReplaceSearchPlanner.MatchesRequiredFormat(workbook, sheet, candidate.Address, options.RequiredFormat)");
        findReplaceServiceSource.Should().Contain("ThreadedCommentReply");
        findReplaceServiceSource.Should().Contain("FindResultTarget Target = FindResultTarget.Cell,");
        findReplaceServiceSource.Should().Contain("int? ReplyIndex = null);");
        findReplaceSearchPlannerSource.Should().Contain("public readonly record struct SearchText(");
        findReplaceSearchPlannerSource.Should().Contain("public static bool MatchesRequiredFormat(Workbook workbook, Sheet sheet, CellAddress address, StyleDiff? requiredFormat)");
        findReplaceSearchPlannerSource.Should().Contain("comment.Replies[replyIndex].Text");
        findReplaceSearchPlannerSource.Should().Contain("FindResultTarget.ThreadedCommentReply,");
        smokeSource.Should().Contain("bool HasNativeFindMenuItem,");
        smokeSource.Should().Contain("bool HasNativeFindNextMenuItem,");
        smokeSource.Should().Contain("bool HasNativeReplaceMenuItem,");
        smokeSource.Should().Contain("bool HasNativeGoToMenuItem,");
        smokeSource.Should().Contain("bool HasNativeGoToSpecialMenuItem,");
        smokeSource.Should().Contain("internal sealed record MacOsLaunchSmokeDialogSnapshot(");
        smokeSource.Should().Contain("bool HasFindDialog,");
        smokeSource.Should().Contain("bool HasFindDialogTextBox,");
        smokeSource.Should().Contain("bool HasFindDialogActionButtons,");
        smokeSource.Should().Contain("bool HasFindDialogOptions,");
        smokeSource.Should().Contain("bool HasFindDialogFormatControls,");
        smokeSource.Should().Contain("bool HasFindDialogCompactLayout,");
        smokeSource.Should().Contain("bool HasReplaceDialog,");
        smokeSource.Should().Contain("bool HasReplaceDialogTextBoxes,");
        smokeSource.Should().Contain("bool HasReplaceDialogActionButtons,");
        smokeSource.Should().Contain("bool HasReplaceDialogOptions,");
        smokeSource.Should().Contain("bool HasReplaceDialogFormatControls,");
        smokeSource.Should().Contain("bool HasReplaceDialogCompactLayout,");
        smokeSource.Should().Contain("bool HasGoToDialog,");
        smokeSource.Should().Contain("bool HasGoToDialogReferenceControls,");
        smokeSource.Should().Contain("bool HasGoToDialogHistoryControls,");
        smokeSource.Should().Contain("bool HasGoToDialogSpecialControl,");
        smokeSource.Should().Contain("bool HasGoToDialogCompactLayout,");
        smokeSource.Should().Contain("bool HasGoToSpecialDialog,");
        smokeSource.Should().Contain("bool HasGoToSpecialKindControls,");
        smokeSource.Should().Contain("bool HasGoToSpecialValueTypeControls,");
        smokeSource.Should().Contain("bool HasGoToSpecialDialogCompactLayout,");
        smokeSource.Should().Contain("bool HasFindDialogClosedWithoutAccept,");
        smokeSource.Should().Contain("bool HasReplaceDialogClosedWithoutAccept,");
        smokeSource.Should().Contain("bool HasGoToDialogClosedWithoutAccept,");
        smokeSource.Should().Contain("bool HasGoToSpecialDialogClosedWithoutAccept,");
        smokeSource.Should().Contain("MacOsLaunchSmokeDialogSnapshot DialogEvidence,");
        smokeSource.Should().Contain("HasNativeFindMenuItem &&");
        smokeSource.Should().Contain("HasNativeFindNextMenuItem &&");
        smokeSource.Should().Contain("HasNativeReplaceMenuItem &&");
        smokeSource.Should().Contain("HasNativeGoToMenuItem &&");
        smokeSource.Should().Contain("HasNativeGoToSpecialMenuItem &&");
        smokeSource.Should().Contain("DialogEvidence.IsPassed");
        smokeSource.Should().Contain("await mainWindow.CaptureLaunchSmokeDialogEvidenceAsync();");
        source.Should().Contain("internal async Task<MacOsLaunchSmokeDialogSnapshot> CaptureLaunchSmokeDialogEvidenceAsync()");
        source.Should().Contain("ShowFindInputDialogAsync(probe =>");
        source.Should().Contain("ShowReplaceInputDialogAsync(probe =>");
        source.Should().Contain("ShowGoToSpecialInputDialogAsync(probe =>");
        source.Should().Contain("RunLaunchSmokeDialogProbe(");
        source.Should().Contain("Dispatcher.UIThread.Post(() => dialog.Close());");
        source.Should().Contain("HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 430, minWidth: 360, minHeight: 390)");
        source.Should().Contain("HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 520, minWidth: 360, minHeight: 480)");
        source.Should().Contain("HasLaunchSmokeCompactDialog(probe.Dialog, width: 420, height: 320, minWidth: 420, minHeight: 320)");
        source.Should().Contain("height: GoToSpecialDialogPlanner.Height");
        source.Should().Contain("minHeight: GoToSpecialDialogPlanner.Height");
        source.Should().Contain("HasLaunchSmokeButton(probe.ChooseFormatButton, \"FindChooseFormatFromCellButton\", \"Choose From Cell\")");
        source.Should().Contain("HasLaunchSmokeButton(probe.ChooseFindFormatButton, \"ReplaceFindChooseFormatFromCellButton\", \"Choose From Cell\")");
        source.Should().Contain("ShowGoToInputDialogAsync(");
        source.Should().Contain("HasLaunchSmokeAutomationId(probe.InputBox, \"GoToReferenceBox\")");
        source.Should().Contain("HasLaunchSmokeAutomationId(probe.HistoryList, \"GoToHistoryList\")");
        source.Should().Contain("HasLaunchSmokeButton(probe.SpecialButton, \"GoToSpecialButton\", \"Special...\")");
        source.Should().Contain("HasLaunchSmokeCheckBox(probe.NumbersBox, \"GoToSpecialNumbersBox\", \"Numbers\")");
        smokeSource.Should().Contain("native_find_menu_item={FormatBool(snapshot.HasNativeFindMenuItem)}");
        smokeSource.Should().Contain("native_find_next_menu_item={FormatBool(snapshot.HasNativeFindNextMenuItem)}");
        smokeSource.Should().Contain("native_replace_menu_item={FormatBool(snapshot.HasNativeReplaceMenuItem)}");
        smokeSource.Should().Contain("native_go_to_menu_item={FormatBool(snapshot.HasNativeGoToMenuItem)}");
        smokeSource.Should().Contain("native_go_to_special_menu_item={FormatBool(snapshot.HasNativeGoToSpecialMenuItem)}");
        smokeSource.Should().Contain("macos_dialog_smoke={(snapshot.DialogEvidence.IsPassed ? \"passed\" : \"failed\")}");
        smokeSource.Should().Contain("find_dialog={FormatBool(snapshot.DialogEvidence.HasFindDialog)}");
        smokeSource.Should().Contain("find_dialog_text_box={FormatBool(snapshot.DialogEvidence.HasFindDialogTextBox)}");
        smokeSource.Should().Contain("find_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasFindDialogActionButtons)}");
        smokeSource.Should().Contain("find_dialog_options={FormatBool(snapshot.DialogEvidence.HasFindDialogOptions)}");
        smokeSource.Should().Contain("find_dialog_format_controls={FormatBool(snapshot.DialogEvidence.HasFindDialogFormatControls)}");
        smokeSource.Should().Contain("find_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasFindDialogCompactLayout)}");
        smokeSource.Should().Contain("replace_dialog={FormatBool(snapshot.DialogEvidence.HasReplaceDialog)}");
        smokeSource.Should().Contain("replace_dialog_text_boxes={FormatBool(snapshot.DialogEvidence.HasReplaceDialogTextBoxes)}");
        smokeSource.Should().Contain("replace_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasReplaceDialogActionButtons)}");
        smokeSource.Should().Contain("replace_dialog_options={FormatBool(snapshot.DialogEvidence.HasReplaceDialogOptions)}");
        smokeSource.Should().Contain("replace_dialog_format_controls={FormatBool(snapshot.DialogEvidence.HasReplaceDialogFormatControls)}");
        smokeSource.Should().Contain("replace_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasReplaceDialogCompactLayout)}");
        smokeSource.Should().Contain("go_to_dialog={FormatBool(snapshot.DialogEvidence.HasGoToDialog)}");
        smokeSource.Should().Contain("go_to_dialog_reference_controls={FormatBool(snapshot.DialogEvidence.HasGoToDialogReferenceControls)}");
        smokeSource.Should().Contain("go_to_dialog_history_controls={FormatBool(snapshot.DialogEvidence.HasGoToDialogHistoryControls)}");
        smokeSource.Should().Contain("go_to_dialog_special_control={FormatBool(snapshot.DialogEvidence.HasGoToDialogSpecialControl)}");
        smokeSource.Should().Contain("go_to_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasGoToDialogCompactLayout)}");
        smokeSource.Should().Contain("go_to_special_dialog={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialog)}");
        smokeSource.Should().Contain("go_to_special_dialog_kind_controls={FormatBool(snapshot.DialogEvidence.HasGoToSpecialKindControls)}");
        smokeSource.Should().Contain("go_to_special_dialog_value_type_controls={FormatBool(snapshot.DialogEvidence.HasGoToSpecialValueTypeControls)}");
        smokeSource.Should().Contain("go_to_special_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialogCompactLayout)}");
        smokeSource.Should().Contain("find_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasFindDialogClosedWithoutAccept)}");
        smokeSource.Should().Contain("replace_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasReplaceDialogClosedWithoutAccept)}");
        smokeSource.Should().Contain("go_to_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasGoToDialogClosedWithoutAccept)}");
        smokeSource.Should().Contain("go_to_special_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialogClosedWithoutAccept)}");
    }

    [Fact]
    public void MainWindow_WiresWorkbookStatisticsToSharedSizeNativeMenuDialog()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var shortcutCatalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs"));

        source.Should().Contain("private readonly NativeMenuItem _workbookStatisticsMenuItem = new();");
        source.Should().Contain("ConfigureNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics);");
        catalogSource.Should().Contain("\"AvaloniaNativeMenu_WorkbookStatistics\"");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.WorkbookStatistics)");
        source.Should().Contain("_workbookStatisticsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.WorkbookStatistics);");
        catalogSource.Should().Contain("FileItem(NativeFileMenuItemId.WorkbookStatistics)");
        catalogSource.Should().Contain("new(NativeFileMenuItemId.WorkbookStatistics, context.IsIdle)");
        source.Should().Contain("HasNativeWorkbookStatisticsMenuItem: HasNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics)");
        smokeSource.Should().Contain("bool HasNativeWorkbookStatisticsMenuItem,");
        smokeSource.Should().Contain("HasNativeWorkbookStatisticsMenuItem &&");
        smokeSource.Should().Contain("native_workbook_statistics_menu_item={FormatBool(snapshot.HasNativeWorkbookStatisticsMenuItem)}");
        AssertWorkbookShortcutCatalogRoute(shortcutCatalogSource, "WorkbookStatistics", "WorkbookShortcutKey.G", "WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift");
        AssertWorkbookShortcutRouteHandled(source, "WorkbookStatistics", "await ShowWorkbookStatisticsDialogAsync();");
        source.Should().Contain("private async Task ShowWorkbookStatisticsDialogAsync()");
        source.Should().Contain("WorkbookStatisticsService.GetStatistics(_session.Workbook)");
        source.Should().Contain("Title = \"Workbook Statistics\"");
        source.Should().Contain("Width = WorkbookStatisticsDialogPlanner.Width");
        source.Should().Contain("Height = WorkbookStatisticsDialogPlanner.Height");
        source.Should().Contain("MinWidth = WorkbookStatisticsDialogPlanner.MinWidth");
        source.Should().Contain("MinHeight = WorkbookStatisticsDialogPlanner.MinHeight");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"WorkbookStatisticsDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"WorkbookStatisticsOkButton\");");
        source.Should().Contain("CreateWorkbookStatisticsDialogContent(statistics, okButton, copyToClipboardButton)");
        source.Should().Contain("AutomationProperties.SetAutomationId(statisticsBlock, \"WorkbookStatisticsSummary\");");
        source.Should().Contain("Summarizes sheet, cell, formula, comment, and object counts for the workbook.");
        source.Should().Contain("private static string FormatWorkbookStatistics(WorkbookStatistics statistics)");
        source.Should().Contain("WorkbookStatisticsFormatter.Format(statistics);");
    }

    [Fact]
    public void MainWindow_KeepsMacOsCommandKeyMenuGesturesAndDirectInputRoutesAligned()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        source.Should().Contain("const KeyModifiers commandModifiers = KeyModifiers.Control | KeyModifiers.Meta;");
        source.Should().Contain("return (modifiers & commandModifiers) != 0 &&");
        source.Should().Contain("(modifiers & ~commandModifiers) == 0;");
        source.Should().Contain("return modifiers.HasFlag(KeyModifiers.Shift) &&");
        source.Should().Contain("(modifiers & ~(commandModifiers | KeyModifiers.Shift)) == 0;");

        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.NewWorkbook)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.OpenWorkbook)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.SaveWorkbook)");
        catalogSource.Should().Contain("NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift");
        catalogSource.Should().Contain("NativeMenuGestureKey.W, NativeMenuGestureModifiers.Meta");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Undo)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Redo)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Cut)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Copy)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Paste)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.PasteSpecial)");
        catalogSource.Should().Contain("NativeMenuGestureKey.A, NativeMenuGestureModifiers.Meta");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.Find)");
        catalogSource.Should().Contain("NativeMenuGestureKey.G, NativeMenuGestureModifiers.Meta");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.ToggleBold)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.ToggleItalic)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.ToggleUnderline)");
        catalogSource.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.OpenFormatCells)");
        catalogSource.Should().Contain("NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta");

        AssertWorkbookShortcutRouteHandled(source, "SelectPreviousSheetGroup", "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true)");
        AssertWorkbookShortcutRouteHandled(source, "SelectNextSheetGroup", "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true)");
        AssertWorkbookShortcutRouteHandled(source, "ActivatePreviousSheet", "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false)");
        AssertWorkbookShortcutRouteHandled(source, "ActivateNextSheet", "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false)");
        AssertWorkbookShortcutRouteHandled(source, "Find", "await ShowFindDialogAsync();");
        source.Should().Contain("e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers)");
        AssertWorkbookShortcutRouteHandled(source, "ToggleBold", "ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: e.Key == Key.B);");
        AssertWorkbookShortcutRouteHandled(source, "ToggleItalic", "ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: e.Key == Key.I);");
        AssertWorkbookShortcutRouteHandled(source, "ToggleUnderline", "ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: e.Key == Key.U);");
        source.Should().Contain("e.Key == Key.W && HasOnlyCommandModifier(e.KeyModifiers)");
    }

    [Fact]
    public void MainWindow_WiresNativeWindowMenuToMacOsWindowActionsAndLaunchSmoke()
    {
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var catalogSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));

        windowSource.Should().Contain("private readonly NativeMenuItem _minimizeWindowMenuItem = new();");
        windowSource.Should().Contain("private readonly NativeMenuItem _zoomWindowMenuItem = new();");
        windowSource.Should().Contain("private readonly NativeMenuItem _bringAllToFrontMenuItem = new();");
        catalogSource.Should().Contain("new(NativeMenuItemId.MinimizeWindow, \"Minimize\", new NativeMenuGesturePlan(NativeMenuGestureKey.M, NativeMenuGestureModifiers.Meta))");
        windowSource.Should().Contain("_minimizeWindowMenuItem.Click += (_, _) => WindowState = WindowState.Minimized;");
        catalogSource.Should().Contain("new(NativeMenuItemId.ZoomWindow, \"Zoom\", RequiresGestureInSmoke: false)");
        windowSource.Should().Contain("WindowState = WindowState == WindowState.Maximized");
        catalogSource.Should().Contain("new(NativeMenuItemId.BringAllToFront, \"Bring All to Front\", RequiresGestureInSmoke: false)");
        windowSource.Should().Contain("var windowMenu = CreateNativeMenu(NativeMenuTopLevelId.Window);");
        catalogSource.Should().Contain("public static IReadOnlyList<NativeMenuEntryPlan> WindowMenuEntries");
        catalogSource.Should().Contain("Item(NativeMenuItemId.MinimizeWindow)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.ZoomWindow)");
        catalogSource.Should().Contain("Item(NativeMenuItemId.BringAllToFront)");
        windowSource.Should().Contain("[NativeMenuTopLevelId.Window] = windowMenu,");
        windowSource.Should().Contain("HasNativeWindowMenu: hasNativeWindowMenu");
        windowSource.Should().Contain("HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, NativeMenuItemId.MinimizeWindow)");
        windowSource.Should().Contain("HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, NativeMenuItemId.ZoomWindow)");
        windowSource.Should().Contain("HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, NativeMenuItemId.BringAllToFront)");

        smokeSource.Should().Contain("bool HasNativeWindowMenu,");
        smokeSource.Should().Contain("bool HasNativeMinimizeWindowMenuItem,");
        smokeSource.Should().Contain("bool HasNativeZoomWindowMenuItem,");
        smokeSource.Should().Contain("bool HasNativeBringAllToFrontMenuItem,");
        smokeSource.Should().Contain("HasNativeWindowMenu &&");
        smokeSource.Should().Contain("HasNativeMinimizeWindowMenuItem &&");
        smokeSource.Should().Contain("HasNativeZoomWindowMenuItem &&");
        smokeSource.Should().Contain("HasNativeBringAllToFrontMenuItem &&");
        smokeSource.Should().Contain("native_window_menu={FormatBool(snapshot.HasNativeWindowMenu)}");
        smokeSource.Should().Contain("native_minimize_window_menu_item={FormatBool(snapshot.HasNativeMinimizeWindowMenuItem)}");
        smokeSource.Should().Contain("native_zoom_window_menu_item={FormatBool(snapshot.HasNativeZoomWindowMenuItem)}");
        smokeSource.Should().Contain("native_bring_all_to_front_menu_item={FormatBool(snapshot.HasNativeBringAllToFrontMenuItem)}");
    }

    [Fact]
    public void MainWindow_WiresConditionalFormatRuleAndManageDialogsToLaunchSmoke()
    {
        var cfSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.ConditionalFormat.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));

        // New Formatting Rule editor: rule-type picker, presets, per-type controls, automation ids.
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ConditionalFormatRuleDialog\");");
        cfSource.Should().Contain("Width = ConditionalFormatDialogCatalog.RuleEditorCaptureWidth");
        cfSource.Should().Contain("Height = ConditionalFormatDialogCatalog.RuleEditorCaptureHeight");
        cfSource.Should().Contain("MinWidth = ConditionalFormatDialogCatalog.RuleEditorMinWidth");
        cfSource.Should().Contain("MinHeight = ConditionalFormatDialogCatalog.RuleEditorMinHeight");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(ruleTypeBox, \"ConditionalFormatRuleTypeBox\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(presetBox, \"ConditionalFormatPresetBox\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(topBottomBox, \"ConditionalFormatTopBottomBox\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(minColorBox, \"ConditionalFormatMinColorBox\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(maxColorBox, \"ConditionalFormatMaxColorBox\");");
        cfSource.Should().Contain("ConditionalFormatPresetFactory.BuildInput(preset)");
        cfSource.Should().Contain("ConditionalFormatRuleBuilder.TryBuildApplyCommand(");

        // Manage Rules dialog: New / reorder / change applies-to, automation ids.
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"ManageConditionalFormatsDialog\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(moveUpButton, \"ManageConditionalFormatsMoveUpButton\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(moveDownButton, \"ManageConditionalFormatsMoveDownButton\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(appliesToBox, \"ManageConditionalFormatsAppliesToBox\");");
        cfSource.Should().Contain("AutomationProperties.SetAutomationId(applyAppliesToButton, \"ManageConditionalFormatsApplyAppliesToButton\");");
        // The Manage dialog now stages edits against a working copy and commits on OK (H32):
        // moves/applies-to mutate the working copy via the shared session, and OK replaces the
        // sheet's rules in one undoable command.
        cfSource.Should().Contain("new ManageConditionalFormatsSession(");
        cfSource.Should().Contain("manageSession.Move(item.Id, direction)");
        cfSource.Should().Contain("manageSession.ApplyRange(item.Id, range)");
        cfSource.Should().Contain("manageSession.CreateApplyCommand(_session.ActiveSheet.Id),");
        cfSource.Should().Contain("_session.TryResolveReferenceRange(reference, out var range)");

        // Launch-smoke probe wiring for both dialogs.
        cfSource.Should().Contain("private sealed record ConditionalFormatRuleDialogSmokeProbe(");
        cfSource.Should().Contain("internal sealed record ManageConditionalFormatsDialogSmokeProbe(");
        windowSource.Should().Contain("HasLaunchSmokeDialog(probe.Dialog, \"New Formatting Rule\")");
        windowSource.Should().Contain("width: ConditionalFormatDialogCatalog.RuleEditorCaptureWidth");
        windowSource.Should().Contain("height: ConditionalFormatDialogCatalog.RuleEditorCaptureHeight");
        windowSource.Should().Contain("minWidth: ConditionalFormatDialogCatalog.RuleEditorMinWidth");
        windowSource.Should().Contain("minHeight: ConditionalFormatDialogCatalog.RuleEditorMinHeight");
        windowSource.Should().Contain("HasLaunchSmokeDialog(probe.Dialog, UiText.Get(\"ManageConditionalFormats_ConditionalFormattingRulesManager\"))");
        windowSource.Should().Contain("HasLaunchSmokeText(AutomationProperties.GetName(probe.ListBox), UiText.Get(\"ManageConditionalFormats_ConditionalFormattingRules\"))");
        windowSource.Should().Contain("HasLaunchSmokeNamedButton(probe.MoveUpButton, \"ManageConditionalFormatsMoveUpButton\", UiText.Get(\"ManageConditionalFormats_MoveUp\"))");
        windowSource.Should().Contain("HasLaunchSmokeButton(probe.ApplyAppliesToButton, \"ManageConditionalFormatsApplyAppliesToButton\", UiText.Get(\"ManageConditionalFormats_Apply\"))");
        windowSource.Should().Contain("NormalizeLaunchSmokeText(string? text)");

        // Snapshot fields, IsPassed gating, and report lines for the two CF dialogs.
        smokeSource.Should().Contain("bool HasConditionalFormatRuleDialog = false,");
        smokeSource.Should().Contain("bool HasManageConditionalFormatsDialog = false,");
        smokeSource.Should().Contain("HasConditionalFormatRuleDialog &&");
        smokeSource.Should().Contain("HasManageConditionalFormatsReorderControls &&");
        smokeSource.Should().Contain("HasManageConditionalFormatsAppliesToControls &&");
        smokeSource.Should().Contain("conditional_format_rule_dialog={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleDialog)}");
        smokeSource.Should().Contain("manage_conditional_formats_dialog={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsDialog)}");
    }

    [Fact]
    public void MainWindow_WiresCustomViewsManagerAndAddDialogsThroughPortablePlanner()
    {
        var customViewsSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.CustomViews.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        // The View ▸ Workbook Views ▸ Custom Views ribbon button is wired to the dialog entry point.
        windowSource.Should().Contain("[\"Custom Views\"] = () => RunGuarded(OpenCustomViewsDialogAsync),");

        // Manager dialog + Add dialog carry stable automation ids.
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"CustomViewsDialog\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(viewsList, \"CustomViewsList\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(showButton, \"CustomViewsShowButton\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(addButton, \"CustomViewsAddButton\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(deleteButton, \"CustomViewsDeleteButton\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"CustomViewAddDialog\");");
        customViewsSource.Should().Contain("AutomationProperties.SetAutomationId(nameBox, \"CustomViewNameBox\");");
        customViewsSource.Should().Contain("Width = 640,");
        customViewsSource.Should().Contain("CreateCustomViewsHeader()");
        customViewsSource.Should().Contain("AddCustomViewsHeaderCell(header, 1, UiText.Get(\"CustomViews_Sheets\"));");
        customViewsSource.Should().Contain("new ColumnDefinitions(\"200,70,110,210\")");
        customViewsSource.Should().Contain("Children = { showButton, addButton, deleteButton, closeButton }");

        // All Custom Views logic flows through the portable planner + the shared session command path.
        customViewsSource.Should().Contain("CustomViewsPlanner.BuildRows(_session.Workbook)");
        customViewsSource.Should().Contain("CustomViewsPlanner.BuildApplyCommand(name)");
        customViewsSource.Should().Contain("CustomViewsPlanner.BuildSaveCommand(");
        customViewsSource.Should().Contain("CustomViewsPlanner.BuildDeleteCommand(name)");
        customViewsSource.Should().Contain("CustomViewsPlanner.ValidateName(_session.Workbook,");
        customViewsSource.Should().Contain("_session.ExecuteReviewCommand(");
        // Applying a view that changes the active sheet re-syncs the session's cached active sheet.
        customViewsSource.Should().Contain("ResyncActiveSheetToWorkbook();");
    }

    [Fact]
    public void GetData_FromTextCsv_RoutesThroughPortablePlannerAndImportCommand()
    {
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var getDataSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.GetData.cs"));

        // The Data-tab Get Data button + Refresh route to the new file-based import, not the old stubs.
        windowSource.Should().Contain("[\"data.getData\"] = GetDataFromText,");
        windowSource.Should().Contain("[\"data.refresh\"] = RefreshImportedData,");
        windowSource.Should().NotContain("GetDataNotSupported");
        windowSource.Should().NotContain("RefreshAllNotSupported");

        // The dialog gathers options and previews via the portable ImportDataPlanner.
        getDataSource.Should().Contain("private void GetDataFromText() => _ = ShowGetDataDialogAsync();");
        getDataSource.Should().Contain("ImportDataFilePickerPlanner.BuildTextOpenPickerPlan(UiText.Get(\"GetData_FileTypeName\"))");
        getDataSource.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        getDataSource.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(");
        getDataSource.Should().NotContain("Patterns = [\"*.csv\", \"*.tsv\", \"*.tab\", \"*.txt\"]");
        getDataSource.Should().Contain("ImportDataPlanner.DecodeBytes(bytes, encodingKind)");
        getDataSource.Should().Contain("ImportDataPlanner.PreviewText(decodedText, options");
        getDataSource.Should().Contain("ImportDataPlanner.ResolveDelimiter(options, decodedText)");

        // The parse reuses the existing delimited-text reader and applies via ImportSheetCommand on the
        // shared session command path; the source (including its resolved anchor) is remembered so
        // Refresh can re-run it back into the same anchor rather than the current selection.
        getDataSource.Should().Contain("new DelimitedTextFileAdapter(");
        getDataSource.Should().Contain("delimiter, allowSeparatorDirective, options.TreatConsecutiveDelimitersAsOne).Load(stream)");
        getDataSource.Should().Contain("new ImportSheetCommand(destination.Sheet, destination, sourceSheet)");
        getDataSource.Should().Contain("_session.ExecuteReviewCommand(command)");
        getDataSource.Should().Contain("_session.AddSheet()");
        getDataSource.Should().Contain("_lastImportSource = new ImportDataSource(filePath, options, resolvedDestination, destination)");
        getDataSource.Should().Contain("private void RefreshImportedData()");

        // User-facing strings go through UiText with the unique GetData_ key prefix.
        getDataSource.Should().Contain("UiText.Get(\"GetData_DialogTitle\")");
        getDataSource.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"GetDataDialog\");");
    }

    [Fact]
    public void PictureShapeSingleValueDialog_UsesValidAvaloniaMinimumHeight()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs"));

        source.Should().Contain("MinHeight = multiline ? 64 : 0");
        source.Should().NotContain("MinHeight = multiline ? 64 : double.NaN");
    }

    private static string ExtractSourceBlock(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"source should contain {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(0, $"source should contain {endMarker} after {startMarker}");
        return source[start..(end + endMarker.Length)];
    }

    private static void AssertWorkbookShortcutRouteHandled(string source, string routeName, params string[] expectedMarkers)
    {
        source.Should().Contain("TryHandleWorkbookShortcutRouteAsync(e)");
        source.Should().Contain("TryGetWorkbookShortcutRoute(e.Key, e.KeyModifiers, out var route)");
        source.Should().Contain("WorkbookApplicationCommandRouter.TryRouteShortcut(route, out var applicationRoute)");
        source.Should().Contain("WorkbookApplicationCommands.TryExecuteAsync(");
        source.Should().Contain("e.Handled = execution.Handled;");

        var bindingsSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ApplicationCommandRouting.cs"));
        var intentMarker = $"WorkbookApplicationCommandIntent.{routeName}";
        var start = bindingsSource.IndexOf(intentMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"application bindings should contain {intentMarker}");
        var end = bindingsSource.IndexOf("bindings.", start + intentMarker.Length, StringComparison.Ordinal);
        var routeBlock = end >= 0 ? bindingsSource[start..end] : bindingsSource[start..];

        foreach (var marker in expectedMarkers)
        {
            var bindingMarker = marker.StartsWith("await ", StringComparison.Ordinal)
                ? marker["await ".Length..]
                : marker;
            bindingMarker = bindingMarker
                .TrimEnd(';')
                .Replace("e.Key", "KeyArgs(invocation)?.Key", StringComparison.Ordinal);
            routeBlock.Should().Contain(bindingMarker);
        }
    }

    private static void AssertWorkbookShortcutCatalogRoute(string catalogSource, string routeName, params string[] expectedMarkers)
    {
        catalogSource.Should().Contain($"WorkbookShortcutRoute.{routeName}");
        foreach (var marker in expectedMarkers)
            catalogSource.Should().Contain(marker);
    }
}
