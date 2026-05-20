using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var view in _workbook.CustomViews)
            _items.Add(new CustomViewViewModel(view.Name, view.Sheets.Count));

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
        if (ViewsList.SelectedItem is not CustomViewViewModel vm) return;
        var outcome = _commandBus.Execute(_workbook.Id, new ApplyCustomViewCommand(vm.Name));
        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Could not apply custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var outcome = _commandBus.Execute(_workbook.Id, new SaveCustomViewCommand(name));
        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Could not save custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshList();
        SelectView(name);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewsList.SelectedItem is not CustomViewViewModel vm) return;

        var outcome = _commandBus.Execute(_workbook.Id, new DeleteCustomViewCommand(vm.Name));
        if (!outcome.Success)
            MessageBox.Show(outcome.ErrorMessage ?? "Could not delete custom view.",
                "Custom Views", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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

internal sealed class CustomViewViewModel(string name, int sheetCount)
{
    public string Name { get; } = name;
    public int SheetCount { get; } = sheetCount;
}

public sealed record CustomViewNameDialogResult(string ViewName);

public sealed class CustomViewNameDialog : Window
{
    private readonly TextBox _nameBox = new();

    public CustomViewNameDialogResult Result { get; private set; }

    public CustomViewNameDialog(string defaultValue)
    {
        Result = CreateResult(defaultValue);
        Title = "Add View";
        Width = 320;
        Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new Label { Content = "_Name:", Target = _nameBox, Margin = new Thickness(0, 0, 0, 4) };
        _nameBox.Text = Result.ViewName;
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
        Grid.SetRow(buttons, 2);
        grid.Children.Add(label);
        grid.Children.Add(_nameBox);
        grid.Children.Add(buttons);
        Content = grid;

        Loaded += (_, _) => _nameBox.SelectAll();
    }

    public static CustomViewNameDialogResult CreateResult(string viewName) => new(viewName.Trim());

    private void Accept()
    {
        Result = CreateResult(_nameBox.Text);
        if (string.IsNullOrWhiteSpace(Result.ViewName))
            return;

        DialogResult = true;
    }
}
