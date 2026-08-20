using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Free.Shared.Shell.Wpf;
using FreeX.App.Localization;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Calculation;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private WorkbookFileWorkflow CreateWorkbookFileWorkflow() =>
        new(
            _fileAdapters,
            new WorkbookOpenService(openedWorkbook => _recalcEngine.RecalculateAllFormulas(openedWorkbook)),
            request => RecentFileRegistrationService.RegisterIfNeeded(ReloadRecentFilesStore, request));

    private static readonly FreeXBackstageHomePanePlan BackstageHomePanePlan = FreeXBackstageHomePanePlanner.Build();
    private PrintPreviewSettings _backstagePrintPreviewSettings = new();
    private FixedDocument? _backstagePrintPreviewDocument;

    private void ShowStartScreen()
    {
        StartScreenOverlay.Visibility = Visibility.Visible;
        // The shared frame builds the Home pane (greeting + recent list refresh runs in its ContentFactory)
        // and lands focus on the Home rail entry. The overlay/frame become visible on the next layout pass,
        // so post the focus at Loaded priority (the rail buttons aren't focusable until they are visible) —
        // mirroring how the Print pane focuses Print Now.
        _backstageFrame?.Show(BackstageFramePlan.Selection.DefaultPaneAutomationId);
        FocusDefaultBackstagePaneNavigation();
        Dispatcher.BeginInvoke(
            new Action(FocusDefaultBackstagePaneNavigation),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HideStartScreen()
    {
        // Hide() funnels through the frame's Closed event, which collapses the overlay and restores
        // SheetGrid focus. Guard for the (test) case where the frame was never built.
        if (_backstageFrame is not null)
        {
            _backstageFrame.Hide();
            return;
        }

        StartScreenOverlay.Visibility = Visibility.Collapsed;
        SheetGrid.Focus();
    }

    private void FocusDefaultBackstagePaneNavigation() =>
        _backstageFrame?.FocusEntry(BackstageFramePlan.Selection.DefaultPaneAutomationId);

    private void ConfigureBackstageHomePaneDescriptors()
    {
        var plan = BackstageHomePanePlan;
        ConfigureBackstageRecentTab(plan.RecentTab, SsRecentTabButton, SsRecentTabText);
        ConfigureBackstageRecentTab(plan.PinnedTab, SsPinnedTabButton, SsPinnedTabText);

        System.Windows.Automation.AutomationProperties.SetName(
            SsSearchBox,
            UiText.Get(plan.Search.AutomationNameKey));
        System.Windows.Automation.AutomationProperties.SetHelpText(
            SsSearchBox,
            UiText.Get(plan.Search.AutomationHelpTextKey));

        foreach (var column in plan.Columns)
        {
            ResolveBackstageRecentColumnHeader(column.Id).Text = UiText.Get(column.LabelKey);
        }
    }

    private static void ConfigureBackstageRecentTab(
        FreeXBackstageRecentTabDescriptor descriptor,
        Button button,
        TextBlock label)
    {
        label.Text = UiText.Get(descriptor.LabelKey);
        RibbonTooltip.SetTitle(button, UiText.Get(descriptor.TooltipTitleKey));
        RibbonTooltip.SetKeyTip(button, descriptor.KeyTip);
        RibbonMetadata.SetCommandName(button, descriptor.CommandName);
    }

    private TextBlock ResolveBackstageRecentColumnHeader(FreeXBackstageRecentColumnId id) =>
        id switch
        {
            FreeXBackstageRecentColumnId.Name => SsRecentNameColumnHeader,
            FreeXBackstageRecentColumnId.DateModified => SsRecentDateModifiedColumnHeader,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static void ApplyBackstageRecentFileRowDescriptor(
        FrameworkElement element,
        FreeXBackstageRecentFileRowKind kind)
    {
        var descriptor = BackstageHomePanePlan.Rows.Single(row => row.Kind == kind);
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, descriptor.AutomationId);
    }

    private void SsRecentPinCommandButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
            ConfigureBackstageRecentFileCommandButton(button, FreeXBackstageRecentFileCommandId.Pin);
    }

    private void SsPinnedUnpinCommandButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
            ConfigureBackstageRecentFileCommandButton(button, FreeXBackstageRecentFileCommandId.Unpin);
    }

    private static void ConfigureBackstageRecentFileCommandButton(
        Button button,
        FreeXBackstageRecentFileCommandId id)
    {
        var command = BackstageHomePanePlan.RowCommands.Single(command => command.Id == id);
        RibbonTooltip.SetTitle(button, UiText.Get(command.TooltipTitleKey));
        RibbonTooltip.SetDescription(button, UiText.Get(command.TooltipDescriptionKey));
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, command.AutomationId);
        button.ToolTip = UiText.Get(command.ToolTipKey);
        RibbonMetadata.SetCommandName(button, command.CommandName);
    }

    private void ConfigureBackstageInfoActionButtons()
    {
        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.WpfInfoPane,
            FreeXBackstageInfoPaneRequest.Empty);

        foreach (var action in pane.Actions)
        {
            if (action.Id == FreeXBackstageInfoActionId.ProtectWorkbook)
            {
                InfoProtectWorkbookButton.Click += InfoProtectWorkbookBtn_Click;
                System.Windows.Automation.AutomationProperties.SetAutomationId(
                    InfoProtectWorkbookButton,
                    action.AutomationId);
                RibbonTooltip.SetKeyTip(InfoProtectWorkbookButton, action.KeyTip ?? string.Empty);
                RefreshBackstageInfoProtectionButton();
                continue;
            }

            ConfigureBackstageInfoActionButton(
                ResolveBackstageInfoActionButton(action.Id),
                UiText.Get(action.LabelKey),
                UiText.Get(action.AutomationHelpTextKey ?? action.TooltipDescriptionKey ?? action.LabelKey),
                UiText.Get(action.TooltipTitleKey ?? action.LabelKey),
                UiText.Get(action.TooltipDescriptionKey ?? action.AutomationHelpTextKey ?? action.LabelKey),
                action.AutomationId,
                action.KeyTip ?? string.Empty,
                ResolveBackstageInfoActionHandler(action.Id));
        }
    }

    private static void ConfigureBackstageInfoActionButton(
        Button button,
        string automationName,
        string automationHelpText,
        string tooltipTitle,
        string tooltipDescription,
        string automationId,
        string keyTip,
        RoutedEventHandler clickHandler)
    {
        button.Click += clickHandler;
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, automationHelpText);
        RibbonTooltip.SetTitle(button, tooltipTitle);
        RibbonTooltip.SetDescription(button, tooltipDescription);
        RibbonTooltip.SetKeyTip(button, keyTip);
    }

    private Button ResolveBackstageInfoActionButton(FreeXBackstageInfoActionId id) =>
        id switch
        {
            FreeXBackstageInfoActionId.CheckAccessibility => InfoCheckAccessibilityButton,
            FreeXBackstageInfoActionId.WorkbookStatistics => InfoWorkbookStatisticsButton,
            FreeXBackstageInfoActionId.ErrorChecking => InfoErrorCheckingButton,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private RoutedEventHandler ResolveBackstageInfoActionHandler(FreeXBackstageInfoActionId id) =>
        id switch
        {
            FreeXBackstageInfoActionId.CheckAccessibility => InfoAccessibilityCheckerBtn_Click,
            FreeXBackstageInfoActionId.WorkbookStatistics => InfoWorkbookStatisticsBtn_Click,
            FreeXBackstageInfoActionId.ErrorChecking => InfoErrorCheckingBtn_Click,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private bool TryHandleBackstageShellFocusCycle(bool reverse)
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
            !IsInsideStartScreenOverlay(focusedElement))
        {
            FocusDefaultBackstagePaneNavigation();
            return true;
        }

        var direction = reverse
            ? FocusNavigationDirection.Previous
            : FocusNavigationDirection.Next;

        if (StartScreenOverlay.MoveFocus(new TraversalRequest(direction)))
            return true;

        FocusDefaultBackstagePaneNavigation();
        return true;
    }

    // The Up/Down/Home/End rail navigation that used to live here is now owned by the shared
    // BackstageFrame (see Free.Shared.Shell.Wpf.BackstageFrame.OnKeyDown), so the overlay no longer
    // hooks PreviewKeyDown for it.

    private bool TryOpenFocusedBackstageContextMenu()
    {
        if (!IsStartScreenVisible() ||
            Keyboard.FocusedElement is not FrameworkElement focusedElement ||
            !IsInsideStartScreenOverlay(focusedElement) ||
            focusedElement.ContextMenu is not { } menu)
        {
            return false;
        }

        menu.PlacementTarget = focusedElement;
        menu.Opened -= BackstageContextMenu_Opened;
        menu.Opened += BackstageContextMenu_Opened;
        menu.IsOpen = true;
        return true;
    }

    private static void BackstageContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        MenuItem? firstEnabledItem = null;
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem || !menuItem.IsEnabled)
                continue;

            firstEnabledItem = menuItem;
            break;
        }

        if (firstEnabledItem is null)
            return;

        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    private void OpenPrintBackstage()
    {
        ShowStartScreen();
        ShowPrintView();
    }

    // The three Show*View methods now drive the shared frame: selecting a pane entry highlights the rail
    // button and runs that pane's ContentFactory (which does the live refresh + reparents the pane element).
    // They are addressed by language-invariant automation id so they work in any UI language.
    private void ShowHomeView() => ShowBackstagePane(FreeXBackstagePaneId.Home);

    private void ShowInfoView() => ShowBackstagePane(FreeXBackstagePaneId.Info);

    private void ShowPrintView() => ShowBackstagePane(FreeXBackstagePaneId.Print);

    private void ShowBackstagePane(FreeXBackstagePaneId pane) =>
        _backstageFrame?.Show(BackstageFramePlan.Selection.For(pane));

    private void ConfigureBackstagePrintOptions(Sheet? activeSheet)
    {
        SsPrintOptionsHost.Content = PrintPreviewSettingsPanelFactory.Build(
            _currentSheetId,
            activeSheet,
            cmd => TryExecuteCommand(cmd, "Print Settings"),
            RefreshBackstagePrintPreview,
            settings => _backstagePrintPreviewSettings = settings,
            hasSelection: SheetGrid.SelectedRange is not null,
            showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
            showCustomMargins: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()));
    }

    private void RefreshBackstagePrintPreview()
    {
        var refreshed = BuildActiveSheetPrintPreview(_backstagePrintPreviewSettings);
        _backstagePrintPreviewDocument = refreshed.Document;
        SsPrintPreviewViewer.Document = refreshed.Document;
        SsPrintSettingsSummary.Text = refreshed.Settings.Summary;
        Dispatcher.BeginInvoke(
            () => SsPrintPreviewViewer.FitToWidth(),
            DispatcherPriority.Loaded);
    }

    private void BackstagePrintNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backstagePrintPreviewDocument is null)
            RefreshBackstagePrintPreview();

        if (_backstagePrintPreviewDocument is null)
            return;

        var settings = _backstagePrintPreviewSettings;

        if (!TryResolveBackstagePrintPaginator(_backstagePrintPreviewDocument, settings, out var paginator))
            return;

        // A missing saved queue falls through to null so the native dialog uses Windows' default.
        var printQueue = Free.Shared.Shell.Wpf.WpfPrintQueueCatalog.Resolve(settings.PrinterName);

        NativePrintDialogService.ShowPrintDialogAndPrint(
            paginator,
            printQueue,
            PrintSettingsPlanner.ClampCopies(settings.Copies),
            settings.Collated,
            settings.Sides,
            this);
    }

    /// <summary>
    /// Resolves the paginator to send to the printer for the Backstage Print pane's "Print Now"
    /// button, applying the requested Pages From/To range when one was typed. An out-of-bounds or
    /// reversed range (From&gt;To, either bound outside 1..totalPages) must not silently fall through
    /// to printing the full, unranged document with zero feedback -- warn and abort instead, mirroring
    /// the separate Print Preview dialog's ShowInvalidPageRangeWarning behavior for the same input.
    /// </summary>
    private bool TryResolveBackstagePrintPaginator(
        FixedDocument document,
        PrintPreviewSettings settings,
        out DocumentPaginator paginator)
    {
        paginator = document.DocumentPaginator;
        if (!settings.PageFrom.HasValue && !settings.PageTo.HasValue)
            return true;

        var totalPages = paginator.PageCount;
        if (!PrintSettingsPlanner.TryValidatePageRange(
                settings.PageFrom, settings.PageTo, totalPages,
                out var from, out var to))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("PrintPreview_InvalidPageRangeMessage"), Title);
            return false;
        }

        paginator = WpfPageRangeDocumentPaginator.CreateValidatedInclusive(document.DocumentPaginator, from, to);
        return true;
    }

    private void UpdateInfoView()
    {
        // Under Manual calculation, a freshly-typed circular formula is never recalculated until
        // F9/save/an automatic-mode edit, so the session's cyclic-cell state would otherwise still be
        // empty here and File > Info would report zero circular references while Formulas >
        // Error Checking (which recalculates first — see ErrorCheckBtn_Click) reports the real
        // count for the identical workbook state. Recalculate here too so both surfaces agree.
        RecalculateWorkbook();

        var activeSheet = _workbook.GetSheet(_currentSheetId);
        var info = BackstageInfoPlanner.Build(
            _workbook,
            _currentFilePath,
            WpfResourceKeyTextResolver.Instance,
            activeSheet,
            hasSelection: SheetGrid.SelectedRange is not null,
            cyclicCells: _session.CyclicCells);
        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.WpfInfoPane,
            BackstageInfoPlanner.CreatePaneRequest(info));

        foreach (var detail in pane.Details)
        {
            ResolveBackstageInfoDetailTextBlock(detail.Id).Text = ResolveBackstageTextValue(detail.Value);
        }

        RefreshBackstageInfoProtectionButton();
    }

    private TextBlock ResolveBackstageInfoDetailTextBlock(FreeXBackstageInfoDetailId id) =>
        id switch
        {
            FreeXBackstageInfoDetailId.WorkbookName => InfoWorkbookName,
            FreeXBackstageInfoDetailId.FilePath => InfoFilePath,
            FreeXBackstageInfoDetailId.SheetCount => InfoSheetCount,
            FreeXBackstageInfoDetailId.Format => InfoFormat,
            FreeXBackstageInfoDetailId.FileSize => InfoFileSize,
            FreeXBackstageInfoDetailId.LastModified => InfoLastModified,
            FreeXBackstageInfoDetailId.Share => InfoShareStatus,
            FreeXBackstageInfoDetailId.Export => InfoExportStatus,
            FreeXBackstageInfoDetailId.WorkbookProtection => InfoWorkbookProtectionSummary,
            FreeXBackstageInfoDetailId.ActiveSheetProtection => InfoActiveSheetProtectionSummary,
            FreeXBackstageInfoDetailId.WorkbookStatistics => InfoStatisticsSummary,
            FreeXBackstageInfoDetailId.Accessibility => InfoAccessibilitySummary,
            FreeXBackstageInfoDetailId.FormulaErrors => InfoFormulaErrorSummary,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static string ResolveBackstageTextValue(FreeXBackstageTextValue value) =>
        value.Resolve(UiText.Get);

    private void RefreshBackstageInfoProtectionButton()
    {
        if (InfoProtectWorkbookButton is null)
            return;

        var plan = ProtectionWorkflowSession.CreateWorkbookChromePlan(_workbook);
        var buttonContent = UiText.Get(plan.ButtonContentResourceKey);
        var tooltipTitle = UiText.Get(plan.TooltipTitleResourceKey);
        var tooltipDescription = UiText.Get(plan.TooltipDescriptionResourceKey);
        InfoProtectWorkbookButton.Content = buttonContent;
        System.Windows.Automation.AutomationProperties.SetName(InfoProtectWorkbookButton, buttonContent);
        System.Windows.Automation.AutomationProperties.SetHelpText(InfoProtectWorkbookButton, tooltipDescription);
        RibbonTooltip.SetTitle(InfoProtectWorkbookButton, tooltipTitle);
        RibbonTooltip.SetDescription(InfoProtectWorkbookButton, tooltipDescription);
    }

    private void UpdateSsGreeting()
    {
        SsGreeting.Text = BackstageGreetingFormatter.FormatGreeting(DateTime.Now);
    }

    private bool _showingPinnedList;

    private void UpdateSsRecentList(string filter = "")
    {
        // Reload from disk rather than reading the constructor-time _recentFiles snapshot: with
        // multiple windows sharing this process (View > New Window), a sibling window may have
        // registered/pinned/removed an entry since this window loaded, and this window's cached
        // instance would never observe it otherwise.
        // Route existence checks through _recentFilePathExistenceCache rather than a raw
        // System.IO.File.Exists: this method re-runs on every Recent-files search-box keystroke
        // (SsSearchBox_TextChanged below), and a raw File.Exists against an unreachable UNC/network
        // recent entry blocks for the SMB/TCP connect timeout (20+ seconds) on the UI thread, per
        // character typed. The cache probes off-thread and reuses the cached result instead.
        var plan = BackstageRecentFileListPlanner.Build(
            ReloadRecentFilesStore().Snapshot(),
            filter,
            _recentFilePathExistenceCache.Exists);
        _allRecentItems = plan.AllItems.ToList();
        SsRecentList.ItemsSource = plan.RecentItems;
        SsPinnedList.ItemsSource = plan.PinnedItems;
    }

    /// <summary>
    /// Reloads the recent-files store fresh from disk. Every window in the process constructs its
    /// own <see cref="RecentFilesStore"/> instance at startup (see MainWindow.xaml.cs), so with
    /// multiple windows open (Excel-style "New Window") each window's cached instance goes stale
    /// the moment a sibling window writes to recent.json. Reloading immediately before every read
    /// or mutation — rather than trusting the long-lived <c>_recentFiles</c> field — avoids both
    /// showing a stale list and clobbering a sibling's write with a stale one (lost update).
    /// </summary>
    private RecentFilesStore ReloadRecentFilesStore() => RecentFilesStore.Load();

    private void SsRecentTab_Click(object sender, RoutedEventArgs e)
    {
        ApplyBackstageTabSelection(BackstageTabSelectionPlanner.Select(
            _showingPinnedList,
            BackstageRecentTab.Recent));
    }

    private void SsPinnedTab_Click(object sender, RoutedEventArgs e)
    {
        ApplyBackstageTabSelection(BackstageTabSelectionPlanner.Select(
            _showingPinnedList,
            BackstageRecentTab.Pinned));
    }

    private void SwitchToRecentTab()
    {
        ApplyBackstageTabSelection(BackstageTabSelectionPlanner.Select(
            _showingPinnedList,
            BackstageRecentTab.Recent),
            force: true);
    }

    private void SwitchToPinnedTab()
    {
        ApplyBackstageTabSelection(BackstageTabSelectionPlanner.Select(
            _showingPinnedList,
            BackstageRecentTab.Pinned),
            force: true);
    }

    private void ApplyBackstageTabSelection(BackstageTabSelectionPlan plan, bool force = false)
    {
        if (!plan.Changed && !force)
            return;

        _showingPinnedList = plan.ActiveTab == BackstageRecentTab.Pinned;
        SsRecentScroll.Visibility = plan.RecentListVisible ? Visibility.Visible : Visibility.Collapsed;
        SsPinnedScroll.Visibility = plan.PinnedListVisible ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = TryFindResource("FreeXAccentBrush") as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x6D, 0x8C));
        var inactiveBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));

        SsRecentTab.BorderBrush = plan.ActiveTab == BackstageRecentTab.Recent
            ? activeBrush
            : System.Windows.Media.Brushes.Transparent;
        SsRecentTabText.FontWeight = plan.ActiveTab == BackstageRecentTab.Recent
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        SsRecentTabText.Foreground = plan.ActiveTab == BackstageRecentTab.Recent
            ? activeBrush
            : inactiveBrush;

        SsPinnedTab.BorderBrush = plan.ActiveTab == BackstageRecentTab.Pinned
            ? activeBrush
            : System.Windows.Media.Brushes.Transparent;
        SsPinnedTabText.FontWeight = plan.ActiveTab == BackstageRecentTab.Pinned
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        SsPinnedTabText.Foreground = plan.ActiveTab == BackstageRecentTab.Pinned
            ? activeBrush
            : inactiveBrush;
    }

    // Startup and test paths create the initial Book1; the user-facing File > New path passes the
    // next session name (Book2, Book3, …) via RequestNewWorkbookAsync (Issue 121). Kept as a single
    // parameterless method so reflection-based test harnesses resolve it unambiguously.
    private void CreateNewWorkbook() => InitializeNewWorkbook(workbookName: null);

    private void InitializeNewWorkbook(string? workbookName)
    {
        CloseFindReplaceDialogIfOpen();
        AdoptWorkbookAsInitial(WorkbookFactory.CreateFromAppOptions(_options, workbookName));
    }

    private void AdoptWorkbookAsInitial(Workbook wb)
    {
        // When "New Window" siblings still view the current document, leave their context
        // (workbook ref / command bus / dirty state) untouched and continue on a fresh one:
        // File > New replaces the document in THIS window only (H39).
        if (DocumentSharedWithOtherWindows())
        {
            DetachFromSharedDocumentContext();
        }
        ReplaceWorkbookSession(new StartupWorkbookLoadResult(
            wb,
            wb.Name,
            "Created new workbook.",
            IsFallback: false));
        InvalidateToolbarVisualState();
        _worksheetSelections.Clear();
        _worksheetViewStates.Clear();
        InvalidateNavigationCaches();
        _currentFileSourceLastWriteTimeUtc = null;
        _currentXlsxFeatureReport = null;
        _workbookReadOnlySession.Reset();
        UpdateTitleBar();
        RecalculateWorkbook();
        SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        RefreshSheetTabs();
        UpdateViewport();
        MarkWorkbookSaved();
        // Document-scoped broadcast: after a detach there are no same-document siblings, so
        // this is a no-op for windows over other documents (they keep their own workbooks).
        NotifyOtherWindowsOfWorkbookChange();
        // This window may have just left a "New Window" group — renumber so a now-lone
        // sibling drops its " - 1" suffix and this window starts unnumbered.
        _windowRegistry?.RefreshWindowNumbering();
        RecordDiagnosticEvent("workbook_new");
    }

    private async Task RequestNewWorkbookAsync()
    {
        // Skip the save prompt when a "New Window" sibling still views this document — the
        // document (and its dirty state) stays alive there; only this view is being replaced.
        if (!DocumentSharedWithOtherWindows() &&
            !await CanProceedAfterSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeCreatingWorkbook")))
            return;

        // Advance the session name sequence so File > New produces Book2, Book3, … rather than
        // repeatedly creating another Book1 (Issue 121).
        InitializeNewWorkbook(_newWorkbookNameSequence.Next());
        HideStartScreen();
    }

    internal Task OpenStartupFileAsync(string path) => OpenFileAsync(path);

    /// <summary>
    /// Tells the user that a command-line file argument could not be opened (it didn't exist, was
    /// a directory, a URL, or an otherwise-invalid path) when no startup argument resolved to an
    /// openable file. Called from App.xaml.cs's App_OnStartup after Show() so the window is
    /// available as the dialog's owner. Without this the app fell back to a blank workbook with
    /// zero indication the requested file wasn't opened (R118).
    /// </summary>
    internal void ReportStartupFileNotFound(string path) =>
        ShowOwnedMessage(
            UiText.Format("Startup_FileArgumentNotFoundMessage", path),
            UiText.Get("MainWindowMessage_OpenErrorTitle"),
            MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>
    /// Opens a recovery snapshot into this window without recording the snapshot path in recent
    /// files. Called from App.xaml.cs during startup recovery; the snapshot path is a temporary
    /// .fxl file that must never appear in the MRU list.
    /// </summary>
    internal Task OpenRecoverySnapshotAsync(string snapshotPath) =>
        OpenFileAsync(snapshotPath, suppressRecentFiles: true);

    private async Task OpenFileAsync(string path, bool suppressRecentFiles = false)
    {
        if (!_fileWorkflow.TryResolveOpenTarget(path, out var target, out var openTargetMessage))
        {
            // Surface the planner's discarded failure reason (e.g. "Unsupported file type: .txt." or
            // the renamed-file content/extension mismatch message) instead of silently no-opping, the
            // same way a load-time exception below is reported via ShowOwnedMessage (R118). This is
            // the single choke point for every open path: File > Open, drag-drop, MRU clicks, and
            // command-line startup args (via OpenStartupFileAsync) all funnel through here.
            if (!string.IsNullOrEmpty(openTargetMessage))
            {
                ShowOwnedMessage(
                    UiText.Format("MainWindowMessage_OpenFileFailed", openTargetMessage),
                    UiText.Get("MainWindowMessage_OpenErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }
        var ext = FileFormatResolver.NormalizeExtension(target!.Extension);
        if (_isOpeningFile) return;
        _isOpeningFile = true;
        using var operationCancellation = _fileOperationCancellationSession.Begin();
        try
        {
            // Skip the save prompt when a "New Window" sibling still views this document — the
            // document (and its dirty state) stays alive there; only this view is being replaced.
            if (!DocumentSharedWithOtherWindows() &&
                !await CanProceedAfterSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeOpeningWorkbook")))
                return;

            _operationProgressFileName = System.IO.Path.GetFileName(target.Path);
            ShowOpenProgress(CreateOpenProgress("preparing", TimeSpan.Zero, 1));

            var progress = new Progress<WorkbookOpenProgressUpdate>(update =>
                ShowOpenProgress(WorkbookProgressTextFormatter.FormatOpen(update, UiText.Get)));
            var workflowResult = await _fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(
                target,
                ApplyOpenedWorkbookAsync,
                suppressRecentFiles,
                Progress: progress,
                CancellationToken: operationCancellation.Token));

            if (workflowResult.Outcome == WorkbookFileOperationOutcome.Canceled)
            {
                RecordDiagnosticEvent("workbook_open_canceled", new Dictionary<string, string?>
                {
                    ["extension"] = ext,
                    ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                    ["format"] = target.Format.FormatName
                });
                return;
            }

            if (!workflowResult.Succeeded)
            {
                var exception = workflowResult.Exception ?? new InvalidOperationException(workflowResult.Message);
                RecordDiagnosticEvent("workbook_open_failed", new Dictionary<string, string?>
                {
                    ["extension"] = ext,
                    ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                    ["format"] = target.Format.FormatName,
                    ["reason"] = exception.GetType().Name
                });
                ShowOwnedMessage(
                    UiText.Format("MainWindowMessage_OpenFileFailed", exception.Message),
                    UiText.Get("MainWindowMessage_OpenErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            return;

            Task ApplyOpenedWorkbookAsync(
                WorkbookOpenWorkflowContext context,
                CancellationToken cancellationToken)
            {
            var plan = context.CompletionPlan;
            var result = context.Result;
            CloseFindReplaceDialogIfOpen();
            // When "New Window" siblings still view the current document, leave their context
            // (workbook ref / command bus / dirty state) untouched and continue on a fresh one:
            // File > Open loads into THIS window only, the siblings keep their document (H39).
            if (DocumentSharedWithOtherWindows())
            {
                DetachFromSharedDocumentContext();
            }
            _currentXlsxFeatureReport = plan.FeatureReport;
            ReplaceWorkbookSession(new StartupWorkbookLoadResult(
                plan.Workbook,
                plan.DisplayName,
                plan.Status,
                IsFallback: false,
                SourcePath: plan.SourcePath,
                OpenedAsTemplate: plan.OpenedAsTemplate,
                FeatureReport: plan.FeatureReport,
                LoadWarnings: result.LoadWarnings,
                SourceFileAccessIdentity: plan.SourceFileAccessIdentity));
            // WorkbookOpenService only recalculates (and thereby rebuilds the dependency graph) when
            // the file demands a full recalc on load; most real-world workbooks trust their cached
            // values and skip that branch entirely (WorkbookOpenService.ShouldRecalculateLoadedFormulas).
            // Without this, _recalcEngine's single persistent graph stays empty for every formula in
            // the newly opened workbook, so later edits to precedent cells never propagate to
            // dependents until a manual F9 or save/reopen. Rebuild unconditionally after every load,
            // matching the Avalonia host's WorkbookSessionFactory.Create.
            _recalcEngine.RebuildFormulaDependencies(_workbook);
            // This host does not route File > Open through WorkbookSessionFactory.Create, so it
            // must apply the same on-open selective volatile-cell refresh (NOW/OFFSET/etc.) that
            // Create() applies for the Avalonia host -- otherwise Automatic-mode volatile cells
            // stay stale (showing their on-disk cached values) until the next edit or F9.
            WorkbookSessionFactory.ApplyOnOpenVolatileRecalc(_recalcEngine, _workbook, _fileAdapters);
            // R126-app-watch-window-stale-after-open: this host's other workbook-swap choke points
            // (CreateNewWorkbook via RecalculateWorkbook, and CloseFindReplaceDialogIfOpen above for
            // Find/Replace) already make sure no modeless dialog survives with a reference into the
            // just-discarded workbook. OpenFileAsync swaps _workbook above but -- unlike
            // CreateNewWorkbook -- never routes through RecalculateWorkbook/RecalculateDirtyCells/
            // RebuildDependenciesAndCalculate/RecalculateIfAutomatic (the only places that call
            // _watchWindowDialog?.Refresh(), see R88-app-formula-auditing-5-1), so without this the
            // open Watch Window kept showing the discarded workbook's watched cells (a genuinely
            // different WatchedCells collection, see WatchWindowService) until the user manually
            // clicked Add/Refresh/Delete or edited a cell. The dialog's own getEntries callback reads
            // the instance field _workbook at call time, so this Refresh() call alone repopulates it
            // from the newly opened workbook -- exactly matching Excel, which drops a workbook's
            // watches the moment that workbook is gone.
            _watchWindowDialog?.Refresh();
            InvalidateToolbarVisualState();
            _workbook.Name = plan.DisplayName;
            _worksheetSelections.Clear();
            _worksheetViewStates.Clear();
            _currentSheetId = plan.ActiveSheetId;
            InvalidateNavigationCaches();
            _currentFileSourceLastWriteTimeUtc = result.SourceLastWriteTimeUtc;
            UpdateTitleBar();
            MarkWorkbookSaved();
            // Document-scoped broadcast: after a detach there are no same-document siblings,
            // so windows over other documents are untouched. (Kept for the defensive case of
            // a sibling that somehow still shares this ref — it must rebind, not go stale.)
            NotifyOtherWindowsOfWorkbookChange();
            // This window may have just left a "New Window" group — renumber so a now-lone
            // sibling drops its " - 1" suffix and this window starts unnumbered.
            _windowRegistry?.RefreshWindowNumbering();

            // Reload from disk immediately before writing: with multiple windows sharing this
            // process (View > New Window), each window's cached _recentFiles snapshot goes stale
            // the moment a sibling window registers/pins/removes an entry. Writing through the
            // stale cache would silently clobber the sibling's write (last-writer-wins data loss).
            ShowOpenProgress(CreateOpenProgress("preparing view", TimeSpan.Zero, null));
            cancellationToken.ThrowIfCancellationRequested();
            ApplyOpenedWorksheetViewState();
            RefreshSheetTabs();
            HideStartScreen();
            ShowOpenProgress(CreateOpenProgress("done", TimeSpan.Zero, 100));
            ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();
            ShowXlsxLoadWarningsIfNeeded(result.LoadWarnings);
            ApplyWorkbookReadOnlyOpenPolicy(_workbook, target.Path);
            RecordDiagnosticEvent("workbook_opened", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                ["format"] = target.Format.FormatName,
                ["worksheetCount"] = _workbook.Sheets.Count.ToString()
            });
            return Task.CompletedTask;
            }
        }
        catch (OperationCanceledException) when (operationCancellation.Token.IsCancellationRequested)
        {
            RecordDiagnosticEvent("workbook_open_canceled", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                ["format"] = target.Format.FormatName
            });
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("workbook_open_failed", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                ["format"] = target.Format.FormatName,
                ["reason"] = ex.GetType().Name
            });
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_OpenFileFailed", ex.Message),
                UiText.Get("MainWindowMessage_OpenErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isOpeningFile = false;
            HideOpenProgress();
        }
    }

    private static WorkbookProgressText CreateOpenProgress(string phase, TimeSpan elapsed, double? percent) =>
        WorkbookProgressTextFormatter.FormatOpen(phase, elapsed, percent, UiText.Get);

    private void ShowOpenProgress(WorkbookProgressText update) =>
        ShowOpenProgress(update.Title, update.Detail, update.Percent);

    private void ApplyOpenedWorksheetViewState()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeRow = sheet?.ActiveRow ?? 1;
        var activeCol = sheet?.ActiveCol ?? 1;
        SetActiveCell(new CellAddress(
            _currentSheetId,
            Math.Clamp(activeRow, 1u, CellAddress.MaxRow),
            Math.Clamp(activeCol, 1u, CellAddress.MaxCol)));

        // Freeze Panes is this window's own state (R89-freeze-split-per-window-1); at this point
        // (window/document just opened) GetEffectiveViewState seeds fresh from the Sheet, so this
        // is equivalent to the shared fields but keeps every scroll-math call site consistently
        // routed through the per-window store.
        var viewState = GetEffectiveViewState(sheet);
        VerticalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewTopRow,
            1,
            CellAddress.MaxRow,
            viewState.FrozenRows);
        HorizontalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewLeftCol,
            1,
            CellAddress.MaxCol,
            viewState.FrozenCols);
        UpdateViewport();
    }

    private void ShowOpenProgress(string title, string detail, double? percent = null)
    {
        ShowOperationFooterProgress(title, detail, percent);
        // Open replaces the workbook wholesale (it never serializes the live model), so a transparent
        // mouse blocker over the editing surface is enough — there is no torn-snapshot race to guard
        // against, and the sheet stays visible with the footer progress live, matching Excel.
        OpenProgressOverlay.Visibility = Visibility.Visible;
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private void HideOpenProgress()
    {
        HideOperationFooterProgress();
        BackstageProgressOverlayBinder.Hide(OpenProgressOverlay);
    }

    // Excel-style footer progress: the operation runs asynchronously while a small progress bar and a
    // live status message appear in the status bar (footer) instead of a modal dialog.  The message
    // leads with the file name so it reads as a real, specific action.
    private void ShowOperationFooterProgress(string title, string detail, double? percent)
    {
        var message = string.IsNullOrEmpty(_operationProgressFileName)
            ? detail
            : $"{_operationProgressFileName} — {detail}";
        BackstageProgressOverlayBinder.ShowStatusPanel(
            StatusSaveProgressPanel,
            StatusSaveProgressText,
            StatusSaveProgressBar,
            title: string.Empty,
            message,
            percent);
        StatusSaveProgressPanel.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, title);
        StatusSaveProgressCancelButton.Visibility = Visibility.Visible;
        StatusSaveProgressCancelButton.IsEnabled = _fileOperationCancellationSession.CanCancel;
        StatusReadyText.Visibility = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Collapsed;
    }

    private void HideOperationFooterProgress()
    {
        BackstageProgressOverlayBinder.Hide(StatusSaveProgressPanel);
        _operationProgressFileName = null;
        StatusSaveProgressCancelButton.Visibility = Visibility.Collapsed;
        RefreshStatusBar();
    }

    private void CancelFileOperation_Click(object sender, RoutedEventArgs e)
    {
        _fileOperationCancellationSession.CancelCurrent();
        StatusSaveProgressCancelButton.IsEnabled = _fileOperationCancellationSession.CanCancel;
    }

    private void SetFileOperationInputEnabled(bool isEnabled)
    {
        if (!isEnabled)
        {
            if (_fileOperationInputEnabledSnapshot is not null)
                return;

            _fileOperationInputEnabledSnapshot = [];
            foreach (UIElement child in RootGrid.Children)
            {
                if (ReferenceEquals(child, StatusBarRoot))
                    continue;

                _fileOperationInputEnabledSnapshot[child] = child.IsEnabled;
                child.IsEnabled = false;
            }

            StatusInteractiveControls.IsEnabled = false;
            return;
        }

        if (_fileOperationInputEnabledSnapshot is not null)
        {
            foreach (var (element, wasEnabled) in _fileOperationInputEnabledSnapshot)
                element.IsEnabled = wasEnabled;
            _fileOperationInputEnabledSnapshot = null;
        }

        StatusInteractiveControls.IsEnabled = true;
    }

    /// <summary>
    /// Acquires or releases one hold on this window's save-input gate, applying
    /// <see cref="SetFileOperationInputEnabled"/> only on the 0→1 / 1→0 transition. Both this
    /// window's own save (<see cref="SaveWorkbookToTargetAsync"/>) and a "New Window" sibling's
    /// save (via <see cref="ApplySaveInProgress"/>, broadcast through
    /// <see cref="WorkbookWindowRegistry.BroadcastSaveInProgress"/>) acquire a hold here, so if
    /// both happen to overlap on the same shared document, the earlier save finishing first does
    /// not prematurely re-enable input while the other save is still serializing the live
    /// workbook (R115-app-host-save-race).
    /// </summary>
    private void AdjustSaveGate(bool acquire)
    {
        if (acquire)
        {
            _saveGateHoldCount++;
            if (_saveGateHoldCount == 1)
                SetFileOperationInputEnabled(false);
            return;
        }

        if (_saveGateHoldCount > 0)
            _saveGateHoldCount--;
        if (_saveGateHoldCount == 0)
            SetFileOperationInputEnabled(true);
    }

    /// <summary>
    /// <see cref="IWorkbookWindow.ApplySaveInProgress"/>: applies (or releases) the save-input
    /// gate that a sibling window sharing this document is broadcasting for the duration of its
    /// own full-workbook save. Save serializes the LIVE Workbook instance on a background thread
    /// (see <see cref="SaveWorkbookToTargetAsync"/>), and a "New Window" sibling shares that exact
    /// Workbook/CommandBus instance (<see cref="AdoptSharedWorkbook"/>) — without this, a keystroke
    /// landing in this window while the OTHER window's background serialize enumerates the shared
    /// Sheet cell dictionaries could tear them structurally mid-enumeration (R115-app-host-save-race).
    /// </summary>
    public void ApplySaveInProgress(bool inProgress) => AdjustSaveGate(inProgress);

    // Start screen button handlers. The former rail-button forwarders (SsBackBtn_Click, SsNewBtn_Click,
    // SsOpenBtn_Click, SsCloseBtn_Click, SsHomeRibbonBtn_Click, SsShareBtn_Click, SsHomeNavBtn_Click,
    // SsInfoBtn_Click, SsPrintNavBtn_Click) were removed when the rail moved to the shared BackstageFrame —
    // its entries now invoke the underlying commands (RequestNewWorkbookAsync / OpenButton_Click / Close /
    // ShareWorkbookAsync) and pane shows (Show*View) directly. The Blank-workbook tile inside the Home pane
    // still uses this handler.
    private async void SsBlankWorkbook_Click(object sender, RoutedEventArgs e) => await RequestNewWorkbookAsync();

    private void RibbonTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRibbonSelectionChangedNormalization)
            return;

        if (RibbonTabs.SelectedItem == FileTab)
        {
            // Switch back to Home immediately so the tab never stays selected
            ChangeRibbonSelectionWithoutTabNormalization(() => RibbonTabs.SelectedIndex = 1);
            ShowStartScreen();
            NormalizeRibbonSurfaceAfterTabSelection();
            return;
        }

        NormalizeRibbonSurfaceAfterTabSelection();
    }
    private void InfoProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        ProtectWorkbookBtn_Click(sender, e);
        if (SsInfoView.Visibility == Visibility.Visible)
            UpdateInfoView();
    }

    private void InfoAccessibilityCheckerBtn_Click(object sender, RoutedEventArgs e)
    {
        HideStartScreen();
        AccessibilityCheckerBtn_Click(sender, e);
    }

    private void InfoWorkbookStatisticsBtn_Click(object sender, RoutedEventArgs e) =>
        WorkbookStatisticsBtn_Click(sender, e);

    private void InfoErrorCheckingBtn_Click(object sender, RoutedEventArgs e)
    {
        HideStartScreen();
        ErrorCheckBtn_Click(sender, e);
    }

    private void SsAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var plan = BuildLocalAccountPanePlan();
        var message = WpfResourceKeyTextResolver.Resolve(
            DeferredCommandMessagePlanner.LocalAccountInfo(),
            body => FreeXBackstageAccountPanePlanner.FormatMessageBody(plan, body, UiText.Get));
        ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private FreeXBackstageAccountPanePlan BuildLocalAccountPanePlan()
    {
        var accountInfo = LocalAccountInfoPlanner.Build(new LocalAccountInfoRequest(
            typeof(MainWindow).Assembly,
            DeviceName: Environment.MachineName,
            UserName: _options.UserName,
            LocalOsUserName: Environment.UserName,
            LocalOsUserDomain: Environment.UserDomainName,
            OptionsFile: AppOptionsStore.StorePath,
            CurrentWorkbookPath: _currentFilePath,
            CurrentWorkbookName: _workbook.Name,
            Workbook: _workbook,
            HasSelection: SheetGrid.SelectedRange is not null));

        return FreeXBackstageAccountPanePlanner.Build(
            LocalAccountInfoPlanner.CreateBackstageAccountPaneRequest(
                accountInfo,
                _currentFilePath,
                _workbook.Name));
    }

    private void SsMoreTemplatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.OnlineTemplatesExcluded());
        ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowOptionsDialog();
    }

    private void ErrorCheckingOptionsBtn_Click(object sender, RoutedEventArgs e) =>
        ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking);

    private void ShowOptionsDialog(OptionsDialogInitialSection initialSection = OptionsDialogInitialSection.General)
    {
        var previousAppLanguage = AppLanguageCatalog.NormalizeCultureName(_options.AppLanguage);
        var dlg = new OptionsDialog(
            _options,
            _workbook.DisabledFormulaErrorCodes,
            initialSection,
            CalculationOptionsDialogState.FromWorkbook(_workbook),
            _optionsRuntimeSession);
        if (ShowOwnedDialog(dlg) == true)
        {
            _options = dlg.Result;
            var appLanguageChanged = !StringComparer.OrdinalIgnoreCase.Equals(
                previousAppLanguage,
                AppLanguageCatalog.NormalizeCultureName(_options.AppLanguage));
            if (appLanguageChanged)
                AppLocalization.Bootstrap.ApplyAppLanguage(_options.AppLanguage);

            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
            ApplyOptionsCalculationSubmission(dlg.CalculationSubmission);
            RebuildQuickAccessToolbar();
            ApplyOptionsWorksheetViewSettings();
            ApplyOptionsToView();
            UpdateViewport();

            if (appLanguageChanged)
            {
                ShowOwnedMessage(
                    UiText.Get("Options_AppLanguageRestartMessage"),
                    UiText.Get("Options_Language"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }

    /// <summary>
    /// Applies the portable calculation submission emitted by the Options dialog.
    /// </summary>
    private void ApplyOptionsCalculationSubmission(CalculationOptionsSubmission? submission)
    {
        var outcome = CalculationOptionsSubmissionCoordinator.Apply(CalculationWorkflow, submission);
        if (outcome.ModeOutcome is { } modeOutcome)
            ApplyCalculationWorkflowOutcome(modeOutcome);
    }

    private void ApplyOptionsWorksheetViewSettings()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        if (sheet.ShowGridlines == _options.ShowGridlines &&
            sheet.ShowHeadings == _options.ShowHeadings)
            return;

        TryExecuteGroupedSheetCommand(
            "Worksheet View Options",
            sheetId => new SetWorksheetViewOptionsCommand(
                sheetId,
                _options.ShowGridlines,
                _options.ShowHeadings,
                _workbook.GetSheet(sheetId)?.ShowRulers ?? true));
    }

    private void ApplyFormulaErrorCheckingOptions(IReadOnlySet<string> disabledErrorCodes)
    {
        CalculationWorkflow.ChangeFormulaErrorRules(disabledErrorCodes);
    }

    private bool OpenFileBackstageFromKeyTip()
    {
        ShowStartScreen();
        if (RibbonTabs != null)
            RibbonTabs.SelectedIndex = 1;
        return true;
    }

    private async void SsRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is RecentFileViewModel vm)
            await OpenFileAsync(vm.Path);
    }

    private void SsPinItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            // Reload-then-mutate: see ReloadRecentFilesStore for why the cached _recentFiles
            // instance is unsafe to write through when multiple windows share this process.
            ReloadRecentFilesStore().Pin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsUnpinItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            ReloadRecentFilesStore().Unpin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsRemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            ReloadRecentFilesStore().Remove(vm.Path);
            _allRecentItems.RemoveAll(x => x.Path == vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
    }

    private static RecentFileViewModel? GetContextMenuViewModel(object menuItemSender)
    {
        if (menuItemSender is MenuItem mi &&
            mi.Parent is ContextMenu cm &&
            cm.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as RecentFileViewModel;
        if (menuItemSender is FrameworkElement direct)
            return direct.DataContext as RecentFileViewModel;
        return null;
    }

    private void SsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateSsRecentList(SsSearchBox.Text);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var plan = WorkbookFilePickerPlanner.BuildOpenDialogPlan(_fileAdapters);
        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            plan.Filter,
            plan.DefaultExtensionWithDot,
            checkFileExists: true,
            multiselect: false);

        if (result.Chosen)
            await OpenFileAsync(result.FileName!);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // P2b: Save resolves Save-vs-Save-As through the shared SaveResolvedAsync helper (which routes the
        // existing-path-vs-dialog DECISION through FileLifecyclePlanner.PlanSave), the same path the
        // dirty-gate's "Save then proceed" branch takes — one resolution, shared decision truth.
        var saved = await SaveResolvedAsync();

        if (saved && IsStartScreenVisible())
            HideStartScreen();
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveWorkbookWithDialogAsync())
            HideStartScreen();
    }

    private async Task<bool> SaveWorkbookWithDialogAsync()
    {
        var plan = WorkbookFilePickerPlanner.BuildSaveDialogPlan(
            _fileAdapters,
            _workbook.Name,
            _options.DefaultFormat);
        var result = WpfFileDialogService.ShowSaveDialog(
            this,
            plan.Filter,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithDot,
            plan.FilterIndex);

        if (result.Chosen)
        {
            if (!_fileWorkflow.TryResolveSaveTarget(
                    result.FileName!, out var target, out _, result.FilterIndex) || target is null)
            {
                return false;
            }

            return await SaveWorkbookToTargetAsync(target);
        }

        return false;
    }

    private async Task<bool> SaveWorkbookToTargetAsync(FileSaveTarget target)
    {
        if (_isSavingFile)
            return false;

        if (_fileWorkflow.ShouldSkipSaveTargetWrite(_workbookDirty, _currentFilePath, target))
            return true;

        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return false;

        // Save-As to a plain/single-sheet lossy format (CSV/TXT/PRN/SLK/DIF/DBF, ...) has no gate at
        // all otherwise: a multi-sheet workbook or one with charts would write silently, dropping
        // every sheet but the current one plus any charts, with no warning the .xlsx path already
        // gives via ConfirmUnsupportedXlsxFeatureSave above.
        if (ext != ".xlsx" &&
            LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(_workbook, ext) &&
            !ConfirmLossyFormatFeatureLossSave(ext))
        {
            return false;
        }

        using var operationCancellation = _fileOperationCancellationSession.Begin();
        try
        {
            _isSavingFile = true;

            // Block all user input for the duration of the save.  Unlike open (which builds a fresh
            // workbook), save serializes the LIVE model on a background thread, so a concurrent edit —
            // including a keyboard edit, which a mouse-only overlay would not stop — could tear the
            // snapshot.  Disable the app surface while leaving the status-bar cancel affordance live;
            // the generation check below is belt-and-suspenders.
            //
            // A "New Window" sibling shares this EXACT Workbook/CommandBus instance (see
            // AdoptSharedWorkbook), so disabling only this window's own surface is not enough: the
            // sibling would stay fully interactive while this window's background thread enumerates
            // the shared Sheet cell dictionaries, and a keystroke landing there could tear them
            // structurally mid-enumeration.  Extend the same input gate to every OTHER window
            // viewing this document via the registry (R115-app-host-save-race).
            AdjustSaveGate(acquire: true);
            _windowRegistry?.BroadcastSaveInProgress(this, inProgress: true);
            _operationProgressFileName = System.IO.Path.GetFileName(target.Path);
            ShowSaveProgress(CreateSaveProgress("preparing", TimeSpan.Zero, 1));
            var progress = new Progress<WorkbookSaveProgressUpdate>(update =>
                ShowSaveProgress(WorkbookProgressTextFormatter.FormatSave(update, UiText.Get)));
            var saveService = new WorkbookSaveService();
            var workflowResult = await _fileWorkflow.SaveTargetAsync(new WorkbookSaveWorkflowRequest(
                _workbookDirty,
                _currentFilePath,
                target,
                _currentFileSourceLastWriteTimeUtc,
                GetCurrentWorkbook: () => _workbook,
                GetDirtyGeneration: () => _workbookDirtyGeneration,
                ConfirmExternallyModifiedOverwrite: ConfirmExternallyModifiedFileOverwrite,
                ProjectViewStateForSave: ReconcileViewStateForSave,
                SaveAsync: invocation => saveService.SaveAsync(
                    invocation.Target.Path,
                    invocation.Target.Adapter,
                    invocation.Workbook,
                    progress,
                    invocation.CancellationToken,
                    invocation.ExpectedLastWriteTimeUtc),
                ApplyCompletion: ApplyWpfSaveCompletion,
                CancellationToken: operationCancellation.Token));

            if (workflowResult.Outcome == WorkbookFileOperationOutcome.Canceled)
            {
                RecordDiagnosticEvent("workbook_save_canceled", new Dictionary<string, string?>
                {
                    ["extension"] = ext,
                    ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                    ["format"] = target.Adapter.FormatName
                });
                return false;
            }

            if (workflowResult.Outcome == WorkbookFileOperationOutcome.Rejected)
                return false;

            if (workflowResult.Outcome == WorkbookFileOperationOutcome.ExternalWriteConflict)
            {
                RecordDiagnosticEvent("workbook_save_externally_modified", new Dictionary<string, string?>
                {
                    ["extension"] = ext,
                    ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                    ["format"] = target.Adapter.FormatName
                });
                ShowOwnedMessage(
                    UiText.Format("MainWindowMessage_ExternallyModifiedFileBody", System.IO.Path.GetFileName(target.Path)),
                    UiText.Get("MainWindowMessage_ExternallyModifiedFileTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (workflowResult.Outcome == WorkbookFileOperationOutcome.Failed)
            {
                var saveException = workflowResult.Exception ?? new InvalidOperationException("Save failed.");
                RecordDiagnosticEvent("workbook_save_failed", new Dictionary<string, string?>
                {
                    ["extension"] = ext,
                    ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                    ["format"] = target.Adapter.FormatName,
                    ["reason"] = saveException.GetType().Name
                });
                ShowOwnedMessage(
                    UiText.Format("MainWindowMessage_SaveFileFailed", saveException.Message),
                    UiText.Get("MainWindowMessage_SaveErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var executionResult = workflowResult.RequireExecutionResult();
            _currentFileSourceLastWriteTimeUtc = executionResult.SavedLastWriteTimeUtc;
            ShowXlsxSaveWarningsIfNeeded(executionResult.Warnings);
            RecordDiagnosticEvent("workbook_saved", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                ["format"] = target.Adapter.FormatName,
                ["worksheetCount"] = _workbook.Sheets.Count.ToString()
            });
            return true;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("workbook_save_failed", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileFormatResolver.SafeFileTypeFromExtension(ext),
                ["format"] = target.Adapter.FormatName,
                ["reason"] = ex.GetType().Name
            });
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_SaveFileFailed", ex.Message),
                UiText.Get("MainWindowMessage_SaveErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isSavingFile = false;
            AdjustSaveGate(acquire: false);
            _windowRegistry?.BroadcastSaveInProgress(this, inProgress: false);
            HideSaveProgress();
        }
    }

    private void ApplyWpfSaveCompletion(SaveCompletionPlan plan)
    {
        if (plan.ApplyFileContext && plan.FileContext is { } fileContext)
        {
            _currentFilePath = fileContext.Path;
            _workbook.Name = fileContext.DisplayName;
        }

        if (plan.MarkSaved)
            MarkWorkbookSaved();

        UpdateTitleBar();
        if (plan.ApplyFileContext)
            NotifyOtherWindowsOfWorkbookChange();
    }

    private void ShowSaveProgress(string title, string detail, double? percent = null)
    {
        ShowOperationFooterProgress(title, detail, percent);
    }

    private void ShowSaveProgress(WorkbookProgressText update) =>
        ShowSaveProgress(update.Title, update.Detail, update.Percent);

    private static WorkbookProgressText CreateSaveProgress(string phase, TimeSpan elapsed, double? percent) =>
        WorkbookProgressTextFormatter.FormatSave(phase, elapsed, percent, UiText.Get);

    private void HideSaveProgress()
    {
        HideOperationFooterProgress();
    }

    private bool ConfirmExternallyModifiedFileOverwrite(string path)
    {
        return ShowOwnedSynchronousPrompt(
            FreeXSynchronousPromptCatalog.ForExternallyModifiedFile(path)) == UserMessageResult.Yes;
    }

    private bool ConfirmUnsupportedXlsxFeatureSave()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return true;

        var message = WpfResourceKeyTextResolver.Resolve(
            DeferredCommandMessagePlanner.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport));

        var result = ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private bool ConfirmLossyFormatFeatureLossSave(string extension)
    {
        return ShowOwnedSynchronousPrompt(
            FreeXSynchronousPromptCatalog.ForLossyFormatFeatureLoss(extension)) == UserMessageResult.Yes;
    }

    /// <summary>
    /// A workbook saved with "Read-Only Recommended" (<c>WorkbookFileSharingModel.ReadOnlyRecommended</c>)
    /// or a write-reservation password (<c>ReservationPassword</c>) used to open fully editable with no
    /// prompt at all -- the metadata round-tripped on Save but was never enforced (SECURITY finding,
    /// round 134). This is a workbook-integrity/authoring control only, matching Excel's "Password to
    /// Modify" -- it is not encryption and provides no confidentiality; the file contents remain
    /// plainly readable regardless of the password.
    /// <para>
    /// A write-reservation password now actually gates write access: the host realizes the native
    /// password dialog, while <see cref="WorkbookReadOnlySession"/> classifies the prompt, verifies the
    /// stored password, and owns the resulting read-only state. A wrong password or Cancel falls back
    /// to a read-only session rather than refusing to open the file.
    /// </para>
    /// <see cref="ResolveExistingSaveTarget"/> (MainWindow.WorkbookLifecycle.cs) reads the
    /// shared read-only state on every Save to force Save-over-original through the
    /// Save-As dialog instead of a silent overwrite (R83-services-doc-recovery-props-5-1). Individual
    /// edit commands are not yet blocked -- that remains out of scope (tracked separately).
    /// <para>
    /// <paramref name="filePath"/> is the on-disk path just opened (passed through to
    /// <see cref="WorkbookReadOnlySession.RunOpen"/> so it can classify an OS-level read-only file
    /// -- read-only attribute, read-only share/volume, or a denied ACL -- even when neither
    /// embedded workbook flag above is set; previously that combination opened fully editable
    /// with zero indication until the first Save failed, round 149).
    /// </para>
    /// </summary>
    private WorkbookReadOnlyOpenOutcome ApplyWorkbookReadOnlyOpenPolicy(Workbook workbook, string? filePath = null) =>
        _workbookReadOnlySession.RunOpen(workbook, new WpfWorkbookReadOnlyOpenPromptPort(this), filePath);

    private sealed class WpfWorkbookReadOnlyOpenPromptPort(MainWindow owner) : IWorkbookReadOnlyOpenPromptPort
    {
        public WorkbookReadOnlyRecommendationChoice PromptReadOnlyRecommended(WorkbookReadOnlyOpenPlan plan) =>
            owner.ShowOwnedSynchronousPrompt(
                FreeXSynchronousPromptCatalog.ForReadOnlyRecommended(plan.WorkbookName)) == UserMessageResult.Yes
                ? WorkbookReadOnlyRecommendationChoice.OpenReadOnly
                : WorkbookReadOnlyRecommendationChoice.OpenEditable;

        public WorkbookReservationPasswordResponse PromptReservationPassword(WorkbookReadOnlyOpenPlan plan) =>
            WorkbookReservationPasswordResponse.FromPromptResult(
                owner.ResolveReservationPasswordPrompt(plan.WorkbookName));

        public void ShowIncorrectReservationPasswordNotice(WorkbookReadOnlyOpenPlan plan) =>
            owner.ShowOwnedMessage(
                UiText.Get("MainWindowMessage_ReservationPasswordIncorrectBody"),
                UiText.Get("MainWindowMessage_ReservationPasswordTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
    }

    private string? ResolveReservationPasswordPrompt(string workbookName)
    {
        var handled = false;
        string? password = null;
        TryResolveExternalReservationPasswordPrompt(workbookName, ref handled, ref password);
        return handled ? password : ShowReservationPasswordPromptDialog(workbookName);
    }

    private string? ShowReservationPasswordPromptDialog(string workbookName)
    {
        var prompt = UiText.Format("MainWindowMessage_ReservationPasswordPromptFormat", workbookName);
        var dialog = new PasswordProtectionDialog(
            UiText.Get("MainWindowMessage_ReservationPasswordTitle"),
            prompt)
        {
            Owner = this
        };

        return dialog.ShowDialog() == true ? dialog.Password ?? string.Empty : null;
    }

    private void ShowUnsupportedXlsxFeatureOpenWarningIfNeeded()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return;

        var message = WpfResourceKeyTextResolver.Resolve(
            DeferredCommandMessagePlanner.UnsupportedXlsxFeatureOpenWarning(_currentXlsxFeatureReport));
        ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowXlsxLoadWarningsIfNeeded(IReadOnlyList<string>? warnings)
    {
        if (warnings is not { Count: > 0 })
            return;

        const int maxShown = 10;
        var lines = warnings.Take(maxShown).ToList();
        var body = UiText.Format(
            "MainWindowMessage_XlsxLoadWarningsBodyFormat",
            string.Join("\n", lines.Select(w => UiText.Format("MainWindowMessage_BulletListItemFormat", w))));
        if (warnings.Count > maxShown)
            body += UiText.Format("MainWindowMessage_XlsxLoadWarningsMoreFormat", warnings.Count - maxShown);

        ShowOwnedMessage(
            body,
            UiText.Get("MainWindowMessage_FileOpenedWithWarningsTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowXlsxSaveWarningsIfNeeded(IReadOnlyList<string>? warnings)
    {
        if (warnings is not { Count: > 0 })
            return;

        const int maxShown = 10;
        var lines = warnings.Take(maxShown).ToList();
        var body = UiText.Format(
            "MainWindowMessage_XlsxSaveWarningsBodyFormat",
            string.Join("\n", lines.Select(w => UiText.Format("MainWindowMessage_BulletListItemFormat", w))));
        if (warnings.Count > maxShown)
            body += UiText.Format("MainWindowMessage_XlsxSaveWarningsMoreFormat", warnings.Count - maxShown);

        ShowOwnedMessage(
            body,
            UiText.Get("MainWindowMessage_FileSavedWithWarningsTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
