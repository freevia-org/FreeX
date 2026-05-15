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
        var name = PromptForViewName($"Custom View {_workbook.CustomViews.Count + 1}");
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

    private string? PromptForViewName(string defaultValue)
    {
        var dialog = new Window
        {
            Title = "Add View",
            Owner = this,
            Width = 320,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) };
        var textBox = new System.Windows.Controls.TextBox { Text = defaultValue };
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Grid.SetRow(label, 0);
        Grid.SetRow(textBox, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(label);
        grid.Children.Add(textBox);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        textBox.SelectAll();
        return dialog.ShowDialog() == true ? textBox.Text.Trim() : null;
    }
}

internal sealed class CustomViewViewModel(string name, int sheetCount)
{
    public string Name { get; } = name;
    public int SheetCount { get; } = sheetCount;
}
