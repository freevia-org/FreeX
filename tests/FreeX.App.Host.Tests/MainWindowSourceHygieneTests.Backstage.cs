using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void MainWindowFileDrop_WiresWindowDropToWorkbookPlannerAndOpenFile()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FileDrop.cs");
        var planner = DialogSourceTestSupport.ReadAppServicesSource("WorkbookOpenIngressPlanner.cs");

        xaml.Should().Contain("AllowDrop=\"True\"");
        xaml.Should().Contain("DragOver=\"MainWindow_DragOver\"");
        xaml.Should().Contain("Drop=\"MainWindow_Drop\"");
        source.Should().Contain("WorkbookOpenIngressPlanner.SelectOpenableFile(paths, _fileAdapters)");
        source.Should().Contain("await OpenFileAsync(path)");
        planner.Should().Contain("WorkbookOpenTargetPlanner.TryCreateOpenTarget(adapters, path");
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
    public void FreeXBackstageRailEntries_UsePresentationFramePlanner()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var frameSource = DialogSourceTestSupport.ReadHostSources("MainWindow.BackstageFrame.cs");
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("Backstage", "FreeXBackstageFramePlanner.cs");
        var buildMethod = ExtractMethodSource(frameSource, "private IEnumerable<BackstageEntry> BuildBackstageEntries()");

        frameSource.Should().Contain("BackstageFrameComposer.Build(");
        frameSource.Should().Contain("new BackstageFrameComposerSpec(");
        frameSource.Should().Contain("DecorateNavButtons = DecorateBackstageNavButton");
        frameSource.Should().Contain("Closed = OnBackstageFrameClosed");
        frameSource.Should().Contain("private static readonly FreeXBackstageFramePlan BackstageFramePlan = FreeXBackstageFramePlanner.Build();");
        frameSource.Should().NotContain("new BackstageFrame()");
        frameSource.Should().NotContain("frame.SetEntries(");
        buildMethod.Should().Contain("BackstageFramePlan.Entries.Select(MapBackstageFrameEntry)");
        buildMethod.Should().NotContain("UiText.Get(\"MainWindow_Text_Home\")");
        buildMethod.Should().NotContain("UiText.Get(\"MainWindow_Text_SaveAs\")");
        buildMethod.Should().NotContain("BackstageSaveAsButton");
        buildMethod.Should().NotContain("BackstageAccountButton");

        var mapMethod = ExtractMethodSource(frameSource, "private BackstageEntry MapBackstageFrameEntry(");
        mapMethod.Should().Contain("SisterBackstageEntryPlan<UIElement>");
        mapMethod.Should().Contain("StableId = entry.StableId");
        mapMethod.Should().Contain("WpfBackstageEntryProjection.FromPlan(mapped with");
        mapMethod.Should().NotContain("BackstageEntry.Pane(");
        mapMethod.Should().NotContain("BackstageEntry.Command(");

        frameSource.Should().Contain("RequirePaneFlow(entry)");
        frameSource.Should().Contain("RequireCommandWorkflow(entry)");
        frameSource.Should().Contain("BuildBackstagePane(FreeXBackstagePaneFlowPlan plan)");
        frameSource.Should().Contain("FreeXBackstageCommandWorkflowExecutor.ExecuteAsync(");
        frameSource.Should().Contain("CreateBackstageCommandHandlers()");
        frameSource.Should().NotContain("ResolveBackstageCommand(FreeXBackstageCommandId command)");
        frameSource.Should().NotContain("FreeXBackstageFlowPlanner.BuildCommandWorkflow(");
        frameSource.Should().NotContain("FreeXBackstageCommandWorkflowKind.NewWorkbook");
        frameSource.Should().NotContain("FreeXBackstageCommandWorkflowKind.ExportWorkbook");
        frameSource.Should().NotContain("ResolveBackstagePane(FreeXBackstagePaneId pane)");
        frameSource.Should().NotContain("FreeXBackstageFlowPlanner.BuildPaneFlow(");
        frameSource.Should().NotContain("private const string BackstageHomePaneId");

        backstageSource.Should().Contain("BackstageFramePlan.Selection.DefaultPaneAutomationId");
        backstageSource.Should().Contain("ShowBackstagePane(FreeXBackstagePaneId.Info)");
        backstageSource.Should().Contain("BackstageFramePlan.Selection.For(pane)");
        backstageSource.Should().NotContain("BackstageHomePaneId");
        backstageSource.Should().NotContain("BackstageInfoPaneId");
        backstageSource.Should().NotContain("BackstagePrintPaneId");

        plannerSource.Should().Contain("FreeXBackstageNavigationPlanner.Build()");
        plannerSource.Should().Contain("FreeXBackstageFlowPlanner.BuildPaneFlow(pane)");
        plannerSource.Should().Contain("FreeXBackstageFlowPlanner.BuildCommandWorkflow(command)");
        plannerSource.Should().Contain("FreeXBackstagePaneSelectionPlan");
    }

    [Fact]
    public void FreeXBackstageRail_UsesPlannerStableIdsForSelectionIdentity()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            harness.CurrentEntryId.Should().Be(FreeXBackstageFramePlanner.GetPaneStableId(
                FreeXBackstagePaneId.Home));

            harness.Invoke("ShowInfoView");

            harness.CurrentEntryId.Should().Be(FreeXBackstageFramePlanner.GetPaneStableId(
                FreeXBackstagePaneId.Info));
        });
    }

    [Fact]
    public void BackstageSaveAs_ForcesSaveDialogInsteadOfExistingPathSave()
    {
        // The Save As handler still forces the dialog path (not the existing-path save) and closes the
        // backstage — asserted on the unchanged MainWindow.Backstage.cs source. The rail button that fires
        // it now lives on the shared frame, so its presence/keytip is asserted behaviourally.
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        backstageSource.Should().Contain("private async void SaveAsButton_Click(object sender, RoutedEventArgs e)");
        // Save As forces the Save-As dialog directly (it does NOT route through the shared
        // SaveResolvedAsync existing-path resolution that Save uses), then closes the backstage.
        var saveAsMethod = ExtractMethodSource(backstageSource, "private async void SaveAsButton_Click(");
        saveAsMethod.Should().Contain("await SaveWorkbookWithDialogAsync()");
        saveAsMethod.Should().NotContain("SaveResolvedAsync");
        saveAsMethod.Should().Contain("HideStartScreen();");

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
        var pickerPlannerSource = DialogSourceTestSupport.ReadAppServicesSource("WorkbookFilePickerPlanner.cs");
        var sessionSource = DialogSourceTestSupport.ReadAppServicesSource("WorkbookSession.cs");

        backstageSource.Should().Contain("WorkbookFilePickerPlanner.BuildOpenDialogPlan(_fileAdapters)");
        backstageSource.Should().Contain("WorkbookFilePickerPlanner.BuildSaveDialogPlan(");
        pickerPlannerSource.Should().Contain("FileDialogRequestPlanner.BuildOpenDialogPlan(");
        pickerPlannerSource.Should().Contain("FileDialogRequestPlanner.BuildSaveDialogPlan(");
        backstageSource.Should().Contain("_fileWorkflow.TryResolveOpenTarget(path");
        backstageSource.Should().Contain("_fileWorkflow.OpenAsync(");
        backstageSource.Should().NotContain("WorkbookFileCompletionPlanner.PlanOpen(");
        backstageSource.Should().Contain("SourcePath: plan.SourcePath");
        sessionSource.Should().Contain("_documentState.SetCurrentFilePath(source.OpenedAsTemplate ? null : source.SourcePath);");
    }

    [Fact]
    public void BackstageOpenAndSaveDialogs_DeclareNativeDialogGuardrails()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        backstageSource.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        backstageSource.Should().Contain("plan.Filter,");
        backstageSource.Should().Contain("plan.DefaultExtensionWithDot");
        backstageSource.Should().Contain("checkFileExists: true");
        backstageSource.Should().Contain("multiselect: false");
        backstageSource.Should().Contain("if (result.Chosen)");
        backstageSource.Should().Contain("await OpenFileAsync(result.FileName!);");

        backstageSource.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        backstageSource.Should().Contain("plan.SuggestedFileName");
        backstageSource.Should().Contain("plan.DefaultExtensionWithDot");
        backstageSource.Should().Contain("plan.FilterIndex");
        backstageSource.Should().Contain("_fileWorkflow.TryResolveSaveTarget(");
        backstageSource.Should().Contain("return await SaveWorkbookToTargetAsync(target);");
        backstageSource.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
        backstageSource.Should().NotContain("new Microsoft.Win32.SaveFileDialog");
    }

    [Fact]
    public void FileNewSaveSaveAsAndClose_RouteThroughDirtyPromptAndOwnedMessages()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        var newMethod = ExtractMethodSource(backstageSource, "private async Task RequestNewWorkbookAsync()");
        newMethod.Should().Contain("CanProceedAfterSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeCreatingWorkbook\"))");
        // File > New advances the session name sequence (Book2, Book3, …) via InitializeNewWorkbook
        // rather than re-creating Book1 through CreateNewWorkbook() (Issue 121). (Also de-brittled for P2b:
        // the dirty-gate now routes through PlanDirtyGate/ResolveDirtyGate — asserted below.)
        newMethod.Should().Contain("InitializeNewWorkbook(_newWorkbookNameSequence.Next());");
        newMethod.Should().Contain("HideStartScreen();");

        var openMethod = ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        openMethod.Should().Contain("CanProceedAfterSaveBeforeDestructiveActionAsync(UiText.Get(\"MainWindowMessage_SaveChangesBeforeOpeningWorkbook\"))");
        openMethod.IndexOf("CanProceedAfterSaveBeforeDestructiveActionAsync", StringComparison.Ordinal)
            .Should()
            .BeLessThan(openMethod.IndexOf("_fileWorkflow.OpenAsync", StringComparison.Ordinal));
        openMethod.IndexOf("CanProceedAfterSaveBeforeDestructiveActionAsync", StringComparison.Ordinal)
            .Should()
            .BeLessThan(openMethod.IndexOf("ReplaceWorkbookSession(new StartupWorkbookLoadResult(", StringComparison.Ordinal));

        // P2b: SaveButton_Click defers the Save-vs-Save-As resolution to the shared SaveResolvedAsync
        // helper (asserted below against MainWindow.WorkbookLifecycle.cs) — the same single resolution path
        // the dirty-gate uses — then hides the backstage on a successful save.
        var saveButtonMethod = ExtractMethodSource(backstageSource, "private async void SaveButton_Click(");
        saveButtonMethod.Should().Contain("var saved = await SaveResolvedAsync();");
        saveButtonMethod.Should().Contain("if (saved && IsStartScreenVisible())");
        saveButtonMethod.Should().Contain("HideStartScreen();");

        var saveAsMethod = ExtractMethodSource(backstageSource, "private async void SaveAsButton_Click(");
        saveAsMethod.Should().Contain("await SaveWorkbookWithDialogAsync()");
        saveAsMethod.Should().Contain("HideStartScreen();");

        var saveTargetMethod = ExtractMethodSource(backstageSource, "private async Task<bool> SaveWorkbookToTargetAsync(");
        saveTargetMethod.Should().Contain("_fileWorkflow.ShouldSkipSaveTargetWrite(_workbookDirty, _currentFilePath, target)");
        saveTargetMethod.Should().NotContain("FileSavePlanner.CanSkipCleanSave(");
        saveTargetMethod.IndexOf("_fileWorkflow.ShouldSkipSaveTargetWrite", StringComparison.Ordinal)
            .Should()
            .BeLessThan(saveTargetMethod.IndexOf("ConfirmUnsupportedXlsxFeatureSave()", StringComparison.Ordinal));
        saveTargetMethod.Should().Contain("ShowSaveProgress(CreateSaveProgress(\"preparing\", TimeSpan.Zero, 1));");
        backstageSource.Should().Contain("WorkbookProgressTextFormatter.FormatSave(phase, elapsed, percent, UiText.Get)");
        saveTargetMethod.Should().Contain("using var operationCancellation = _fileOperationCancellationSession.Begin();");
        // R115-app-host-save-race replaced the direct SetFileOperationInputEnabled(false/true) calls
        // with ref-counted AdjustSaveGate(acquire: true/false) so a "New Window" sibling viewing the
        // same shared Workbook/CommandBus also gets its input surface disabled for the save's
        // duration (see AdjustSaveGate's doc comment) — the pinned literals were never updated.
        saveTargetMethod.Should().Contain("AdjustSaveGate(acquire: true);");
        saveTargetMethod.Should().Contain("operationCancellation.Token");
        saveTargetMethod.Should().Contain("workflowResult.Outcome == WorkbookFileOperationOutcome.Canceled");
        saveTargetMethod.Should().Contain("AdjustSaveGate(acquire: false);");
        saveTargetMethod.Should().Contain("ApplyCompletion: ApplyWpfSaveCompletion");
        backstageSource.Should().Contain("private void ApplyWpfSaveCompletion(SaveCompletionPlan plan)");
        backstageSource.Should().Contain("plan.FileContext is { } fileContext");
        saveTargetMethod.Should().Contain("UiText.Format(\"MainWindowMessage_SaveFileFailed\", ex.Message)");
        saveTargetMethod.Should().Contain("UiText.Get(\"MainWindowMessage_SaveErrorTitle\")");
        saveTargetMethod.Should().Contain("finally");
        saveTargetMethod.Should().Contain("HideSaveProgress();");
        saveTargetMethod.Should().NotContain("RootGrid.IsEnabled = false");
        saveTargetMethod.Should().NotContain("MessageBox.Show(");

        var confirmMethod = ExtractMethodSource(lifecycleSource, "private async Task<SaveChangesConfirmation> ConfirmSaveBeforeDestructiveActionAsync(");
        confirmMethod.Should().Contain("_fileWorkflow.ConfirmBeforeDestructiveActionAsync(");
        confirmMethod.Should().Contain("_workbookDirty");
        confirmMethod.Should().Contain("PromptSaveChangesBeforeDestructiveAction(message)");
        confirmMethod.Should().Contain("SaveResolvedAsync");

        var canProceedMethod = ExtractMethodSource(lifecycleSource, "private Task<bool> CanProceedAfterSaveBeforeDestructiveActionAsync(");
        canProceedMethod.Should().Contain("_fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(");
        canProceedMethod.Should().Contain("_workbookDirty");
        canProceedMethod.Should().Contain("PromptSaveChangesBeforeDestructiveAction(message)");
        canProceedMethod.Should().Contain("SaveResolvedAsync");
        canProceedMethod.Should().Contain("() => _workbookDirty");

        var promptMethod = ExtractMethodSource(lifecycleSource, "private SaveChangesPrompt PromptSaveChangesBeforeDestructiveAction(");
        promptMethod.Should().Contain("ShowOwnedMessage(");
        promptMethod.Should().Contain("MessageBoxButton.YesNoCancel");
        promptMethod.Should().Contain("MessageBoxResult.Cancel => SaveChangesPrompt.Cancel");
        promptMethod.Should().Contain("MessageBoxResult.No => SaveChangesPrompt.DontSave");

        // Save-vs-Save-As resolution: the shared coordinator owns the PlanSave branch and adapter
        // resolution; WPF supplies only the concrete save target and save-as effects.
        var saveResolvedMethod = ExtractMethodSource(lifecycleSource, "private async Task<bool> SaveResolvedAsync()");
        saveResolvedMethod.Should().Contain("_fileWorkflow.SaveResolvedAsync(");
        saveResolvedMethod.Should().Contain("_workbookDirty");
        saveResolvedMethod.Should().Contain("_currentFilePath");
        saveResolvedMethod.Should().Contain("ResolveExistingSaveTarget");
        saveResolvedMethod.Should().Contain("SaveWorkbookToTargetAsync");
        saveResolvedMethod.Should().Contain("SaveWorkbookWithDialogAsync");
        // Round 83 (d8a9dbea7c) extracted the existing-path resolution (and its _fileAdapters use)
        // out of SaveResolvedAsync into its own expression-bodied ResolveExistingSaveTarget helper,
        // passed down as a delegate, so _fileAdapters is no longer referenced directly inside
        // SaveResolvedAsync itself.
        lifecycleSource.Should().Contain("private FileSaveTarget? ResolveExistingSaveTarget() =>");
        lifecycleSource.Should().Contain("_workbookReadOnlySession.ResolveExistingSaveTarget(");
        lifecycleSource.Should().Contain("() => _fileWorkflow.ResolveExistingSaveTarget(_currentFilePath)");

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
        finalCloseMethod.Should().NotContain("_commandBus.Retire(");
        mainSource.Should().Contain("_session.Dispose();");

        var releaseUiMethod = ExtractMethodSource(lifecycleSource, "private void ReleaseWorkbookUiStateForClose()");
        releaseUiMethod.Should().Contain("ClearFormulaReferenceHighlights();");
        releaseUiMethod.Should().Contain("ClearClipboardVisualState();");
        releaseUiMethod.Should().Contain("_workbookClipboardSession.Clear();");
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

        keyboardSource.Should().Contain("RegisterPortableKeyboardCommand(KeyboardCommandShortcut.NewWorkbook, WorkbookShortcutRoute.NewWorkbook);");
        keyboardSource.Should().Contain("RegisterPortableKeyboardCommand(KeyboardCommandShortcut.SaveWorkbook, WorkbookShortcutRoute.SaveWorkbook);");
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
    public void BackstagePaneEntryPoints_SelectPlannedPaneTargets()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            harness.Invoke("ShowInfoView");
            harness.ContentHostShows("SsInfoView").Should().BeTrue("Info is selected by the planned pane target");

            harness.Invoke("ShowPrintView");
            harness.ContentHostShows("SsPrintView").Should().BeTrue("Print is selected by the planned pane target");

            harness.Invoke("ShowHomeView");
            harness.ContentHostShows("SsHomeView").Should().BeTrue("Home is selected by the planned pane target");
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
        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("ImportDataFilePickerPlanner.cs");

        dataCommandsSource.Should().Contain("ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(_fileAdapters)");
        plannerSource.Should().Contain("\".csv\",");
        plannerSource.Should().Contain("\".txt\",");
        plannerSource.Should().Contain("\".tsv\",");
        plannerSource.Should().Contain("\".tab\",");
        plannerSource.Should().Contain("\".xml\"");
    }

    [Fact]
    public void GetData_CsvImportFlowGuardsNativeDialogAndRefreshesImportedCells()
    {
        var dataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        dataCommandsSource.Should().Contain("ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(_fileAdapters)");
        dataCommandsSource.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        dataCommandsSource.Should().Contain("plan.Filter,");
        dataCommandsSource.Should().Contain("checkFileExists: plan.CheckFileExists");
        dataCommandsSource.Should().Contain("multiselect: plan.Multiselect");
        dataCommandsSource.Should().Contain("if (!result.Chosen) return;");
        dataCommandsSource.Should().Contain("FileFormatResolver.FindOpenAdapter(adapters, ext, out var format)");
        dataCommandsSource.Should().Contain("private async void GetDataBtn_Click(object sender, RoutedEventArgs e)");
        dataCommandsSource.Should().Contain("WorkbookImportWorkflow.ImportPathAsync(");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_failed\"");
        dataCommandsSource.Should().Contain("RecordDiagnosticEvent(\"import_completed\"");
        // Round 68 (8007e3ef6c, "R68-async-ordering-race-sweep-2") captures _currentSheetId into a
        // local targetSheetId BEFORE the async import's await, so a concurrent File > Open swapping
        // _currentSheetId out from under this await can't redirect the import to the wrong sheet.
        dataCommandsSource.Should().Contain("var targetSheetId = _currentSheetId;");
        dataCommandsSource.Should().Contain("var targetSession = _session;");
        dataCommandsSource.Should().Contain("targetSession.ExecuteCommandPreservingSelection(command)");
        dataCommandsSource.Should().Contain("previousExtent: previousExtent");
        dataCommandsSource.Should().Contain("_lastImportExtent = (");
        dataCommandsSource.Should().NotContain("_commandBus.Execute(");
        dataCommandsSource.Should().NotContain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        dataCommandsSource.Should().Contain("SetActiveCell(destination);");
        dataCommandsSource.Should().Contain("EnsureCellVisible(destination);");
        dataCommandsSource.Should().Contain("UpdateViewport();");
        dataCommandsSource.Should().Contain("RefreshStatusBar();");
        dataCommandsSource.Should().Contain("UiText.Get(\"MainWindowMessage_NoImportAdapters\")");
        dataCommandsSource.Should().Contain("WorkbookImportFailurePlanner.FromException(ext, ex)");
        dataCommandsSource.Should().Contain("ShowOwnedMessage(diagnostic.UserMessage");
        dataCommandsSource.Should().Contain("errorDetail: diagnostic.Detail");
        dataCommandsSource.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
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

        shareMethod.Should().Contain("WorkbookShareReadinessPlanner.CreatePlan(");
        shareMethod.Should().Contain("WorkbookShareSurface.WindowsShare");
        shareMethod.Should().Contain("WorkbookShareReadinessPlanKind.SaveAsBeforeShare");
        shareMethod.Should().NotContain("ShareWorkbookPlanner.CreatePlan(_currentFilePath)");
        shareMethod.Should().Contain("SaveWorkbookWithDialogAsync()");
        shareMethod.Should().Contain("FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target)");
        shareMethod.Should().Contain("SaveWorkbookToTargetAsync(target!)");
        shareMethod.Should().Contain("_shareService.ShareFileAsync(this, sharePath, _workbook.Name)");

        reviewSource.Should().Contain("private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();");

        // The backstage Share rail entry now lives on the shared frame and routes through the shared
        // workflow executor to the FreeX frame wrapper's ShareWorkbookAsync handler.
        var frameSource = DialogSourceTestSupport.ReadHostSources("MainWindow.BackstageFrame.cs");
        frameSource.Should().Contain("ShareWorkbookAsync: ShareWorkbookAsync");
    }

    [Fact]
    public void BackstageOpenProgressAndUnsupportedWarnings_UseOwnedDialogsAndRecoverOverlay()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var openMethod = ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        var saveWarningMethod = ExtractMethodSource(backstageSource, "private bool ConfirmUnsupportedXlsxFeatureSave()");
        var openWarningMethod = ExtractMethodSource(backstageSource, "private void ShowUnsupportedXlsxFeatureOpenWarningIfNeeded()");

        openMethod.Should().Contain("ShowOpenProgress(CreateOpenProgress(\"preparing\", TimeSpan.Zero, 1));");
        openMethod.Should().Contain("using var operationCancellation = _fileOperationCancellationSession.Begin();");
        openMethod.Should().Contain("_fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(");
        openMethod.Should().Contain("target,");
        openMethod.Should().Contain("ApplyOpenedWorkbookAsync,");
        openMethod.Should().Contain("Progress: progress,");
        openMethod.Should().Contain("WorkbookProgressTextFormatter.FormatOpen(update, UiText.Get)");
        openMethod.Should().Contain("ShowOpenProgress(CreateOpenProgress(\"preparing view\", TimeSpan.Zero, null));");
        openMethod.Should().Contain("catch (OperationCanceledException) when (operationCancellation.Token.IsCancellationRequested)");
        openMethod.Should().Contain("ShowOpenProgress(CreateOpenProgress(\"done\", TimeSpan.Zero, 100));");
        openMethod.Should().Contain("workflowResult.Outcome == WorkbookFileOperationOutcome.Canceled");
        openMethod.Should().Contain("if (!workflowResult.Succeeded)");
        openMethod.Should().Contain("ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();");
        openMethod.Should().Contain("UiText.Format(\"MainWindowMessage_OpenFileFailed\", ex.Message)");
        openMethod.Should().Contain("UiText.Get(\"MainWindowMessage_OpenErrorTitle\")");
        openMethod.Should().Contain("finally");
        openMethod.Should().Contain("HideOpenProgress();");
        openMethod.Should().Contain("_isOpeningFile = false;");
        openMethod.Should().NotContain("MessageBox.Show(");

        saveWarningMethod.Should().Contain("DeferredCommandMessagePlanner.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport)");
        saveWarningMethod.Should().Contain("WpfResourceKeyTextResolver.Resolve(");
        saveWarningMethod.Should().Contain("ShowOwnedMessage(");
        saveWarningMethod.Should().NotContain("MessageBox.Show(");

        openWarningMethod.Should().Contain("DeferredCommandMessagePlanner.UnsupportedXlsxFeatureOpenWarning(_currentXlsxFeatureReport)");
        openWarningMethod.Should().Contain("WpfResourceKeyTextResolver.Resolve(");
        openWarningMethod.Should().Contain("ShowOwnedMessage(");
        openWarningMethod.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void FooterOperationProgress_HidesReadyAndRoutesCancel()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var showProgressMethod = ExtractMethodSource(backstageSource, "private void ShowOperationFooterProgress(");
        var hideProgressMethod = ExtractMethodSource(backstageSource, "private void HideOperationFooterProgress()");
        var cancelMethod = ExtractMethodSource(backstageSource, "private void CancelFileOperation_Click(");
        var inputLockMethod = ExtractMethodSource(backstageSource, "private void SetFileOperationInputEnabled(");

        showProgressMethod.Should().Contain("StatusSaveProgressCancelButton.Visibility = Visibility.Visible;");
        showProgressMethod.Should().Contain("StatusSaveProgressCancelButton.IsEnabled = _fileOperationCancellationSession.CanCancel;");
        showProgressMethod.Should().Contain("StatusReadyText.Visibility = Visibility.Collapsed;");
        showProgressMethod.Should().Contain("StatusStatsPanel.Visibility = Visibility.Collapsed;");
        hideProgressMethod.Should().Contain("StatusSaveProgressCancelButton.Visibility = Visibility.Collapsed;");
        hideProgressMethod.Should().Contain("RefreshStatusBar();");
        cancelMethod.Should().Contain("_fileOperationCancellationSession.CancelCurrent();");
        cancelMethod.Should().Contain("StatusSaveProgressCancelButton.IsEnabled = _fileOperationCancellationSession.CanCancel;");
        inputLockMethod.Should().Contain("ReferenceEquals(child, StatusBarRoot)");
        inputLockMethod.Should().Contain("StatusInteractiveControls.IsEnabled = false;");
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
        method.Should().Contain("DeferredCommandMessagePlanner.OnlineTemplatesExcluded()");
        method.Should().Contain("WpfResourceKeyTextResolver.Resolve(");
        method.Should().Contain("ShowOwnedMessage(");
        method.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void ExportWorkflow_SurfacesPlannedPdfAndXpsPaths()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

        source.Should().Contain("ExportAsPdf(");
        source.Should().Contain("effectiveRequest.Path,");
        source.Should().Contain("WpfExportDescriptionPlanner.DescribeRequest(effectiveRequest)");
        source.Should().Contain("ExportAsXps(");
        source.Should().Contain("var document = RenderExportDocument(effectiveOptions)");
        source.Should().Contain("var paginator = RenderExportPaginator(effectiveOptions)");
        source.Should().Contain("WpfExportDescriptionPlanner.DescribeRequest(effectiveRequest)");
        source.Should().Contain("OpenExportedFile(resultPlan.DestinationPath)");
        source.Should().NotContain("ExportPdfFallbackAsXps");
    }

    [Fact]
    public void ExportPdfXpsSaveDialog_DeclaresNativeGuardrailsAndOwnedMessages()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");
        var exportMethod = ExtractMethodSource(source, "private async void ExportPdfButton_Click(");
        var exportPdfMethod = ExtractMethodSource(source, "private async Task<bool> ExportAsPdf(");
        var exportXpsMethod = ExtractMethodSource(source, "private async Task<bool> ExportAsXps(");

        exportMethod.Should().Contain("var savePlan = ExportFilePickerPlanner.BuildPdfXpsDialogPlan(_workbook.Name, \"FreeX\");");
        exportMethod.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        exportMethod.Should().Contain("UiText.Get(\"MainWindowDialog_ExportPdfXpsTitle\")");
        exportMethod.Should().Contain("UiText.Get(\"MainWindowDialog_ExportPdfXpsFilter\")");
        exportMethod.Should().Contain("savePlan.DefaultExtensionWithDot");
        exportMethod.Should().Contain("savePlan.SuggestedFileName");
        exportMethod.Should().Contain("savePlan.DefaultFilterIndex");
        exportMethod.Should().Contain("if (!saveResult.Chosen) return;");
        exportMethod.Should().Contain("ExportFormatCatalog");
        exportMethod.Should().Contain("ExportFilePickerPlanner.FormatFromPdfXpsFilterIndex(saveResult.FilterIndex)");
        exportMethod.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        exportMethod.Should().Contain("WorkbookExportInteractionPlanner.CreateResultPlan(");
        exportMethod.Should().Contain("WorkbookExportWorkflow.ExecuteBooleanAsync(");
        exportMethod.Should().Contain("resultPlan.ShouldPresentIssue");
        exportMethod.Should().Contain("resultPlan.Message");
        exportMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportOptionsTitle\")");
        exportMethod.Should().Contain("ShowOwnedMessage(");
        exportMethod.Should().Contain("OpenExportedFile(resultPlan.DestinationPath)");
        exportMethod.Should().NotContain("MessageBox.Show(");
        exportMethod.Should().NotContain("new Microsoft.Win32.SaveFileDialog");

        exportPdfMethod.Should().Contain("PdfDocumentExporter.CreateProperties(_workbook, effectiveOptions)");
        exportPdfMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportPdfSavedFormat\", optionSummary, pdfPath)");
        exportPdfMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportPdfTitle\")");
        exportPdfMethod.Should().Contain("UiText.Format(\"MainWindowMessage_ExportPdfFailed\", ex.Message)");
        exportPdfMethod.Should().Contain("UiText.Get(\"MainWindowMessage_ExportErrorTitle\")");
        exportPdfMethod.Should().Contain("ShowOwnedMessage(");
        exportPdfMethod.Should().NotContain("MessageBox.Show(");

        exportXpsMethod.Should().Contain("XpsPackagePropertiesAdapter.Apply(");
        exportXpsMethod.Should().Contain("ExportDocumentPropertiesPlanner.FromWorkbook(_workbook, effectiveOptions)");
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
        var routingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        backstageSource.Should().Contain("private void OpenPrintBackstage()");
        backstageSource.Should().Contain("RefreshBackstagePrintPreview();");
        backstageSource.Should().Contain("ShowPrintView();");
        backstageSource.Should().NotContain("PrintButton_Click(SsPrintNavBtn, new RoutedEventArgs())");
        keyboardSource.Should().Contain("KeyboardCommandShortcut.OpenPrintPreview, WorkbookShortcutRoute.PrintWorkbook");
        routingSource.Should().Contain("PrintWorkbookAsync:");
        routingSource.Should().Contain("OpenPrintBackstageAsync:");
        routingSource.Should().Contain("RunApplicationFrameCommand(OpenPrintBackstage)");
        keyboardSource.Should().NotContain("KeyboardCommandShortcut.OpenPrintPreview, PrintButton_Click");
        // Print-pane content (kept verbatim through the migration):
        xaml.Should().Contain("x:Name=\"SsBackstagePrintNowButton\"");
        xaml.Should().Contain("x:Name=\"SsPrintOptionsHost\"");
        xaml.Should().Contain("x:Name=\"SsPrintPreviewViewer\"");
        // The backstage Print button now carries the access-key label "_Print..." for its visible content,
        // while its stable automation name stays "Print".
        xaml.ShouldContainLocalizedAttribute("Content", "_Print...");
        xaml.ShouldContainLocalizedAttribute("AutomationProperties.Name", "Print");
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
        printSource.Should().Contain("var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService, workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());");
        printSource.Should().Contain("PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance)");
        printSource.Should().Contain("new PrintPreviewDialog(");
        printSource.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        previewSource.Should().Contain("Content = UiText.Get(\"PrintPreview_PrintButton\")");
        previewSource.Should().Contain("ShowNativePrintDialog");
        previewSource.Should().Contain("Forms.PrintDialog");
        previewSource.Should().Contain("PrintDocument(paginator");
    }
}
