using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class CustomViewsDialog : Window
{
    private readonly Workbook _workbook;
    private readonly ICommandBus _commandBus;
    private readonly ObservableCollection<CustomViewViewModel> _items = [];

    public bool ViewApplied { get; private set; }

    public CustomViewsDialog(Workbook workbook, ICommandBus commandBus)
    {
        _workbook = workbook;
        _commandBus = commandBus;
        InitializeComponent();
        ViewsList.ItemsSource = _items;
        RefreshList();
        UpdateButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var item in CustomViewsDialogPlanner.BuildItems(_workbook))
            _items.Add(item);

        if (_items.Count > 0 && ViewsList.SelectedIndex < 0)
            ViewsList.SelectedIndex = 0;
        UpdateButtons();
    }

    private void ViewsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateButtons();

    private void ViewsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ShowButton_Click(sender, e);

    private void UpdateButtons()
    {
        var hasSelection = ViewsList?.SelectedItem is CustomViewViewModel;
        if (ShowButton is not null)
            ShowButton.IsEnabled = hasSelection;
        if (DeleteButton is not null)
            DeleteButton.IsEnabled = hasSelection;
    }

    private void ShowButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewViewModel vm) { FocusViewsList(); return; }
        var outcome = _commandBus.Execute(_workbook.Id, new ApplyCustomViewCommand(vm.Name));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? "Could not apply custom view.", "Custom Views");
            FocusViewsList();
            return;
        }

        ViewApplied = true;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewNameDialog(CustomViewsDialogPlanner.CreateDefaultViewName(_workbook.CustomViews.Count)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.ViewName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var outcome = _commandBus.Execute(_workbook.Id, new SaveCustomViewCommand(
            name,
            dialog.Result.IncludePrintSettings,
            dialog.Result.IncludeHiddenRowsColumnsAndFilterSettings));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? "Could not save custom view.", "Custom Views");
            FocusViewsList();
            return;
        }

        RefreshList();
        SelectView(name);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewViewModel vm) { FocusViewsList(); return; }

        var outcome = _commandBus.Execute(_workbook.Id, new DeleteCustomViewCommand(vm.Name));
        if (!outcome.Success)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? "Could not delete custom view.", "Custom Views");
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
