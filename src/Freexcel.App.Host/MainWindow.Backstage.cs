using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Freexcel.Core.Commands;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
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

        var firstEnabledItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled);
        if (firstEnabledItem is null)
            return;

        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    private void OpenPrintBackstage()
    {
        ShowStartScreen();
        SsPrintNavBtn.Focus();
        Keyboard.Focus(SsPrintNavBtn);
    }

    private void ShowHomeView()
    {
        SsHomeView.Visibility = Visibility.Visible;
        SsInfoView.Visibility = Visibility.Collapsed;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtn");
    }

    private void ShowInfoView()
    {
        SsHomeView.Visibility = Visibility.Collapsed;
        SsInfoView.Visibility = Visibility.Visible;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        UpdateInfoView();
    }

    private void UpdateInfoView()
    {
        var plan = BackstageInfoPlanner.Build(_workbook, _currentFilePath);
        InfoWorkbookName.Text = plan.WorkbookName;
        InfoFilePath.Text = plan.FilePath;
        InfoSheetCount.Text = plan.SheetCount;
        InfoFormat.Text = plan.Format;
        InfoStatisticsSummary.Text = plan.StatisticsSummary;
        InfoAccessibilitySummary.Text = plan.AccessibilitySummary;
        InfoFormulaErrorSummary.Text = plan.FormulaErrorSummary;
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

        var activeBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x21, 0x73, 0x46));
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
        var wb = new Workbook("Book1");
        wb.AddSheet("Sheet1");
        _workbook = wb;
        _workbookRef.Current = wb;
        _currentSheetId = wb.Sheets[0].Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
        UpdateTitleBar();
        RecalculateWorkbook();
        SheetGrid.SelectedRange = null;
        _selectionAnchor = null;
        _selectionCursor = null;
        CellAddressBox.Text = "A1";
        FormulaBar.Text = "";
        RefreshSheetTabs();
        UpdateViewport();
    }

    private async Task OpenFileAsync(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLower();
        var adapter = FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext, out var format);
        if (adapter == null) return;
        if (_isOpeningFile) return;

        try
        {
            _isOpeningFile = true;
            ShowOpenProgress("Opening workbook", "Loading file (preparing)", 1);

            var progress = new Progress<OpenProgressUpdate>(
                update => ShowOpenProgress(update.Title, update.Detail, update.Percent));
            var loader = new OpenWorkbookLoader(workbook => _recalcEngine.RecalculateAllFormulas(workbook));
            var result = await loader.LoadAsync(path, adapter, ext, format!, progress);

            _currentXlsxFeatureReport = result.FeatureReport;
            _workbook = result.Workbook;
            _workbookRef.Current = result.Workbook;
            _workbook.Name = result.DisplayName;
            _currentSheetId = _workbook.Sheets[0].Id;
            _currentFilePath = result.OpenedAsTemplate ? null : path;
            UpdateTitleBar();

            _recentFiles.AddOrUpdate(path);
            ShowOpenProgress("Opening workbook", "Loading file (preparing view)", 98);
            ApplyOpenedWorksheetViewState();
            RefreshSheetTabs();
            HideStartScreen();
            ShowOpenProgress("Opening workbook", "Loading file (done)", 100);
            ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error",
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
        if (OpenProgressOverlay is null)
            return;

        OpenProgressTitle.Text = title;
        OpenProgressDetail.Text = detail;
        if (OpenProgressBar is not null)
        {
            OpenProgressBar.IsIndeterminate = !percent.HasValue;
            if (percent.HasValue)
                OpenProgressBar.Value = Math.Clamp(percent.Value, OpenProgressBar.Minimum, OpenProgressBar.Maximum);
        }
        OpenProgressOverlay.Visibility = Visibility.Visible;
        OpenProgressOverlay.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private void HideOpenProgress()
    {
        if (OpenProgressOverlay is not null)
            OpenProgressOverlay.Visibility = Visibility.Collapsed;
    }

    // Start screen button handlers
    private void SsBackBtn_Click(object sender, RoutedEventArgs e)       => HideStartScreen();
    private void SsNewBtn_Click(object sender, RoutedEventArgs e)        { CreateNewWorkbook(); HideStartScreen(); }
    private void SsBlankWorkbook_Click(object sender, RoutedEventArgs e) { CreateNewWorkbook(); HideStartScreen(); }
    private void SsOpenBtn_Click(object sender, RoutedEventArgs e)       => OpenButton_Click(sender, e);
    private void SsCloseBtn_Click(object sender, RoutedEventArgs e)      => Application.Current.Shutdown();
    private void SsHomeRibbonBtn_Click(object sender, RoutedEventArgs e) => ShowStartScreen();

    private void RibbonTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RibbonTabs.SelectedItem == FileTab)
        {
            // Switch back to Home immediately so the tab never stays selected
            RibbonTabs.SelectedIndex = 1;
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

    private void SsAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = DeferredCommandMessages.LocalAccountInfo();
        ShowOwnedMessage(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsHomeNavBtn_Click(object sender, RoutedEventArgs e)    => ShowHomeView();
    private void SsInfoBtn_Click(object sender, RoutedEventArgs e)       => ShowInfoView();

    private void SsMoreTemplatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = DeferredCommandMessages.OnlineTemplatesExcluded();
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowOptionsDialog();
    }

    private void ShowOptionsDialog()
    {
        var dlg = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes);
        if (ShowOwnedDialog(dlg) == true)
        {
            _options = dlg.Result;
            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
            ApplyOptionsWorksheetViewSettings();
            ApplyOptionsToView();
            UpdateViewport();
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
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };

        if (dialog.ShowDialog() == true)
            await OpenFileAsync(dialog.FileName);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
        {
            await SaveWorkbookToTargetAsync(target!);
            return;
        }

        await SaveWorkbookWithDialogAsync();
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) =>
        await SaveWorkbookWithDialogAsync();

    private async Task<bool> SaveWorkbookWithDialogAsync()
    {
        var filter = FileDialogFilterBuilder.BuildSaveFilter(_fileAdapters);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = _workbook.Name,
            DefaultExt = ".xlsx"
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

    private async Task<bool> SaveWorkbookToTargetAsync(FileSaveTarget target)
    {
        if (_isSavingFile)
            return false;

        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return false;

        try
        {
            _isSavingFile = true;
            ShowSaveProgress("Saving workbook", "Saving file (preparing)", 1);
            var progress = new Progress<SaveProgressUpdate>(
                update => ShowSaveProgress(update.Title, update.Detail, update.Percent));
            await new SaveWorkbookWriter().SaveAsync(target.Path, target.Adapter, _workbook, progress);
            _currentFilePath = target.Path;
            _recentFiles.AddOrUpdate(target.Path);
            UpdateTitleBar();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save file:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isSavingFile = false;
            HideSaveProgress();
        }
    }

    private void ShowSaveProgress(string title, string detail, double? percent = null)
    {
        if (StatusSaveProgressPanel is null)
            return;

        StatusSaveProgressText.Text = $"{title}: {detail}";
        if (StatusSaveProgressBar is not null)
        {
            StatusSaveProgressBar.IsIndeterminate = !percent.HasValue;
            if (percent.HasValue)
                StatusSaveProgressBar.Value = Math.Clamp(percent.Value, StatusSaveProgressBar.Minimum, StatusSaveProgressBar.Maximum);
        }
        StatusSaveProgressPanel.Visibility = Visibility.Visible;
    }

    private void HideSaveProgress()
    {
        if (StatusSaveProgressPanel is not null)
            StatusSaveProgressPanel.Visibility = Visibility.Collapsed;
    }

    private bool ConfirmUnsupportedXlsxFeatureSave()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return true;

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport);

        var result = MessageBox.Show(
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
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
