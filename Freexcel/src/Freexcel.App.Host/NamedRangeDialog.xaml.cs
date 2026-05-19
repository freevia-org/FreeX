using System.Collections.ObjectModel;
using System.Windows;
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
        NamesList.ItemsSource = _items;
        RefreshList();

        if (initialRange.HasValue)
            RangeBox.Text = FormatRange(initialRange.Value, workbook);
    }

    // ── List management ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        _items.Clear();
        foreach (var (name, range) in _workbook.NamedRanges)
        {
            _items.Add(new NamedRangeViewModel(name, FormatRange(range, _workbook)));
        }
    }

    private static string FormatRange(GridRange range, Workbook wb)
    {
        var sheet = wb.GetSheet(range.Start.Sheet);
        var sheetName = sheet?.Name ?? "Sheet1";
        var start = range.Start.ToA1();
        var end = range.End.ToA1();
        return $"{sheetName}!{start}:{end}";
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void NamesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (NamesList.SelectedItem is NamedRangeViewModel vm)
        {
            NameBox.Text = vm.Name;
            RangeBox.Text = vm.Address;
        }
    }

    private void DefineButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var rangeText = RangeBox.Text.Trim();

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

        var cmd = new DefineNamedRangeCommand(name, range);
        var outcome = _commandBus.Execute(_workbook.Id, cmd);
        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Could not define named range.",
                "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshList();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            MessageBox.Show("Select a named range to delete.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
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

}

/// <summary>View model for a row in the named ranges list.</summary>
internal sealed class NamedRangeViewModel(string name, string address)
{
    public string Name { get; } = name;
    public string Address { get; } = address;
}
