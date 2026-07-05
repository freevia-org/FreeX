using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
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

        // Resolve the printer to a PrintQueue (null = Windows default).
        System.Printing.PrintQueue? printQueue = null;
        if (!string.IsNullOrWhiteSpace(settings.PrinterName))
        {
            try
            {
                using var server = new System.Printing.LocalPrintServer();
                foreach (var q in server.GetPrintQueues())
                {
                    if (string.Equals(q.FullName, settings.PrinterName, StringComparison.OrdinalIgnoreCase))
                    {
                        printQueue = q;
                        break;
                    }
                }
            }
            catch (System.Printing.PrintSystemException)
            {
                // Fall through to null (Windows default).
            }
        }

        // Apply page range if one was requested.
        System.Windows.Documents.DocumentPaginator paginator = _backstagePrintPreviewDocument.DocumentPaginator;
        if (settings.PageFrom.HasValue || settings.PageTo.HasValue)
        {
            var totalPages = paginator.PageCount;
            if (PrintSettingsPlanner.TryValidatePageRange(
                    settings.PageFrom, settings.PageTo, totalPages,
                    out var from, out var to))
            {
                paginator = new PageRangeDocumentPaginator(
                    _backstagePrintPreviewDocument.DocumentPaginator,
                    new ExportPageRange(from, to));
            }
        }

        NativePrintDialogService.ShowPrintDialogAndPrint(
            paginator,
            printQueue,
            PrintSettingsPlanner.ClampCopies(settings.Copies),
            settings.Collated,
            settings.Sides,
            this);
    }

    private void UpdateInfoView()
    {
        var activeSheet = _workbook.GetSheet(_currentSheetId);
        var info = BackstageInfoPlanner.Build(
            _workbook,
            _currentFilePath,
            BackstageInfoResources.Strings,
            activeSheet,
            hasSelection: SheetGrid.SelectedRange is not null);
        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.WpfInfoPane,
            CreateBackstageInfoPaneRequest(info));

        foreach (var detail in pane.Details)
        {
            ResolveBackstageInfoDetailTextBlock(detail.Id).Text = ResolveBackstageTextValue(detail.Value);
        }

        RefreshBackstageInfoProtectionButton();
    }

    private static FreeXBackstageInfoPaneRequest CreateBackstageInfoPaneRequest(BackstageInfoPlan plan) =>
        new(
            plan.WorkbookName,
            plan.FilePath,
            plan.SheetCount,
            plan.Format,
            plan.FileSize,
            plan.LastModified,
            plan.SharingStatus,
            plan.ExportStatus,
            plan.Summary.WorkbookProtectionSummary,
            plan.Summary.ActiveSheetProtectionSummary,
            plan.StatisticsSummary,
            plan.AccessibilitySummary,
            plan.FormulaErrorSummary);

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
        value.TextKey is { } key
            ? UiText.Get(key)
            : value.Text ?? string.Empty;

    private void RefreshBackstageInfoProtectionButton()
    {
        if (InfoProtectWorkbookButton is null)
            return;

        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        InfoProtectWorkbookButton.Content = uiText.ButtonContent;
        System.Windows.Automation.AutomationProperties.SetName(InfoProtectWorkbookButton, uiText.ButtonContent);
        System.Windows.Automation.AutomationProperties.SetHelpText(InfoProtectWorkbookButton, uiText.TooltipDescription);
        RibbonTooltip.SetTitle(InfoProtectWorkbookButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(InfoProtectWorkbookButton, uiText.TooltipDescription);
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
        var plan = BackstageRecentFileListPlanner.Build(
            ReloadRecentFilesStore().Snapshot(),
            filter,
            System.IO.File.Exists);
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
        AdoptWorkbookAsInitial(NewWorkbookFactory.Create(_options, workbookName));
    }

    /// <summary>
    /// Replaces the live workbook with <paramref name="wb"/> and rebinds the grid/title/tabs to it, mirroring
    /// the File &gt; New path. Used by the <c>--parity-capture</c> mode to render a fixed demo workbook so the
    /// WPF and Avalonia <c>grid.demo</c> surfaces compare identical content (see ParityDemoWorkbookFactory).
    /// </summary>
    internal void AdoptWorkbookForParityCapture(Workbook wb)
    {
        ArgumentNullException.ThrowIfNull(wb);
        CloseFindReplaceDialogIfOpen();
        AdoptWorkbookAsInitial(wb);
    }

    private void AdoptWorkbookAsInitial(Workbook wb)
    {
        // When "New Window" siblings still view the current document, leave their context
        // (workbook ref / command bus / dirty state) untouched and continue on a fresh one:
        // File > New replaces the document in THIS window only (H39).
        if (DocumentSharedWithOtherWindows())
            DetachFromSharedDocumentContext();
        _workbook = wb;
        _workbookRef.Current = wb;
        InvalidateToolbarVisualState();
        _worksheetSelections.Clear();
        _currentSheetId = wb.Sheets[0].Id;
        InvalidateNavigationCaches();
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
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
    /// Opens a recovery snapshot into this window without recording the snapshot path in recent
    /// files. Called from App.xaml.cs during startup recovery; the snapshot path is a temporary
    /// .fxl file that must never appear in the MRU list.
    /// </summary>
    internal Task OpenRecoverySnapshotAsync(string snapshotPath) =>
        OpenFileAsync(snapshotPath, suppressRecentFiles: true);

    private async Task OpenFileAsync(string path, bool suppressRecentFiles = false)
    {
        if (!WorkbookOpenTargetPlanner.TryCreateOpenTarget(_fileAdapters, path, out var target, out _))
            return;
        var ext = FileFormatResolver.NormalizeExtension(target!.Extension);
        if (_isOpeningFile) return;
        _isOpeningFile = true;
        using var operationCancellation = BeginFileOperationCancellation();
        try
        {
            // Skip the save prompt when a "New Window" sibling still views this document — the
            // document (and its dirty state) stays alive there; only this view is being replaced.
            if (!DocumentSharedWithOtherWindows() &&
                !await CanProceedAfterSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeOpeningWorkbook")))
                return;

            _operationProgressFileName = System.IO.Path.GetFileName(target.Path);
            ShowOpenProgress(CreateOpenProgress("preparing", TimeSpan.Zero, 1));

            var progress = new Progress<OpenProgressUpdate>(
                update => ShowOpenProgress(update.Title, update.Detail, update.Percent));
            var loader = new OpenWorkbookLoader(workbook => _recalcEngine.RecalculateAllFormulas(workbook));
            var result = await loader.LoadAsync(
                target.Path,
                target.Adapter,
                ext,
                target.Format,
                progress,
                operationCancellation.Token);
            operationCancellation.Token.ThrowIfCancellationRequested();

            var plan = WorkbookFileCompletionPlanner.PlanOpen(
                target,
                new FreeX.App.Services.WorkbookOpenResult(
                    result.Workbook,
                    result.FeatureReport,
                    result.DisplayName,
                    result.OpenedAsTemplate,
                    result.LoadWarnings ?? []),
                suppressRecentFiles);
            CloseFindReplaceDialogIfOpen();
            // When "New Window" siblings still view the current document, leave their context
            // (workbook ref / command bus / dirty state) untouched and continue on a fresh one:
            // File > Open loads into THIS window only, the siblings keep their document (H39).
            if (DocumentSharedWithOtherWindows())
                DetachFromSharedDocumentContext();
            _currentXlsxFeatureReport = plan.FeatureReport;
            _workbook = plan.Workbook;
            _workbookRef.Current = plan.Workbook;
            // OpenWorkbookLoader only recalculates (and thereby rebuilds the dependency graph) when
            // the file demands a full recalc on load; most real-world workbooks trust their cached
            // values and skip that branch entirely (WorkbookOpenService.ShouldRecalculateLoadedFormulas).
            // Without this, _recalcEngine's single persistent graph stays empty for every formula in
            // the newly opened workbook, so later edits to precedent cells never propagate to
            // dependents until a manual F9 or save/reopen. Rebuild unconditionally after every load,
            // matching the Avalonia host's WorkbookSessionFactory.Create.
            _recalcEngine.RebuildFormulaDependencies(_workbook);
            InvalidateToolbarVisualState();
            _workbook.Name = plan.DisplayName;
            _worksheetSelections.Clear();
            _currentSheetId = plan.ActiveSheetId;
            InvalidateNavigationCaches();
            _currentFilePath = plan.CurrentFilePath;
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
            RecentFileRegistrationService.RegisterIfNeeded(ReloadRecentFilesStore, plan.RecentFileRegistration);
            ShowOpenProgress(CreateOpenProgress("preparing view", TimeSpan.Zero, null));
            operationCancellation.Token.ThrowIfCancellationRequested();
            ApplyOpenedWorksheetViewState();
            RefreshSheetTabs();
            HideStartScreen();
            ShowOpenProgress(CreateOpenProgress("done", TimeSpan.Zero, 100));
            ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();
            ShowXlsxLoadWarningsIfNeeded(result.LoadWarnings);
            RecordDiagnosticEvent("workbook_opened", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = target.Format.FormatName,
                ["worksheetCount"] = _workbook.Sheets.Count.ToString()
            });
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            RecordDiagnosticEvent("workbook_open_canceled", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = target.Format.FormatName
            });
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("workbook_open_failed", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
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
            ClearFileOperationCancellation(operationCancellation);
            HideOpenProgress();
        }
    }

    private static OpenProgressUpdate CreateOpenProgress(string phase, TimeSpan elapsed, double? percent) =>
        FromSharedOpenProgressText(WorkbookProgressTextFormatter.FormatOpen(phase, elapsed, percent, UiText.Get));

    private static OpenProgressUpdate FromSharedOpenProgressText(WorkbookProgressText text) =>
        new(text.Title, text.Detail, text.Percent);

    private void ShowOpenProgress(OpenProgressUpdate update) =>
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

        VerticalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewTopRow,
            1,
            CellAddress.MaxRow,
            sheet?.FrozenRows ?? 0);
        HorizontalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewLeftCol,
            1,
            CellAddress.MaxCol,
            sheet?.FrozenCols ?? 0);
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
        StatusSaveProgressCancelButton.IsEnabled = true;
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

    private CancellationTokenSource BeginFileOperationCancellation()
    {
        _fileOperationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _fileOperationCancellation = cancellation;
        return cancellation;
    }

    private void ClearFileOperationCancellation(CancellationTokenSource operationCancellation)
    {
        if (ReferenceEquals(_fileOperationCancellation, operationCancellation))
            _fileOperationCancellation = null;
    }

    private void CancelFileOperation_Click(object sender, RoutedEventArgs e)
    {
        _fileOperationCancellation?.Cancel();
        StatusSaveProgressCancelButton.IsEnabled = false;
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
        var plan = LocalAccountPlanner.Create(
            _options,
            _currentFilePath,
            _workbook.Name,
            workbook: _workbook,
            hasSelection: SheetGrid.SelectedRange is not null);
        var message = DeferredCommandMessages.LocalAccountInfo(plan);
        ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsMoreTemplatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = DeferredCommandMessages.OnlineTemplatesExcluded();
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
            OptionsDialogCalculationSettings.FromWorkbook(_workbook));
        if (ShowOwnedDialog(dlg) == true)
        {
            _options = dlg.Result;
            var appLanguageChanged = !StringComparer.OrdinalIgnoreCase.Equals(
                previousAppLanguage,
                AppLanguageCatalog.NormalizeCultureName(_options.AppLanguage));
            if (appLanguageChanged)
                AppLocalization.ApplyAppLanguage(_options.AppLanguage);

            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
            ApplyOptionsCalculationSettings(dlg.CalculationSettingsResult);
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
    /// Applies the Options dialog's Formulas panel calc-mode/iterative-calculation edits to the
    /// live workbook. <paramref name="calcSettings"/> is null when the dialog detected no change
    /// from what it seeded (see <see cref="OptionsDialog.CalculationSettingsResult"/>), so an
    /// unrelated Options edit never silently flips the workbook's calculation state.
    /// </summary>
    private void ApplyOptionsCalculationSettings(OptionsDialogCalculationSettings? calcSettings)
    {
        if (calcSettings is null)
            return;

        var wantMode = calcSettings.AutoCalculate ? WorkbookCalculationMode.Automatic : WorkbookCalculationMode.Manual;
        if (_workbook.CalculationMode != wantMode &&
            TryExecuteCommand(new SetCalculationModeCommand(wantMode), "Calculation Options") &&
            wantMode == WorkbookCalculationMode.Automatic)
        {
            RecalculateWorkbook();
        }

        if (_workbook.IterativeCalculation != calcSettings.IterativeCalculation ||
            _workbook.MaxCalculationIterations != calcSettings.MaxCalculationIterations ||
            _workbook.MaxCalculationChange != calcSettings.MaxCalculationChange)
        {
            TryExecuteCommand(
                new SetIterativeCalculationOptionsCommand(
                    calcSettings.IterativeCalculation,
                    calcSettings.MaxCalculationIterations,
                    calcSettings.MaxCalculationChange),
                "Calculation Options");
        }
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
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var shouldDisable = disabledErrorCodes.Contains(rule.ErrorCode);
            var isDisabled = _workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode);
            if (shouldDisable == isDisabled)
                continue;

            if (!TryExecuteCommand(
                    new SetFormulaErrorCheckingRuleCommand(rule.ErrorCode, enabled: !shouldDisable),
                    "Error Checking Options"))
            {
                return;
            }
        }
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
            if (!WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(_fileAdapters, result.FileName!, result.FilterIndex, out var target) ||
                target is null)
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

        if (WorkbookFileLifecycleCoordinator.PlanSaveTargetWrite(_workbookDirty, _currentFilePath, target)
            == WorkbookSaveTargetIntent.SkipCleanCurrentPath)
            return true;

        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return false;

        // Capture identity/generation before any await so we can detect edits or
        // workbook replacement that occur while the serialization is running.
        var generationAtSaveStart = _workbookDirtyGeneration;
        var workbookAtSaveStart = _workbook;

        using var operationCancellation = BeginFileOperationCancellation();
        try
        {
            _isSavingFile = true;

            // Block all user input for the duration of the save.  Unlike open (which builds a fresh
            // workbook), save serializes the LIVE model on a background thread, so a concurrent edit —
            // including a keyboard edit, which a mouse-only overlay would not stop — could tear the
            // snapshot.  Disable the app surface while leaving the status-bar cancel affordance live;
            // the generation check below is belt-and-suspenders.
            SetFileOperationInputEnabled(false);
            _operationProgressFileName = System.IO.Path.GetFileName(target.Path);
            ShowSaveProgress(CreateSaveProgress("preparing", TimeSpan.Zero, 1));
            var progress = new Progress<SaveProgressUpdate>(
                update => ShowSaveProgress(update.Title, update.Detail, update.Percent));
            var saveWarnings = await new SaveWorkbookWriter().SaveAsync(
                target.Path,
                target.Adapter,
                _workbook,
                progress,
                operationCancellation.Token);
            operationCancellation.Token.ThrowIfCancellationRequested();

            var plan = SaveCompletionPlanner.Plan(
                generationAtSaveStart,
                _workbookDirtyGeneration,
                sameWorkbook: ReferenceEquals(_workbook, workbookAtSaveStart),
                target.Path);

            if (plan.ApplyFileContext && plan.FileContext is { } fileContext)
            {
                _currentFilePath = fileContext.Path;
                _workbook.Name = fileContext.DisplayName;
                RecentFileRegistrationService.RegisterIfNeeded(ReloadRecentFilesStore, fileContext.RecentFileRegistration);
            }

            if (plan.MarkSaved)
                MarkWorkbookSaved();

            UpdateTitleBar();
            // Notify sibling windows so they pick up the new file path/name in their
            // title bars.  MarkWorkbookSaved() already fans out the dirty-state change;
            // this call ensures the full viewport/title refresh for the file-context.
            if (plan.ApplyFileContext)
                NotifyOtherWindowsOfWorkbookChange();
            ShowXlsxSaveWarningsIfNeeded(saveWarnings);
            RecordDiagnosticEvent("workbook_saved", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = target.Adapter.FormatName,
                ["worksheetCount"] = _workbook.Sheets.Count.ToString()
            });
            return true;
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            RecordDiagnosticEvent("workbook_save_canceled", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = target.Adapter.FormatName
            });
            return false;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("workbook_save_failed", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
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
            ClearFileOperationCancellation(operationCancellation);
            _isSavingFile = false;
            SetFileOperationInputEnabled(true);
            HideSaveProgress();
        }
    }

    private void ShowSaveProgress(string title, string detail, double? percent = null)
    {
        ShowOperationFooterProgress(title, detail, percent);
    }

    private void ShowSaveProgress(SaveProgressUpdate update) =>
        ShowSaveProgress(update.Title, update.Detail, update.Percent);

    private static SaveProgressUpdate CreateSaveProgress(string phase, TimeSpan elapsed, double? percent) =>
        FromSharedSaveProgressText(WorkbookProgressTextFormatter.FormatSave(phase, elapsed, percent, UiText.Get));

    private static SaveProgressUpdate FromSharedSaveProgressText(WorkbookProgressText text) =>
        new(text.Title, text.Detail, text.Percent);

    private void HideSaveProgress()
    {
        HideOperationFooterProgress();
    }

    private bool ConfirmUnsupportedXlsxFeatureSave()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return true;

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport);

        var result = ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void ShowUnsupportedXlsxFeatureOpenWarningIfNeeded()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return;

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(_currentXlsxFeatureReport);
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
