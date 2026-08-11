using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.CustomViews;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class CustomViewsDialog : Window
{
    private readonly Workbook _workbook;
    private readonly Func<IWorkbookCommand, CommandOutcome> _executeCommand;
    private readonly ObservableCollection<CustomViewsPlanner.DialogRow> _items = [];

    public bool ViewApplied { get; private set; }

    public CustomViewsDialog(Workbook workbook, Func<IWorkbookCommand, CommandOutcome> executeCommand)
    {
        _workbook = workbook;
        _executeCommand = executeCommand;
        InitializeComponent();
        ViewsList.ItemsSource = _items;
        RefreshList();
        UpdateButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var item in CustomViewsPlanner.BuildDialogRows(
                     _workbook,
                     UiText.Get("CustomViews_Included"),
                     UiText.Get("CustomViews_NotIncluded")))
            _items.Add(item);

        if (_items.Count > 0 && ViewsList.SelectedIndex < 0)
            ViewsList.SelectedIndex = 0;
        UpdateButtons();
    }

    private void ViewsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateButtons();

    private void ViewsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewsPlanner.DialogRow)
            return;

        ShowButton_Click(sender, e);
        e.Handled = true;
    }

    private void UpdateButtons()
    {
        var hasSelection = ViewsList?.SelectedItem is CustomViewsPlanner.DialogRow;
        if (ShowButton is not null)
            ShowButton.IsEnabled = hasSelection;
        if (DeleteButton is not null)
            DeleteButton.IsEnabled = hasSelection;
    }

    private void ShowButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewsPlanner.DialogRow vm) { FocusViewsList(); return; }
        var outcome = _executeCommand(CustomViewsPlanner.BuildApplyCommand(vm.Name));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("CustomViews_ApplyFailedMessage"), UiText.Get("CustomViews_CustomViews"));
            FocusViewsList();
            return;
        }

        ViewApplied = true;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewNameDialog(CustomViewsPlanner.SuggestDefaultName(
            _workbook.CustomViews.Count,
            UiText.Get("CustomViews_DefaultName"))) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.ViewName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var outcome = _executeCommand(CustomViewsPlanner.BuildSaveCommand(
            name,
            dialog.Result.IncludePrintSettings,
            dialog.Result.IncludeHiddenRowsColumnsAndFilterSettings));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("CustomViews_SaveFailedMessage"), UiText.Get("CustomViews_CustomViews"));
            FocusViewsList();
            return;
        }

        RefreshList();
        SelectView(name);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewsPlanner.DialogRow vm) { FocusViewsList(); return; }

        var outcome = _executeCommand(CustomViewsPlanner.BuildDeleteCommand(vm.Name));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get("CustomViews_DeleteFailedMessage"), UiText.Get("CustomViews_CustomViews"));
            FocusViewsList();
        }
        else
            RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void FocusInitialKeyboardTarget()
    {
        FocusViewsList();
    }

    private void FocusViewsList()
    {
        ViewsList.Focus();
        Keyboard.Focus(ViewsList);
    }

    private void SelectView(string name)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!string.Equals(_items[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            ViewsList.SelectedIndex = i;
            ViewsList.ScrollIntoView(_items[i]);
            break;
        }
    }
}
