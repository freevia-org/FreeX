using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void MainWindowFileDrop_WiresWindowDropToWorkbookPlannerAndOpenFile()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var xaml = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.FileDrop.cs"));
        var planner = File.ReadAllText(Path.Combine(appHostDirectory, "WorkbookDropPlanner.cs"));

        xaml.Should().Contain("AllowDrop=\"True\"");
        xaml.Should().Contain("DragOver=\"MainWindow_DragOver\"");
        xaml.Should().Contain("Drop=\"MainWindow_Drop\"");
        source.Should().Contain("WorkbookDropPlanner.SelectOpenableFile(paths, _fileAdapters)");
        source.Should().Contain("await OpenFileAsync(path)");
        planner.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(adapters, extension, out _)");
    }

    [Fact]
    public void BackstageAndFileController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var backstageSourcePath = Path.Combine(appHostDirectory, "MainWindow.Backstage.cs");

        File.Exists(backstageSourcePath).Should().BeTrue();
        var backstageSource = File.ReadAllText(backstageSourcePath);

        mainSource.Should().NotContain("private void ShowStartScreen()");
        mainSource.Should().NotContain("private void UpdateSsRecentList(");
        mainSource.Should().NotContain("private async Task OpenFileAsync(");
        mainSource.Should().NotContain("private async void OpenButton_Click(");
        mainSource.Should().NotContain("private async Task<bool> SaveWorkbookWithDialogAsync()");

        backstageSource.Should().Contain("private void ShowStartScreen()");
        backstageSource.Should().Contain("private void UpdateSsRecentList(");
        backstageSource.Should().Contain("private async Task OpenFileAsync(");
        backstageSource.Should().Contain("private async void OpenButton_Click(");
        backstageSource.Should().Contain("private async Task<bool> SaveWorkbookWithDialogAsync()");
        backstageSource.Should().Contain("private async void SaveAsButton_Click(");
    }

    [Fact]
    public void BackstageSaveAs_ForcesSaveDialogInsteadOfExistingPathSave()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("MainWindow.xaml");
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        xaml.ShouldContainLocalizedAttribute("Text", "Save _As");
        xaml.Should().Contain("CommandName=\"Save As\"");
        xaml.Should().Contain("Click=\"SaveAsButton_Click\"");
        backstageSource.Should().Contain("private async void SaveAsButton_Click(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("await SaveWorkbookWithDialogAsync();");
        backstageSource.Should().Contain("HideStartScreen();");
    }

    [Fact]
    public void BackstageOpenAndSave_UseFormatDescriptorRegistry()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildSaveFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext, out var format)");
        backstageSource.Should().Contain("_currentFilePath = result.OpenedAsTemplate ? null : path;");
    }

    [Fact]
    public void BackstageOpenAndSaveDialogs_DeclareNativeDialogGuardrails()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("new Microsoft.Win32.OpenFileDialog");
        backstageSource.Should().Contain("Filter = filter");
        backstageSource.Should().Contain("CheckFileExists = true");
        backstageSource.Should().Contain("Multiselect = false");
        backstageSource.Should().Contain("if (dialog.ShowDialog() == true)");
        backstageSource.Should().Contain("await OpenFileAsync(dialog.FileName);");

        backstageSource.Should().Contain("new Microsoft.Win32.SaveFileDialog");
        backstageSource.Should().Contain("FileName = _workbook.Name");
        backstageSource.Should().Contain("var defaultExt = ResolveSaveDialogDefaultExtension();");
        backstageSource.Should().Contain("DefaultExt = defaultExt");
        backstageSource.Should().Contain("FilterIndex = FileDialogFilterBuilder.FindSaveFilterIndex(_fileAdapters, defaultExt)");
        backstageSource.Should().Contain("AddExtension = true");
        backstageSource.Should().Contain("OverwritePrompt = true");
        backstageSource.Should().Contain("FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ext, out _)");
        backstageSource.Should().Contain("FreeXOptions.NormalizeDefaultFormat(_options.DefaultFormat)");
        backstageSource.Should().Contain("return await SaveWorkbookToTargetAsync(new FileSaveTarget(dialog.FileName, adapter));");
    }

    [Fact]
    public void FileNewSaveSaveAsAndClose_RouteThroughDirtyPromptAndOwnedMessages()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var lifecycleSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs"));
        var keyboardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        var newMethod = ExtractMethodSource(backstageSource, "private async Task RequestNewWorkbookAsync()");
        newMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeCreatingWorkbook\"))");
        newMethod.Should().Contain("CreateNewWorkbook();");
        newMethod.Should().Contain("HideStartScreen();");

        var openMethod = ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        openMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeOpeningWorkbook\"))");
        openMethod.IndexOf("ConfirmSaveBeforeDestructiveActionAsync", StringComparison.Ordinal)
            .Should()
            .BeLessThan(openMethod.IndexOf("var loader = new OpenWorkbookLoader", StringComparison.Ordinal));
        openMethod.IndexOf("ConfirmSaveBeforeDestructiveActionAsync", StringComparison.Ordinal)
            .Should()
            .BeLessThan(openMethod.IndexOf("_workbook = result.Workbook;", StringComparison.Ordinal));

        var saveButtonMethod = ExtractMethodSource(backstageSource, "private async void SaveButton_Click(");
        saveButtonMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target)");
        saveButtonMethod.Should().Contain("await SaveWorkbookToTargetAsync(target!)");
        saveButtonMethod.Should().Contain("await SaveWorkbookWithDialogAsync();");

        var saveAsMethod = ExtractMethodSource(backstageSource, "private async void SaveAsButton_Click(");
        saveAsMethod.Should().Contain("await SaveWorkbookWithDialogAsync()");
        saveAsMethod.Should().Contain("HideStartScreen();");

        var saveTargetMethod = ExtractMethodSource(backstageSource, "private async Task<bool> SaveWorkbookToTargetAsync(");
        saveTargetMethod.Should().Contain("UiText.Get(\"Progress_SavingWorkbook\")");
        saveTargetMethod.Should().Contain("UiText.Get(\"Progress_SavingFilePreparing\")");
        saveTargetMethod.Should().Contain("MarkWorkbookSaved();");
        saveTargetMethod.Should().Contain("UiText.Format(\"MainWindowMessage_SaveFileFailed\", ex.Message)");
        saveTargetMethod.Should().Contain("UiText.Get(\"MainWindowMessage_SaveErrorTitle\")");
        saveTargetMethod.Should().Contain("finally");
        saveTargetMethod.Should().Contain("HideSaveProgress();");
        saveTargetMethod.Should().NotContain("MessageBox.Show(");

        var confirmMethod = ExtractMethodSource(lifecycleSource, "private async Task<bool> ConfirmSaveBeforeDestructiveActionAsync(");
        confirmMethod.Should().Contain("ShowOwnedMessage(");
        confirmMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target)");
        confirmMethod.Should().Contain("return await SaveWorkbookWithDialogAsync();");

        var closingMethod = ExtractMethodSource(lifecycleSource, "private async void MainWindow_Closing(");
        closingMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeClosingWorkbook\"))");
        closingMethod.Should().Contain("_suppressClosePrompt = true;");
        closingMethod.Should().Contain("Close();");

        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewWorkbook, async (_, _) => await RequestNewWorkbookAsync());");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, SaveButton_Click);");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveAs, async (_, _) => await SaveWorkbookWithDialogAsync());");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CloseWorkbook, (_, _) => Close());");
    }

    [Fact]
    public void BackstageOpen_FocusesHomeNavigationForKeyboardUsers()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("StartScreenOverlay.Visibility = Visibility.Visible;");
        backstageSource.Should().Contain("FocusBackstageHomeNavigation();");
        backstageSource.Should().Contain("private void FocusBackstageHomeNavigation()");
        backstageSource.Should().Contain("SsHomeNavBtn.Focus();");
        backstageSource.Should().Contain("Keyboard.Focus(SsHomeNavBtn);");
    }

    [Fact]
    public void BackstageSidebar_UpDownKeysMoveThroughNavigation()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        xaml.Should().Contain("PreviewKeyDown=\"StartScreenOverlay_PreviewKeyDown\"");
        xaml.Should().Contain("x:Name=\"StartScreenSidebar\"");
        backstageSource.Should().Contain("private void StartScreenOverlay_PreviewKeyDown(object sender, KeyEventArgs e)");
        backstageSource.Should().Contain("IsDescendantOf(focusedElement, StartScreenSidebar)");
        backstageSource.Should().Contain("e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)");
        backstageSource.Should().Contain("FocusNavigationDirection.Previous");
        backstageSource.Should().Contain("FocusNavigationDirection.Next");
        backstageSource.Should().Contain("focusedElement.MoveFocus(new TraversalRequest(direction));");
    }

    [Fact]
    public void BackstageSidebar_HomeEndKeysMoveToNavigationEdges()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)");
        backstageSource.Should().Contain("Key.Home => FocusNavigationDirection.First");
        backstageSource.Should().Contain("Key.End => FocusNavigationDirection.Last");
    }

    [Fact]
    public void BackstageOverlay_CyclesTabFocusWithinOverlay()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        xaml.Should().Contain("x:Name=\"StartScreenOverlay\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Cycle\"");
        xaml.Should().Contain("KeyboardNavigation.ControlTabNavigation=\"Cycle\"");
    }

    [Fact]
    public void BackstageContextMenu_UsesFocusedBackstageElementBeforeWorksheetFallback()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        selectionSource.Should().Contain("if (commandShortcut == KeyboardCommandShortcut.OpenContextMenu && TryOpenFocusedBackstageContextMenu())");
        selectionSource.IndexOf("TryOpenFocusedBackstageContextMenu()", StringComparison.Ordinal)
            .Should().BeLessThan(selectionSource.IndexOf("ExecuteCommandShortcut(commandShortcut, sender, e);", StringComparison.Ordinal));
        backstageSource.Should().Contain("private bool TryOpenFocusedBackstageContextMenu()");
        backstageSource.Should().Contain("!IsStartScreenVisible()");
        backstageSource.Should().Contain("Keyboard.FocusedElement is not FrameworkElement focusedElement");
        backstageSource.Should().Contain("!IsInsideStartScreenOverlay(focusedElement)");
        backstageSource.Should().Contain("focusedElement.ContextMenu is not { } menu");
        backstageSource.Should().Contain("menu.PlacementTarget = focusedElement;");
        backstageSource.Should().Contain("menu.IsOpen = true;");
    }

    [Fact]
    public void BackstageContextMenu_FocusesFirstEnabledMenuItem()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("menu.Opened += BackstageContextMenu_Opened;");
        backstageSource.Should().Contain("private static void BackstageContextMenu_Opened(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)");
        backstageSource.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void BackstageF6_CyclesWithinOverlayBeforeWorkbookShellFallback()
    {
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        const string backstageRoute = "if (IsStartScreenVisible() && TryHandleBackstageShellFocusCycle";
        const string workbookFallback = "ExecuteCommandShortcut(commandShortcut, this, e);";

        keyboardFocusSource.Should().Contain(backstageRoute);
        keyboardFocusSource.IndexOf(backstageRoute, StringComparison.Ordinal)
            .Should()
            .BeLessThan(keyboardFocusSource.IndexOf(workbookFallback, StringComparison.Ordinal));
        backstageSource.Should().Contain("private bool TryHandleBackstageShellFocusCycle(bool reverse)");
        backstageSource.Should().Contain("IsInsideStartScreenOverlay(focusedElement)");
        backstageSource.Should().Contain("StartScreenOverlay.MoveFocus");
    }

    [Fact]
    public void GetData_IncludesDelimitedTextAndSpreadsheetMlAdapters()
    {
        var dataCommandsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        dataCommandsSource.Should().Contain("\".csv\", \".txt\", \".tsv\", \".tab\", \".xml\"");
    }

    [Fact]
    public void GetData_CsvImportFlowGuardsNativeDialogAndRefreshesImportedCells()
    {
        var dataCommandsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(adapters)");
        dataCommandsSource.Should().Contain("new Microsoft.Win32.OpenFileDialog");
        dataCommandsSource.Should().Contain("Filter = filter");
        dataCommandsSource.Should().Contain("CheckFileExists = true");
        dataCommandsSource.Should().Contain("Multiselect = false");
        dataCommandsSource.Should().Contain("if (dialog.ShowDialog() != true) return;");
        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(adapters, ext, out var format)");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_failed\"");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_completed\"");
        dataCommandsSource.Should().Contain("new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0])");
        dataCommandsSource.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        dataCommandsSource.Should().Contain("SetActiveCell(destination);");
        dataCommandsSource.Should().Contain("EnsureCellVisible(destination);");
        dataCommandsSource.Should().Contain("RefreshStatusBar();");
        dataCommandsSource.Should().Contain("UiText.Get(\"MainWindowMessage_NoImportAdapters\")");
        dataCommandsSource.Should().Contain("ImportFailureDiagnosticFactory.FromException(ext, ex)");
        dataCommandsSource.Should().Contain("ShowOwnedMessage(diagnostic.UserMessage");
        dataCommandsSource.Should().Contain("errorDetail: diagnostic.Detail");
    }

    [Fact]
    public void RefreshAll_RoutesToCalculateNow()
    {
        var dataCommandsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        dataCommandsSource.Should().Contain("private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);");
    }

    [Fact]
    public void PrintAndExportController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var printSourcePath = Path.Combine(appHostDirectory, "MainWindow.PrintExport.cs");

        File.Exists(printSourcePath).Should().BeTrue();
        var printSource = File.ReadAllText(printSourcePath);

        mainSource.Should().NotContain("private void PrintButton_Click(");
        mainSource.Should().NotContain("private void ExportPdfButton_Click(");
        mainSource.Should().NotContain("private bool ExportAsPdf(");
        mainSource.Should().NotContain("private bool ExportAsXps(");

        printSource.Should().Contain("private void PrintButton_Click(");
        printSource.Should().Contain("private void ExportPdfButton_Click(");
        printSource.Should().Contain("private bool ExportAsPdf(");
        printSource.Should().Contain("private bool ExportAsXps(");
    }

    [Fact]
    public void ShareWorkbookWorkflow_RoutesUnsavedAndSavedFilesThroughPlannerAndShareService()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var reviewSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.ReviewCommands.cs"));
        var backstageSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.Backstage.cs"));
        var shareMethod = ExtractMethodSource(reviewSource, "private async Task ShareWorkbookAsync(");

        shareMethod.Should().Contain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        shareMethod.Should().Contain("ShareWorkbookPlanKind.SaveAsBeforeShare");
        shareMethod.Should().Contain("SaveWorkbookWithDialogAsync()");
        shareMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target)");
        shareMethod.Should().Contain("SaveWorkbookToTargetAsync(target!)");
        shareMethod.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");

        reviewSource.Should().Contain("private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();");
        backstageSource.Should().Contain("await ShareWorkbookAsync();");
    }

    [Fact]
    public void BackstageOpenProgressAndUnsupportedWarnings_UseOwnedDialogsAndRecoverOverlay()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var openMethod = ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        var saveWarningMethod = ExtractMethodSource(backstageSource, "private bool ConfirmUnsupportedXlsxFeatureSave()");
        var openWarningMethod = ExtractMethodSource(backstageSource, "private void ShowUnsupportedXlsxFeatureOpenWarningIfNeeded()");

        openMethod.Should().Contain("OpenWorkbookProgressPlanner.ProgressTitle()");
        openMethod.Should().Contain("OpenWorkbookProgressPlanner.FormatLoadingFileDetail(\"preparing\", TimeSpan.Zero)");
        openMethod.Should().Contain("ShowOpenProgress(update.Title, update.Detail, update.Percent)");
        openMethod.Should().Contain("OpenWorkbookProgressPlanner.FormatLoadingFileDetail(\"done\", TimeSpan.Zero)");
        openMethod.Should().Contain("ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();");
        openMethod.Should().Contain("UiText.Format(\"MainWindowMessage_OpenFileFailed\", ex.Message)");
        openMethod.Should().Contain("UiText.Get(\"MainWindowMessage_OpenErrorTitle\")");
        openMethod.Should().Contain("finally");
        openMethod.Should().Contain("HideOpenProgress();");
        openMethod.Should().Contain("_isOpeningFile = false;");
        openMethod.Should().NotContain("MessageBox.Show(");

        saveWarningMethod.Should().Contain("DeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport)");
        saveWarningMethod.Should().Contain("ShowOwnedMessage(");
        saveWarningMethod.Should().NotContain("MessageBox.Show(");

        openWarningMethod.Should().Contain("DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(_currentXlsxFeatureReport)");
        openWarningMethod.Should().Contain("ShowOwnedMessage(");
        openWarningMethod.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void OnlineTemplatesExcludedCommand_UsesOwnedMessageRoute()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var methodStart = backstageSource.IndexOf("private void SsMoreTemplatesBtn_Click", StringComparison.Ordinal);
        var nextMethodStart = backstageSource.IndexOf("private void SsOptionsBtn_Click", methodStart, StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        nextMethodStart.Should().BeGreaterThan(methodStart);

        var method = backstageSource[methodStart..nextMethodStart];
        method.Should().Contain("DeferredCommandMessages.OnlineTemplatesExcluded()");
        method.Should().Contain("ShowOwnedMessage(");
        method.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void ExportWorkflow_SurfacesPlannedPdfAndXpsPaths()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("ExportAsPdf(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        source.Should().Contain("ExportAsXps(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        source.Should().Contain("var document = RenderExportDocument(effectiveOptions)");
        source.Should().Contain("var paginator = RenderExportPaginator(effectiveOptions)");
        source.Should().Contain("ExportPlanner.DescribeRequest(request)");
        source.Should().Contain("OpenExportedFile(request.ActualPath)");
        source.Should().NotContain("ExportPdfFallbackAsXps");
    }

    [Fact]
    public void ExportPdfXpsSaveDialog_DeclaresNativeGuardrailsAndOwnedMessages()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PrintExport.cs"));
        var exportMethod = ExtractMethodSource(source, "private void ExportPdfButton_Click(");
        var exportPdfMethod = ExtractMethodSource(source, "private bool ExportAsPdf(");
        var exportXpsMethod = ExtractMethodSource(source, "private bool ExportAsXps(");

        exportMethod.Should().Contain("new Microsoft.Win32.SaveFileDialog");
        exportMethod.Should().Contain("Title      = UiText.Get(\"MainWindowDialog_ExportPdfXpsTitle\")");
        exportMethod.Should().Contain("Filter     = UiText.Get(\"MainWindowDialog_ExportPdfXpsFilter\")");
        exportMethod.Should().Contain("DefaultExt = \".pdf\"");
        exportMethod.Should().Contain("AddExtension = true");
        exportMethod.Should().Contain("OverwritePrompt = true");
        exportMethod.Should().Contain("var selectedFormat = saveDlg.FilterIndex == 2");
        exportMethod.Should().Contain("ExportPlanner.PlanExport(saveDlg.FileName, selectedFormat, optionsDialog.Result)");
        exportMethod.Should().Contain("ExportPlanner.TryValidatePublishOptions(request.Options, request.Format, out var publishOptionsError)");
        exportMethod.Should().Contain("publishOptionsError ?? UiText.Get(\"MainWindowMessage_ExportUnsupportedOptions\")");
        exportMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportOptionsTitle\")");
        exportMethod.Should().Contain("ShowOwnedMessage(");
        exportMethod.Should().Contain("OpenExportedFile(request.ActualPath)");
        exportMethod.Should().NotContain("MessageBox.Show(");

        exportPdfMethod.Should().Contain("PdfDocumentProperties.FromWorkbook(_workbook, effectiveOptions)");
        exportPdfMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportPdfSavedFormat\", optionSummary, pdfPath)");
        exportPdfMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportPdfTitle\")");
        exportPdfMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportPdfFailed\", ex.Message)");
        exportPdfMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportErrorTitle\")");
        exportPdfMethod.Should().Contain("ShowOwnedMessage(");
        exportPdfMethod.Should().NotContain("MessageBox.Show(");

        exportXpsMethod.Should().Contain("XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, effectiveOptions))");
        exportXpsMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportXpsSavedFormat\", xpsPath)");
        exportXpsMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportXpsSavedWithOptionsFormat\", optionSummary, xpsPath)");
        exportXpsMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportXpsTitle\")");
        exportXpsMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportXpsFailed\", ex.Message)");
        exportXpsMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportErrorTitle\")");
        exportXpsMethod.Should().Contain("ShowOwnedMessage(");
        exportXpsMethod.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void CtrlP_RoutesThroughBackstagePrintEntryPoint()
    {
        var keyboardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        backstageSource.Should().Contain("private void OpenPrintBackstage()");
        backstageSource.Should().Contain("SsPrintNavBtn.Focus();");
        backstageSource.Should().Contain("PrintButton_Click(SsPrintNavBtn, new RoutedEventArgs())");
        keyboardSource.Should().Contain("KeyboardCommandShortcut.OpenPrintPreview, (_, _) => OpenPrintBackstage()");
        keyboardSource.Should().NotContain("KeyboardCommandShortcut.OpenPrintPreview, PrintButton_Click");
        xaml.Should().Contain("x:Name=\"SsPrintNavBtn\"");
        xaml.ShouldContainLocalizedAttribute("local:RibbonTooltip.Description", "Open the print preview and native print dialog for the rendered worksheet.");
    }

    [Fact]
    public void BackstagePrint_OpensPreviewWithSettingsAndNativePrintPath()
    {
        var printSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PrintExport.cs"));
        var previewSource =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Helpers.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Layout.cs"));

        printSource.Should().Contain("var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);");
        printSource.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printSource.Should().Contain("new PrintPreviewDialog(");
        printSource.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        previewSource.Should().Contain("Content = UiText.Get(\"PrintPreview_PrintButton\")");
        previewSource.Should().Contain("ShowNativePrintDialog");
        previewSource.Should().Contain("PrintDocument(paginator");
    }
}
