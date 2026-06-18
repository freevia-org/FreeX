using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void MainWindowFileDrop_WiresWindowDropToWorkbookPlannerAndOpenFile()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FileDrop.cs");
        var planner = DialogSourceTestSupport.ReadHostSources("WorkbookDropPlanner.cs");

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
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

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
        // The Save As handler still forces the dialog path (not the existing-path save) and closes the
        // backstage — asserted on the unchanged MainWindow.Backstage.cs source. The rail button that fires
        // it now lives on the shared frame, so its presence/keytip is asserted behaviourally.
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        backstageSource.Should().Contain("private async void SaveAsButton_Click(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("await SaveWorkbookWithDialogAsync();");
        backstageSource.Should().Contain("HideStartScreen();");

        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var saveAs = harness.RailButton("BackstageSaveAsButton");
            saveAs.Should().NotBeNull("Save As is a first-class rail command");
            harness.KeyTip(saveAs!).Should().Be("A");
        });
    }

    [Fact]
    public void BackstageOpenAndSave_UseFormatDescriptorRegistry()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildSaveFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext, out var format)");
        backstageSource.Should().Contain("_currentFilePath = result.OpenedAsTemplate ? null : path;");
    }

    [Fact]
    public void BackstageOpenAndSaveDialogs_DeclareNativeDialogGuardrails()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

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
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        var newMethod = ExtractMethodSource(backstageSource, "private async Task RequestNewWorkbookAsync()");
        newMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeCreatingWorkbook\"))");
        newMethod.Should().Contain("== SaveChangesConfirmation.Cancel");
        newMethod.Should().Contain("CreateNewWorkbook();");
        newMethod.Should().Contain("HideStartScreen();");

        var openMethod = ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        openMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeOpeningWorkbook\"))");
        openMethod.Should().Contain("== SaveChangesConfirmation.Cancel");
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
        saveButtonMethod.Should().Contain("if (saved && IsStartScreenVisible())");
        saveButtonMethod.Should().Contain("HideStartScreen();");

        var saveAsMethod = ExtractMethodSource(backstageSource, "private async void SaveAsButton_Click(");
        saveAsMethod.Should().Contain("await SaveWorkbookWithDialogAsync()");
        saveAsMethod.Should().Contain("HideStartScreen();");

        var saveTargetMethod = ExtractMethodSource(backstageSource, "private async Task<bool> SaveWorkbookToTargetAsync(");
        saveTargetMethod.Should().Contain("FileSavePlanner.CanSkipCleanSave(_workbookDirty, _currentFilePath, target)");
        saveTargetMethod.IndexOf("FileSavePlanner.CanSkipCleanSave", StringComparison.Ordinal)
            .Should()
            .BeLessThan(saveTargetMethod.IndexOf("ConfirmUnsupportedXlsxFeatureSave()", StringComparison.Ordinal));
        saveTargetMethod.Should().Contain("UiText.Get(\"Progress_SavingWorkbook\")");
        saveTargetMethod.Should().Contain("UiText.Get(\"Progress_SavingFilePreparing\")");
        saveTargetMethod.Should().Contain("MarkWorkbookSaved();");
        saveTargetMethod.Should().Contain("UiText.Format(\"MainWindowMessage_SaveFileFailed\", ex.Message)");
        saveTargetMethod.Should().Contain("UiText.Get(\"MainWindowMessage_SaveErrorTitle\")");
        saveTargetMethod.Should().Contain("finally");
        saveTargetMethod.Should().Contain("HideSaveProgress();");
        saveTargetMethod.Should().NotContain("MessageBox.Show(");

        var confirmMethod = ExtractMethodSource(lifecycleSource, "private async Task<SaveChangesConfirmation> ConfirmSaveBeforeDestructiveActionAsync(");
        confirmMethod.Should().Contain("ShowOwnedMessage(");
        confirmMethod.Should().Contain("SaveChangesConfirmation.DiscardWithoutSaving");
        confirmMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target)");
        confirmMethod.Should().Contain("return await SaveWorkbookWithDialogAsync()");

        var closingMethod = ExtractMethodSource(lifecycleSource, "private async void MainWindow_Closing(");
        closingMethod.Should().Contain("ConfirmSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeClosingWorkbook\"))");
        closingMethod.Should().Contain("_suppressClosePrompt = true;");
        closingMethod.Should().Contain("PrepareActiveWorkbookForFinalClose();");
        closingMethod.Should().Contain("_ = Dispatcher.BeginInvoke(new Action(Close));");

        var finalCloseMethod = ExtractMethodSource(lifecycleSource, "private void PrepareActiveWorkbookForFinalClose()");
        finalCloseMethod.Should().Contain("ReleaseWorkbookUiStateForClose();");
        finalCloseMethod.IndexOf("ReleaseWorkbookUiStateForClose();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(finalCloseMethod.IndexOf("if (!IsFinalWorkbookWindowClose())", StringComparison.Ordinal));
        finalCloseMethod.Should().Contain("XlsxFileAdapter.ForgetLoadedPackageSnapshot(_workbook);");
        finalCloseMethod.Should().Contain("_workbookRef.Current = replacement;");

        var releaseUiMethod = ExtractMethodSource(lifecycleSource, "private void ReleaseWorkbookUiStateForClose()");
        releaseUiMethod.Should().Contain("ClearFormulaReferenceHighlights();");
        releaseUiMethod.Should().Contain("ClearClipboardVisualState();");
        releaseUiMethod.Should().Contain("_internalClipboard = null;");
        releaseUiMethod.Should().Contain("SheetGrid.Viewport = null;");
        releaseUiMethod.Should().Contain("SheetGrid.Charts = null;");
        releaseUiMethod.Should().Contain("SheetGrid.Pictures = null;");
        releaseUiMethod.Should().Contain("SheetGrid.NativeSlicers = null;");
        releaseUiMethod.Should().Contain("_sheetTabs.Clear();");
        releaseUiMethod.Should().Contain("SheetTabsControl.ItemsSource = null;");
        releaseUiMethod.Should().Contain("PivotAvailableFieldsList.ItemsSource = null;");
        releaseUiMethod.Should().Contain("SlicerItemsControl.ItemsSource = null;");
        releaseUiMethod.Should().Contain("TimelineItemsControl.ItemsSource = null;");
        releaseUiMethod.Should().Contain("_lastViewportSlicerTimelineRefreshKey = null;");

        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewWorkbook, async (_, _) => await RequestNewWorkbookAsync());");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, SaveButton_Click);");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveAs, async (_, _) => await SaveWorkbookWithDialogAsync());");
        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CloseWorkbook, (_, _) => Close());");
    }

    [Fact]
    public void BackstageOpen_FocusesHomeNavigationForKeyboardUsers()
    {
        // Opening the backstage shows the overlay, lands on the Home pane, and gives keyboard focus to the
        // Home rail entry so keyboard users start on a deterministic anchor.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            harness.IsBackstageVisible.Should().BeTrue();
            harness.ContentHostShows("SsHomeView").Should().BeTrue("Home is the default landing pane");
            harness.IsRailButtonFocused("BackstageHomeButton").Should().BeTrue("keyboard focus starts on Home");
        });
    }

    [Fact]
    public void BackstageSidebar_UpDownKeysMoveThroughNavigation()
    {
        // The shared frame owns rail arrow navigation now: Down moves to the next entry, Up to the
        // previous. Assert the behaviour through the live focus tree rather than handler-name source text.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();
            harness.FocusRailButton("BackstageHomeButton");

            harness.PressKeyOnFrame(Key.Down);
            harness.IsRailButtonFocused("BackstageHomeButton")
                .Should().BeFalse("Down moves focus off the first entry");

            harness.FocusRailButton("BackstageNewButton");
            harness.PressKeyOnFrame(Key.Up);
            harness.IsRailButtonFocused("BackstageNewButton")
                .Should().BeFalse("Up moves focus off the current entry toward the previous one");
        });
    }

    [Fact]
    public void BackstageSidebar_HomeEndKeysMoveToNavigationEdges()
    {
        // Home jumps to the first rail entry (the Back arrow), End jumps to the last (Options).
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();
            harness.FocusRailButton("BackstageNewButton");

            harness.PressKeyOnFrame(Key.End);
            harness.IsRailButtonFocused("BackstageOptionsButton")
                .Should().BeTrue("End moves focus to the last (bottom-docked) rail entry");

            harness.PressKeyOnFrame(Key.Home);
            harness.IsRailButtonFocused("BackstageBackButton")
                .Should().BeTrue("Home moves focus to the first rail entry (the Back arrow)");
        });
    }

    [Fact]
    public void BackstageOverlay_CyclesTabFocusWithinOverlay()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        xaml.Should().Contain("x:Name=\"StartScreenOverlay\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Cycle\"");
        xaml.Should().Contain("KeyboardNavigation.ControlTabNavigation=\"Cycle\"");
    }

    [Fact]
    public void BackstageContextMenu_UsesFocusedBackstageElementBeforeWorksheetFallback()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

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
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        backstageSource.Should().Contain("menu.Opened += BackstageContextMenu_Opened;");
        backstageSource.Should().Contain("private static void BackstageContextMenu_Opened(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("foreach (var item in menu.Items)");
        backstageSource.Should().Contain("if (item is not MenuItem menuItem || !menuItem.IsEnabled)");
        backstageSource.Should().Contain("firstEnabledItem = menuItem;");
        backstageSource.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void BackstageF6_CyclesWithinOverlayBeforeWorkbookShellFallback()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

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
        var dataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        dataCommandsSource.Should().Contain("\".csv\", \".txt\", \".tsv\", \".tab\", \".xml\"");
    }

    [Fact]
    public void GetData_CsvImportFlowGuardsNativeDialogAndRefreshesImportedCells()
    {
        var dataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(adapters)");
        dataCommandsSource.Should().Contain("new Microsoft.Win32.OpenFileDialog");
        dataCommandsSource.Should().Contain("Filter = filter");
        dataCommandsSource.Should().Contain("CheckFileExists = true");
        dataCommandsSource.Should().Contain("Multiselect = false");
        dataCommandsSource.Should().Contain("if (dialog.ShowDialog() != true) return;");
        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(adapters, ext, out var format)");
        dataCommandsSource.Should().Contain("private async void GetDataBtn_Click(object sender, RoutedEventArgs e)");
        dataCommandsSource.Should().Contain("await Task.Run(() =>");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_failed\"");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_completed\"");
        dataCommandsSource.Should().Contain("new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0])");
        dataCommandsSource.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        dataCommandsSource.Should().Contain("SetActiveCell(destination);");
        dataCommandsSource.Should().Contain("EnsureCellVisible(destination);");
        dataCommandsSource.Should().Contain("UpdateViewport();");
        dataCommandsSource.Should().Contain("RefreshStatusBar();");
        dataCommandsSource.Should().Contain("UiText.Get(\"MainWindowMessage_NoImportAdapters\")");
        dataCommandsSource.Should().Contain("ImportFailureDiagnosticFactory.FromException(ext, ex)");
        dataCommandsSource.Should().Contain("ShowOwnedMessage(diagnostic.UserMessage");
        dataCommandsSource.Should().Contain("errorDetail: diagnostic.Detail");
    }

    [Fact]
    public void RefreshAll_RoutesToCalculateNow()
    {
        var dataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        dataCommandsSource.Should().Contain("private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);");
    }

    [Fact]
    public void PrintAndExportController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var printSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

        mainSource.Should().NotContain("void PrintButton_Click(");
        mainSource.Should().NotContain("void ExportPdfButton_Click(");
        mainSource.Should().NotContain("ExportAsPdf(");
        mainSource.Should().NotContain("ExportAsXps(");

        printSource.Should().Contain("private void PrintButton_Click(");
        printSource.Should().Contain("private async void ExportPdfButton_Click(");
        printSource.Should().Contain("private async Task<bool> ExportAsPdf(");
        printSource.Should().Contain("private async Task<bool> ExportAsXps(");
    }

    [Fact]
    public void ShareWorkbookWorkflow_RoutesUnsavedAndSavedFilesThroughPlannerAndShareService()
    {
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var shareMethod = ExtractMethodSource(reviewSource, "private async Task ShareWorkbookAsync(");

        shareMethod.Should().Contain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        shareMethod.Should().Contain("ShareWorkbookPlanKind.SaveAsBeforeShare");
        shareMethod.Should().Contain("SaveWorkbookWithDialogAsync()");
        shareMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target)");
        shareMethod.Should().Contain("SaveWorkbookToTargetAsync(target!)");
        shareMethod.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");

        reviewSource.Should().Contain("private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();");

        // The backstage Share rail entry now lives on the shared frame and routes to ShareWorkbookAsync from
        // the FreeX frame wrapper instead of a SsShareBtn_Click forwarder.
        var frameSource = DialogSourceTestSupport.ReadHostSources("MainWindow.BackstageFrame.cs");
        frameSource.Should().Contain("await ShareWorkbookAsync()");
    }

    [Fact]
    public void BackstageOpenProgressAndUnsupportedWarnings_UseOwnedDialogsAndRecoverOverlay()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
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
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");
        var exportMethod = ExtractMethodSource(source, "private async void ExportPdfButton_Click(");
        var exportPdfMethod = ExtractMethodSource(source, "private async Task<bool> ExportAsPdf(");
        var exportXpsMethod = ExtractMethodSource(source, "private async Task<bool> ExportAsXps(");

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
        // Ctrl+P routes through OpenPrintBackstage -> ShowPrintView (the print pane preview), not a stale
        // PrintButton_Click. The print-pane content (preview viewer, options host, Print Now button) is
        // unchanged XAML and still asserted on the markup; the rail Print entry is asserted behaviourally.
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        backstageSource.Should().Contain("private void OpenPrintBackstage()");
        backstageSource.Should().Contain("RefreshBackstagePrintPreview();");
        backstageSource.Should().Contain("ShowPrintView();");
        backstageSource.Should().NotContain("PrintButton_Click(SsPrintNavBtn, new RoutedEventArgs())");
        keyboardSource.Should().Contain("KeyboardCommandShortcut.OpenPrintPreview, (_, _) => OpenPrintBackstage()");
        keyboardSource.Should().NotContain("KeyboardCommandShortcut.OpenPrintPreview, PrintButton_Click");
        // Print-pane content (kept verbatim through the migration):
        xaml.Should().Contain("x:Name=\"SsBackstagePrintNowButton\"");
        xaml.Should().Contain("x:Name=\"SsPrintOptionsHost\"");
        xaml.Should().Contain("x:Name=\"SsPrintPreviewViewer\"");
        xaml.Should().Contain("Click=\"BackstagePrintNowButton_Click\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"BackstagePrintPreviewViewer\"");
        xaml.Should().NotContain("x:Name=\"SsPrintPreviewButton\"");

        // Behaviour: the Ctrl+P entry point lands the backstage on the Print pane via the shared rail.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.Invoke("OpenPrintBackstage");

            harness.IsBackstageVisible.Should().BeTrue();
            harness.ContentHostShows("SsPrintView").Should().BeTrue("Ctrl+P lands on the Print pane");
            var print = harness.RailButton("BackstagePrintButton");
            print.Should().NotBeNull();
            harness.AutomationName(print!).Should().Be(UiText.Get("MainWindow_AutomationName_Print"));
        });
    }

    [Fact]
    public void BackstagePrint_OpensPreviewWithSettingsAndNativePrintPath()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var printSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");
        var previewSource = DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            string.Empty,
            "PrintPreviewDialog.cs",
            "PrintPreviewDialog.Helpers.cs",
            "PrintPreviewDialog.Layout.cs",
            "NativePrintDialogService.cs");

        backstageSource.Should().Contain("SsPrintPreviewViewer.Document = refreshed.Document;");
        backstageSource.Should().Contain("SsPrintOptionsHost.Content = PrintPreviewSettingsPanelFactory.Build(");
        backstageSource.Should().Contain("NativePrintDialogService.ShowPrintDialogAndPrint(");
        printSource.Should().Contain("var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);");
        printSource.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printSource.Should().Contain("new PrintPreviewDialog(");
        printSource.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        previewSource.Should().Contain("Content = UiText.Get(\"PrintPreview_PrintButton\")");
        previewSource.Should().Contain("ShowNativePrintDialog");
        previewSource.Should().Contain("Forms.PrintDialog");
        previewSource.Should().Contain("PrintDocument(paginator");
    }
}
