using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// Named Range Manager dialog.
/// Allows the user to define, view, and delete named ranges in the workbook.
/// </summary>
public sealed partial class NamedRangeDialog : Window
{
    private readonly Workbook _workbook;
    private readonly ICommandBus _commandBus;
    private readonly ObservableCollection<NamedRangeViewModel> _items = [];
    private readonly string _initialRefersTo;

    /// <param name="workbook">The active workbook.</param>
    /// <param name="commandBus">Command bus for dispatching define/delete commands.</param>
    /// <param name="initialRange">
    ///   Optional initial range (e.g. the current selection). If provided, pre-fills
    ///   the Range text box in Sheet!A1:B10 notation.
    /// </param>
    public NamedRangeDialog(Workbook workbook, ICommandBus commandBus, GridRange? initialRange = null)
    {
        _workbook = workbook;
        _commandBus = commandBus;
        InitializeComponent();
        RefreshList();
        UpdateSelectionCommands();

        _initialRefersTo = initialRange.HasValue ? FormatRange(initialRange.Value, workbook) : "";
    }

    // ── List management ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        _items.Clear();
        foreach (var (name, range) in _workbook.NamedRanges)
        {
            var metadata = _workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
                ? savedMetadata
                : NamedRangeMetadata.WorkbookScope;
            _items.Add(new NamedRangeViewModel(
                name,
                FormatValue(range, _workbook),
                FormatRange(range, _workbook),
                metadata.Scope,
                metadata.Comment));
        }

        ApplyFilter();
    }

    private static string FormatRange(GridRange range, Workbook wb)
    {
        var sheet = wb.GetSheet(range.Start.Sheet);
        var sheetName = sheet?.Name ?? "Sheet1";
        var start = range.Start.ToA1();
        var end = range.End.ToA1();
        return $"{sheetName}!{start}:{end}";
    }

    private static string FormatValue(GridRange range, Workbook wb) =>
        FormatRange(range, wb);

    // ── Event handlers ────────────────────────────────────────────────────────

    private void NamesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (NamesList.SelectedItem is NamedRangeViewModel vm)
        {
            RefersToBox.Text = vm.RefersTo;
        }

        UpdateSelectionCommands();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var selected = FilterBox.SelectedIndex switch
        {
            1 => NamedRangeFilterOption.Workbook,
            2 => NamedRangeFilterOption.Worksheet,
            _ => NamedRangeFilterOption.All
        };

        NamesList.ItemsSource = NamedRangeDialogPlanner.FilterItems(_items, selected).ToList();
        if (NamesList.SelectedItem is not NamedRangeViewModel)
        {
            RefersToBox.Clear();
            UpdateSelectionCommands();
        }
    }

    private void UpdateSelectionCommands()
    {
        var hasSelection = NamesList.SelectedItem is NamedRangeViewModel;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        RefersToPickerButton.IsEnabled = hasSelection;
    }

    private void RefersToPickerButton_Click(object sender, RoutedEventArgs e)
    {
        RefersToBox.Focus();
        RefersToBox.SelectAll();
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult("", "Workbook", "", _initialRefersTo),
            GetScopeOptions()) { Owner = this };
        if (dialog.ShowDialog() == true)
            DefineOrUpdateName(dialog.Result);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            MessageBox.Show("Select a named range to edit.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult(vm.Name, vm.Scope, vm.Comment, vm.RefersTo),
            GetScopeOptions())
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
            DefineOrUpdateName(dialog.Result);
    }

    private void DefineOrUpdateName(NameDefinitionDialogResult definition)
    {
        var name = definition.Name.Trim();
        var rangeText = definition.RefersTo.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a name.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!NamedRangeInputParser.TryParseRange(_workbook, rangeText, out var range))
        {
            MessageBox.Show(
                "Invalid range format. Use: SheetName!A1:B10 or A1:B10",
                "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cmd = new DefineNamedRangeCommand(
            name,
            range,
            new NamedRangeMetadata(definition.Scope.Trim(), definition.Comment.Trim()));
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Could not define named range.",
                "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshList();
        if (_items.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) is { } updated)
        {
            ApplyFilter();
            NamesList.SelectedItem = updated;
            RefersToBox.Text = updated.RefersTo;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            MessageBox.Show("Select a named range to delete.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Delete the name '{vm.Name}'?",
                "Name Manager",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var cmd = new RemoveNamedRangeCommand(vm.Name);
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
            MessageBox.Show(outcome.ErrorMessage ?? "Could not delete.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private IReadOnlyList<string> GetScopeOptions() =>
        new[] { "Workbook" }
            .Concat(_workbook.Sheets.Select(sheet => sheet.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public enum NamedRangeFilterOption
{
    All,
    Workbook,
    Worksheet
}

public static class NamedRangeDialogPlanner
{
    public static IReadOnlyList<NamedRangeViewModel> FilterItems(
        IEnumerable<NamedRangeViewModel> items,
        NamedRangeFilterOption filter) =>
        filter switch
        {
            NamedRangeFilterOption.Workbook => items
                .Where(item => string.Equals(item.Scope, "Workbook", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            NamedRangeFilterOption.Worksheet => items
                .Where(item => !string.Equals(item.Scope, "Workbook", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            _ => items.ToList()
        };
}

public sealed record NameDefinitionDialogResult(string Name, string Scope, string Comment, string RefersTo);

internal sealed class NameDefinitionDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _scopeBox = new();
    private readonly TextBox _commentBox = new();
    private readonly TextBox _refersToBox = new();
    private readonly Button _rangePickerButton = new() { Content = "...", Width = 26 };
    private readonly IReadOnlyList<string> _scopeOptions;

    public NameDefinitionDialogResult Result { get; private set; }

    public NameDefinitionDialog(NameDefinitionDialogResult initial, IReadOnlyList<string> scopeOptions)
    {
        Result = initial;
        _scopeOptions = scopeOptions.Count > 0 ? scopeOptions : ["Workbook"];
        Title = string.IsNullOrWhiteSpace(initial.Name) ? "New Name" : "Edit Name";
        Width = 460;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _nameBox.Text = initial.Name;
        foreach (var scope in _scopeOptions)
            _scopeBox.Items.Add(scope);
        _scopeBox.SelectedItem = _scopeOptions.FirstOrDefault(scope =>
            string.Equals(scope, initial.Scope, StringComparison.OrdinalIgnoreCase)) ?? _scopeOptions[0];
        _commentBox.Text = initial.Comment;
        _refersToBox.Text = initial.RefersTo;
        _rangePickerButton.ToolTip = "Select the referenced range from the worksheet";
        _rangePickerButton.Click += (_, _) =>
        {
            _refersToBox.Focus();
            _refersToBox.SelectAll();
        };

        Content = CreateContent();
    }

    private Grid CreateContent()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var row = 0; row < 5; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTextRow(grid, 0, "_Name:", _nameBox);
        AddComboRow(grid, 1, "_Scope:", _scopeBox);
        AddTextRow(grid, 2, "_Comment:", _commentBox);
        AddRefersToRow(grid, 3);

        var buttons = DialogButtonRowFactory.Create(Accept, 72);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 3);
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private static void AddComboRow(Grid grid, int row, string label, ComboBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private void AddRefersToRow(Grid grid, int row)
    {
        grid.Children.Add(new Label { Content = "_Refers to:", Target = _refersToBox, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        _refersToBox.Margin = new Thickness(0, 0, 4, 8);
        grid.Children.Add(_refersToBox);
        Grid.SetRow(_refersToBox, row);
        Grid.SetColumn(_refersToBox, 1);
        _rangePickerButton.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(_rangePickerButton);
        Grid.SetRow(_rangePickerButton, row);
        Grid.SetColumn(_rangePickerButton, 2);
    }

    private void Accept()
    {
        Result = new NameDefinitionDialogResult(
            _nameBox.Text.Trim(),
            (_scopeBox.SelectedItem as string)?.Trim() ?? "Workbook",
            _commentBox.Text.Trim(),
            _refersToBox.Text.Trim());
        DialogResult = true;
    }
}

/// <summary>View model for a row in the named ranges list.</summary>
public sealed class NamedRangeViewModel(string name, string value, string refersTo, string scope, string comment)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string RefersTo { get; } = refersTo;
    public string Scope { get; } = scope;
    public string Comment { get; } = comment;

    public string Address => RefersTo;
}
