using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class WatchWindowDialog : Window
{
    private readonly Func<IReadOnlyList<WatchWindowEntry>> _getEntries;
    private readonly Action? _addWatch;
    private readonly Func<string> _getSelectionText;
    private readonly Action<CellAddress> _navigateTo;
    private readonly Action<CellAddress> _removeWatch;
    private readonly ObservableCollection<WatchWindowRowPlan> _rows = [];
    private readonly ListView _listView;
    private readonly Button _deleteButton;

    // R88-app-formula-auditing-5-1: MainWindow's recalculation choke points (RecalculateWorkbook,
    // RecalculateIfAutomatic, RecalculateDirtyCells, RebuildDependenciesAndCalculate) all call
    // Refresh() so the Value/Formula columns update live after every recalculation, not only when
    // the user clicks Add/Refresh/Delete. But the getEntries callback supplied to this dialog's
    // constructor itself calls MainWindow's RecalculateWorkbook() to guarantee fresh values before
    // reading them -- which now re-enters this very method through that same choke point. Without
    // this guard the re-entrant call would re-clear and re-populate _rows out from under the
    // in-progress outer call, producing duplicated rows; with it, the nested call is a safe no-op
    // and the outer call finishes the refresh once, correctly.
    private bool _isRefreshing;

    public WatchWindowDialog(
        Func<IReadOnlyList<WatchWindowEntry>> getEntries,
        Action? addWatch,
        Func<string>? getSelectionText,
        Action<CellAddress> navigateTo,
        Action<CellAddress> removeWatch)
    {
        _getEntries = getEntries;
        _addWatch = addWatch;
        _getSelectionText = getSelectionText ?? (() => "");
        _navigateTo = navigateTo;
        _removeWatch = removeWatch;

        Title = UiText.Get(WatchWindowDialogPlanner.TitleKey);
        Width = WatchWindowDialogPlanner.Width;
        Height = WatchWindowDialogPlanner.Height;
        MinWidth = WatchWindowDialogPlanner.MinWidth;
        MinHeight = WatchWindowDialogPlanner.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var add = new Button
        {
            Content = UiText.Get("WatchWindow_AddWatch"),
            Width = 96,
            Height = 26,
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = _addWatch is not null,
            ToolTip = UiText.Get("WatchWindow_AddTheCurrentWorksheetSelectionToTheWatchWindow")
        };
        AutomationProperties.SetName(add, UiText.Get("WatchWindow_AddWatch2"));
        AutomationProperties.SetAutomationId(add, "WatchWindowAddButton");
        AutomationProperties.SetHelpText(add, UiText.Get("WatchWindow_AddTheCurrentWorksheetSelectionToTheWatchWindow"));
        add.Click += (_, _) =>
        {
            var dialog = new AddWatchDialog(_getSelectionText()) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _addWatch?.Invoke();
            Refresh();
        };
        buttons.Children.Add(add);

        var refresh = new Button { Content = UiText.Get("WatchWindow_Refresh"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(refresh, UiText.Get("WatchWindow_RefreshWatches"));
        AutomationProperties.SetAutomationId(refresh, "WatchWindowRefreshButton");
        AutomationProperties.SetHelpText(refresh, UiText.Get("WatchWindow_RefreshValuesAndFormulasForWatchedCells"));
        refresh.Click += (_, _) => Refresh();
        buttons.Children.Add(refresh);

        _deleteButton = new Button { Content = UiText.Get("WatchWindow_DeleteWatch"), Width = 96, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(_deleteButton, UiText.Get("WatchWindow_DeleteWatch2"));
        AutomationProperties.SetAutomationId(_deleteButton, "WatchWindowDeleteButton");
        AutomationProperties.SetHelpText(_deleteButton, UiText.Get("WatchWindow_DeleteTheSelectedWatchedCells"));
        _deleteButton.Click += (_, _) => DeleteSelectedWatch();
        buttons.Children.Add(_deleteButton);

        var close = new Button { Content = UiText.Get("WatchWindow_Close"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        AutomationProperties.SetName(close, UiText.Get("WatchWindow_CloseWatchWindow"));
        AutomationProperties.SetAutomationId(close, "WatchWindowCloseButton");
        AutomationProperties.SetHelpText(close, UiText.Get("WatchWindow_CloseTheWatchWindow"));
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var listPanel = new DockPanel();
        _listView = new ListView { ItemsSource = _rows, SelectionMode = SelectionMode.Extended };
        AutomationProperties.SetName(_listView, UiText.Get("WatchWindow_Watches2"));
        AutomationProperties.SetAutomationId(_listView, "WatchWindowList");
        AutomationProperties.SetHelpText(_listView, UiText.Get("WatchWindow_ListsWatchedCellsWithTheirWorkbookSheetAddressValueAndFormula"));
        var listLabel = new Label { Content = UiText.Get("WatchWindow_Watches"), Target = _listView, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(listLabel, Dock.Top);
        listPanel.Children.Add(listLabel);
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.SelectionChanged += (_, _) => UpdateDeleteButtonState();
        _listView.KeyDown += ListView_KeyDown;
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn { Header = UiText.Get("WatchWindow_Book"), Width = WatchWindowDialogPlanner.BookColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Book)) },
                new GridViewColumn { Header = UiText.Get("WatchWindow_Sheet"), Width = WatchWindowDialogPlanner.SheetColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Sheet)) },
                new GridViewColumn { Header = UiText.Get("WatchWindow_Name"), Width = WatchWindowDialogPlanner.NameColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Name)) },
                new GridViewColumn { Header = UiText.Get("WatchWindow_Cell"), Width = WatchWindowDialogPlanner.CellColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Cell)) },
                new GridViewColumn { Header = UiText.Get("WatchWindow_Value"), Width = WatchWindowDialogPlanner.ValueColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Value)) },
                new GridViewColumn { Header = UiText.Get("WatchWindow_Formula"), Width = WatchWindowDialogPlanner.FormulaColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRowPlan.Formula)) }
            }
        };
        listPanel.Children.Add(_listView);
        root.Children.Add(listPanel);

        Refresh();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public void Refresh()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            var selectedAddresses = _listView.SelectedItems
                .OfType<WatchWindowRowPlan>()
                .Select(row => row.Address)
                .ToHashSet();
            _rows.Clear();
            foreach (var row in WatchWindowDialogPlanner.CreateRows(
                         _getEntries(),
                         UiText.Get("WatchWindow_ThisWorkbook")))
                _rows.Add(row);
            RestoreSelection(selectedAddresses);
            UpdateDeleteButtonState();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RestoreSelection(IReadOnlySet<CellAddress> selectedAddresses)
    {
        if (selectedAddresses.Count == 0)
            return;

        foreach (var row in _rows.Where(row => selectedAddresses.Contains(row.Address)))
            _listView.SelectedItems.Add(row);
    }

    private void DeleteSelectedWatch()
    {
        var selectedIndex = _listView.SelectedIndex;
        var fallbackAddress = (_listView.SelectedItem as WatchWindowRowPlan)?.Address;
        var targets = WatchWindowService.GetDeleteTargets(
            _listView.SelectedItems.OfType<WatchWindowRowPlan>().Select(row => row.Address),
            fallbackAddress);
        if (targets.Count == 0)
            return;

        foreach (var address in targets)
            _removeWatch(address);

        Refresh();
        if (_rows.Count > 0)
            _listView.SelectedIndex = Math.Min(Math.Max(0, selectedIndex), _rows.Count - 1);
        UpdateDeleteButtonState();
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_listView.SelectedItem is WatchWindowRowPlan row)
        {
            _navigateTo(row.Address);
            e.Handled = true;
        }
    }

    private void ListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelectedWatch();
            e.Handled = true;
        }
    }

    private void UpdateDeleteButtonState()
    {
        _deleteButton.IsEnabled = _listView.SelectedItems.Count > 0;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_rows.Count > 0 && _listView.SelectedIndex < 0)
            _listView.SelectedIndex = 0;

        _listView.Focus();
        Keyboard.Focus(_listView);
    }
}
