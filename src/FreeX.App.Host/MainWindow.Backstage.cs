using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private PrintPreviewSettings _backstagePrintPreviewSettings = new();
    private FixedDocument? _backstagePrintPreviewDocument;

    private void ShowStartScreen()
    {
        UpdateSsGreeting();
        SwitchToRecentTab();
        UpdateSsRecentList();
        ShowHomeView();
        StartScreenOverlay.Visibility = Visibility.Visible;
        FocusBackstageHomeNavigation();
    }

    private void HideStartScreen()
    {
        StartScreenOverlay.Visibility = Visibility.Collapsed;
        SheetGrid.Focus();
    }

    private void FocusBackstageHomeNavigation()
    {
        SsHomeNavBtn.Focus();
        Keyboard.Focus(SsHomeNavBtn);
    }

    private void ConfigureBackstageInfoActionButtons()
    {
        InfoProtectWorkbookButton.Click += InfoProtectWorkbookBtn_Click;
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            InfoProtectWorkbookButton,
            "BackstageInfoProtectWorkbookButton");
        RibbonTooltip.SetKeyTip(InfoProtectWorkbookButton, "PW");
        RefreshBackstageInfoProtectionButton();

        ConfigureBackstageInfoActionButton(
            InfoCheckAccessibilityButton,
            UiText.Get("MainWindow_Text_CheckAccessibility"),
            UiText.Get("MainWindow_AutomationHelpText_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_AD813E90"),
            UiText.Get("MainWindow_TooltipTitle_CheckAccessibility"),
            UiText.Get("MainWindow_TooltipDescription_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_4FECDB20"),
            "BackstageInfoCheckAccessibilityButton",
            "CA",
            InfoAccessibilityCheckerBtn_Click);

        ConfigureBackstageInfoActionButton(
            InfoWorkbookStatisticsButton,
            UiText.Get("MainWindow_Content_WorkbookStatistics"),
            UiText.Get("MainWindow_AutomationHelpText_ShowWorkbookCountsForSheetsCellsFormulasCommentsAndObjects"),
            UiText.Get("MainWindow_TooltipTitle_WorkbookStatistics"),
            UiText.Get("MainWindow_TooltipDescription_ShowWorkbookCountsForSheetsCellsFormulasCommentsAndObjects"),
            "BackstageInfoWorkbookStatisticsButton",
            "W",
            InfoWorkbookStatisticsBtn_Click);

        ConfigureBackstageInfoActionButton(
            InfoErrorCheckingButton,
            UiText.Get("MainWindow_Content_ErrorChecking"),
            UiText.Get("MainWindow_TooltipDescription_CheckForCommonErrorsInTheFormulasOnThisSheetOrOpenErrorCheckingOptions"),
            UiText.Get("MainWindow_TooltipTitle_ErrorChecking"),
            UiText.Get("MainWindow_TooltipDescription_CheckForCommonErrorsInTheFormulasOnThisSheetOrOpenErrorCheckingOptions"),
            "BackstageInfoErrorCheckingButton",
            "EC",
            InfoErrorCheckingBtn_Click);
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

    private bool TryHandleBackstageShellFocusCycle(bool reverse)
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
            !IsInsideStartScreenOverlay(focusedElement))
        {
            FocusBackstageHomeNavigation();
            return true;
        }

        var direction = reverse
            ? FocusNavigationDirection.Previous
            : FocusNavigationDirection.Next;

        if (StartScreenOverlay.MoveFocus(new TraversalRequest(direction)))
            return true;

        FocusBackstageHomeNavigation();
        return true;
    }

    private void StartScreenOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is not UIElement focusedElement ||
            !IsDescendantOf(focusedElement, StartScreenSidebar) ||
            e.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
        {
            return;
        }

        var direction = e.Key switch
        {
            Key.Up => FocusNavigationDirection.Previous,
            Key.Down => FocusNavigationDirection.Next,
            Key.Home => FocusNavigationDirection.First,
            Key.End => FocusNavigationDirection.Last,
            _ => FocusNavigationDirection.Next
        };
        focusedElement.MoveFocus(new TraversalRequest(direction));
        e.Handled = true;
    }

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

    private void ShowHomeView()
    {
        SsHomeView.Visibility = Visibility.Visible;
        SsInfoView.Visibility = Visibility.Collapsed;
        SsPrintView.Visibility = Visibility.Collapsed;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsPrintNavBtn.Style = (Style)FindResource("SsNavBtn");
    }

    private void ShowInfoView()
    {
        SsHomeView.Visibility = Visibility.Collapsed;
        SsInfoView.Visibility = Visibility.Visible;
        SsPrintView.Visibility = Visibility.Collapsed;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        SsPrintNavBtn.Style = (Style)FindResource("SsNavBtn");
        UpdateInfoView();
    }

    private void ShowPrintView()
    {
        SsHomeView.Visibility = Visibility.Collapsed;
        SsInfoView.Visibility = Visibility.Collapsed;
        SsPrintView.Visibility = Visibility.Visible;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsPrintNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        var activeSheet = _workbook.GetSheet(_currentSheetId);
        _backstagePrintPreviewSettings = new PrintPreviewSettings();
        ConfigureBackstagePrintOptions(activeSheet);
        RefreshBackstagePrintPreview();
        SsBackstagePrintNowButton.Focus();
        Keyboard.Focus(SsBackstagePrintNowButton);
    }

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
        var plan = BackstageInfoPlanner.Build(
            _workbook,
            _currentFilePath,
            activeSheet,
            hasSelection: SheetGrid.SelectedRange is not null);
        InfoWorkbookName.Text = plan.WorkbookName;
        InfoFilePath.Text = plan.FilePath;
        InfoSheetCount.Text = plan.SheetCount;
        InfoFormat.Text = plan.Format;
        InfoFileSize.Text = plan.FileSize;
        InfoLastModified.Text = plan.LastModified;
        InfoShareStatus.Text = plan.SharingStatus;
        InfoExportStatus.Text = plan.ExportStatus;
        InfoWorkbookProtectionSummary.Text = plan.Summary.WorkbookProtectionSummary;
        InfoActiveSheetProtectionSummary.Text = plan.Summary.ActiveSheetProtectionSummary;
        InfoStatisticsSummary.Text = plan.StatisticsSummary;
        InfoAccessibilitySummary.Text = plan.AccessibilitySummary;
        InfoFormulaErrorSummary.Text = plan.FormulaErrorSummary;
        RefreshBackstageInfoProtectionButton();
    }

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
        var plan = BackstageRecentFileListPlanner.Build(
            _recentFiles.Entries,
            filter,
            System.IO.File.Exists);
        _allRecentItems = plan.AllItems.ToList();
        SsRecentList.ItemsSource = plan.RecentItems;
        SsPinnedList.ItemsSource = plan.PinnedItems;
    }

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

        var activeBrush = (System.Windows.Media.Brush)FindResource("FreeXAccentBrush");
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

    private void CreateNewWorkbook()
    {
        CloseFindReplaceDialogIfOpen();
        var wb = NewWorkbookFactory.Create(_options);
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
        // Notify siblings so they rebind to the new workbook.
        NotifyOtherWindowsOfWorkbookChange();
        RecordDiagnosticEvent("workbook_new");
    }

    private async Task RequestNewWorkbookAsync()
    {
        if (await ConfirmSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeCreatingWorkbook")) == SaveChangesConfirmation.Cancel)
            return;

        CreateNewWorkbook();
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
        var ext = System.IO.Path.GetExtension(path).ToLower();
        var adapter = FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext, out var format);
        if (adapter == null) return;
        if (_isOpeningFile) return;
        _isOpeningFile = true;
        try
        {
            if (await ConfirmSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeOpeningWorkbook")) == SaveChangesConfirmation.Cancel)
                return;

            _operationProgressFileName = System.IO.Path.GetFileName(path);
            ShowOpenProgress(
                OpenWorkbookProgressPlanner.ProgressTitle(),
                OpenWorkbookProgressPlanner.FormatLoadingFileDetail("preparing", TimeSpan.Zero),
                1);

            var progress = new Progress<OpenProgressUpdate>(
                update => ShowOpenProgress(update.Title, update.Detail, update.Percent));
            var loader = new OpenWorkbookLoader(workbook => _recalcEngine.RecalculateAllFormulas(workbook));
            var result = await loader.LoadAsync(path, adapter, ext, format!, progress);

            CloseFindReplaceDialogIfOpen();
            _currentXlsxFeatureReport = result.FeatureReport;
            _workbook = result.Workbook;
            _workbookRef.Current = result.Workbook;
            InvalidateToolbarVisualState();
            _workbook.Name = result.DisplayName;
            _worksheetSelections.Clear();
            _currentSheetId = _workbook.Sheets[0].Id;
            InvalidateNavigationCaches();
            _currentFilePath = result.OpenedAsTemplate ? null : path;
            UpdateTitleBar();
            MarkWorkbookSaved();
            // Notify sibling windows so they rebind their viewports to the new workbook.
            // Without this, siblings keep a stale _workbook while the shared command bus
            // resolves the new one — their mutations would target the wrong workbook.
            NotifyOtherWindowsOfWorkbookChange();

            if (!suppressRecentFiles)
                _recentFiles.AddOrUpdate(path);
            ShowOpenProgress(
                OpenWorkbookProgressPlanner.ProgressTitle(),
                OpenWorkbookProgressPlanner.FormatLoadingFileDetail("preparing view", TimeSpan.Zero),
                98);
            ApplyOpenedWorksheetViewState();
            RefreshSheetTabs();
            HideStartScreen();
            ShowOpenProgress(
                OpenWorkbookProgressPlanner.ProgressTitle(),
                OpenWorkbookProgressPlanner.FormatLoadingFileDetail("done", TimeSpan.Zero),
                100);
            ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();
            ShowXlsxLoadWarningsIfNeeded(result.LoadWarnings);
            RecordDiagnosticEvent("workbook_opened", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = format?.FormatName,
                ["worksheetCount"] = _workbook.Sheets.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("workbook_open_failed", new Dictionary<string, string?>
            {
                ["extension"] = ext,
                ["fileType"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext),
                ["format"] = format?.FormatName,
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

    public static string FormatLoadingFileDetail(string phase, TimeSpan elapsed)
        => OpenWorkbookProgressPlanner.FormatLoadingFileDetail(phase, elapsed);

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
    }

    private void HideOperationFooterProgress()
    {
        BackstageProgressOverlayBinder.Hide(StatusSaveProgressPanel);
        _operationProgressFileName = null;
    }

    // Start screen button handlers
    private void SsBackBtn_Click(object sender, RoutedEventArgs e)       => HideStartScreen();
    private async void SsNewBtn_Click(object sender, RoutedEventArgs e)        => await RequestNewWorkbookAsync();
    private async void SsBlankWorkbook_Click(object sender, RoutedEventArgs e) => await RequestNewWorkbookAsync();
    private void SsOpenBtn_Click(object sender, RoutedEventArgs e)       => OpenButton_Click(sender, e);
    private void SsCloseBtn_Click(object sender, RoutedEventArgs e)      => Close();
    private void SsHomeRibbonBtn_Click(object sender, RoutedEventArgs e) => ShowStartScreen();

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
    private async void SsShareBtn_Click(object sender, RoutedEventArgs e)
    {
        await ShareWorkbookAsync();
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

    private void SsHomeNavBtn_Click(object sender, RoutedEventArgs e)    => ShowHomeView();
    private void SsInfoBtn_Click(object sender, RoutedEventArgs e)       => ShowInfoView();
    private void SsPrintNavBtn_Click(object sender, RoutedEventArgs e)   => ShowPrintView();

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
        var dlg = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes, initialSection);
        if (ShowOwnedDialog(dlg) == true)
        {
            _options = dlg.Result;
            var appLanguageChanged = !StringComparer.OrdinalIgnoreCase.Equals(
                previousAppLanguage,
                AppLanguageCatalog.NormalizeCultureName(_options.AppLanguage));
            if (appLanguageChanged)
                AppLocalization.ApplyAppLanguage(_options.AppLanguage);

            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
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
            _recentFiles.Pin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsUnpinItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            _recentFiles.Unpin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsRemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            _recentFiles.Remove(vm.Path);
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
        var filter = FileDialogFilterBuilder.BuildOpenFilter(_fileAdapters);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            await OpenFileAsync(dialog.FileName);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        bool saved;
        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
        {
            saved = await SaveWorkbookToTargetAsync(target!);
        }
        else
        {
            saved = await SaveWorkbookWithDialogAsync();
        }

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
        var filter = FileDialogFilterBuilder.BuildSaveFilter(_fileAdapters);
        var defaultExt = ResolveSaveDialogDefaultExtension();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = _workbook.Name,
            DefaultExt = defaultExt,
            FilterIndex = FileDialogFilterBuilder.FindSaveFilterIndex(_fileAdapters, defaultExt),
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
            var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ext, out _);
            if (adapter == null)
                return false;

            return await SaveWorkbookToTargetAsync(new FileSaveTarget(dialog.FileName, adapter));
        }

        return false;
    }

    private string ResolveSaveDialogDefaultExtension()
    {
        var preferredExtension = FreeXOptions.NormalizeDefaultFormat(_options.DefaultFormat);
        return FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, preferredExtension, out _) is null
            ? FreeXOptions.XlsxDefaultFormat
            : preferredExtension;
    }

    private async Task<bool> SaveWorkbookToTargetAsync(FileSaveTarget target)
    {
        if (_isSavingFile)
            return false;

        if (FileSavePlanner.CanSkipCleanSave(_workbookDirty, _currentFilePath, target))
            return true;

        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return false;

        // Capture identity/generation before any await so we can detect edits or
        // workbook replacement that occur while the serialization is running.
        var generationAtSaveStart = _workbookDirtyGeneration;
        var workbookAtSaveStart = _workbook;

        try
        {
            _isSavingFile = true;

            // Block all user input for the duration of the save.  Unlike open (which builds a fresh
            // workbook), save serializes the LIVE model on a background thread, so a concurrent edit —
            // including a keyboard edit, which a mouse-only overlay would not stop — could tear the
            // snapshot.  Disabling the root grid is the primary defence; the generation check below is
            // belt-and-suspenders.  The footer progress still advances while disabled.
            RootGrid.IsEnabled = false;
            _operationProgressFileName = System.IO.Path.GetFileName(target.Path);
            ShowSaveProgress(
                UiText.Get("Progress_SavingWorkbook"),
                UiText.Get("Progress_SavingFilePreparing"),
                1);
            var progress = new Progress<SaveProgressUpdate>(
                update => ShowSaveProgress(update.Title, update.Detail, update.Percent));
            var saveWarnings = await new SaveWorkbookWriter().SaveAsync(target.Path, target.Adapter, _workbook, progress);

            var plan = SaveCompletionPlanner.Plan(
                generationAtSaveStart,
                _workbookDirtyGeneration,
                sameWorkbook: ReferenceEquals(_workbook, workbookAtSaveStart));

            if (plan.ApplyFileContext)
            {
                _currentFilePath = target.Path;
                _workbook.Name = WorkbookTitleFormatter.DisplayNameFromPath(target.Path);
                _recentFiles.AddOrUpdate(target.Path);
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
            _isSavingFile = false;
            RootGrid.IsEnabled = true;
            HideSaveProgress();
        }
    }

    private void ShowSaveProgress(string title, string detail, double? percent = null)
    {
        ShowOperationFooterProgress(title, detail, percent);
    }

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
