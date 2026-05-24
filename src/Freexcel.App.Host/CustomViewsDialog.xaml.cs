using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
        foreach (var view in _workbook.CustomViews)
            _items.Add(new CustomViewViewModel(
                view.Name,
                view.Sheets.Count,
                view.IncludePrintSettings ? "Included" : "Not included",
                view.IncludeHiddenRowsColumnsAndFilterSettings ? "Included" : "Not included"));

        if (_items.Count > 0 && ViewsList.SelectedIndex < 0)
            ViewsList.SelectedIndex = 0;
        UpdateButtons();
    }

    private void ViewsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateButtons();

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
            MessageBox.Show(outcome.ErrorMessage ?? "Could not apply custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusViewsList();
            return;
        }

        ViewApplied = true;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewNameDialog($"Custom View {_workbook.CustomViews.Count + 1}") { Owner = this };
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
            MessageBox.Show(outcome.ErrorMessage ?? "Could not save custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(outcome.ErrorMessage ?? "Could not delete custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
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

internal sealed class CustomViewViewModel(string name, int sheetCount, string printSettingsIndicator, string filterSettingsIndicator)
{
    public string Name { get; } = name;
    public int SheetCount { get; } = sheetCount;
    public string PrintSettingsIndicator { get; } = printSettingsIndicator;
    public string FilterSettingsIndicator { get; } = filterSettingsIndicator;
}

public sealed record CustomViewNameDialogResult(
    string ViewName,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true);

public sealed class CustomViewNameDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly CheckBox _printSettingsBox = new() { Content = "_Print settings", IsChecked = true };
    private readonly CheckBox _hiddenFilterSettingsBox = new() { Content = "_Hidden rows, columns and filter settings", IsChecked = true };

    public CustomViewNameDialogResult Result { get; private set; }

    public CustomViewNameDialog(string defaultValue)
    {
        Result = CreateResult(defaultValue);
        Title = "Add View";
        Width = 320;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new Label { Content = "_Name:", Target = _nameBox, Margin = new Thickness(0, 0, 0, 4) };
        _nameBox.Text = Result.ViewName;
        _printSettingsBox.Margin = new Thickness(0, 8, 0, 4);
        _hiddenFilterSettingsBox.Margin = new Thickness(0, 0, 0, 4);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 72, IsCancel = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Grid.SetRow(label, 0);
        Grid.SetRow(_nameBox, 1);
        Grid.SetRow(_printSettingsBox, 2);
        Grid.SetRow(_hiddenFilterSettingsBox, 3);
        Grid.SetRow(buttons, 4);
        grid.Children.Add(label);
        grid.Children.Add(_nameBox);
        grid.Children.Add(_printSettingsBox);
        grid.Children.Add(_hiddenFilterSettingsBox);
        grid.Children.Add(buttons);
        Content = grid;

        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static CustomViewNameDialogResult CreateResult(
        string viewName,
        bool includePrintSettings = true,
        bool includeHiddenRowsColumnsAndFilterSettings = true) =>
        new(
            viewName.Trim(),
            includePrintSettings,
            includeHiddenRowsColumnsAndFilterSettings);

    private void Accept()
    {
        Result = CreateResult(
            _nameBox.Text,
            _printSettingsBox.IsChecked == true,
            _hiddenFilterSettingsBox.IsChecked == true);
        if (string.IsNullOrWhiteSpace(Result.ViewName))
        {
            MessageBox.Show(this, "Enter a view name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusNameInput();
            return;
        }

        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusNameInput();
    }

    private void FocusNameInput()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }
}
