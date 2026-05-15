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

        if (!TryParseRange(rangeText, out var range))
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

    // ── Range parsing ─────────────────────────────────────────────────────────

    private bool TryParseRange(string text, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Try Sheet!A1:B10 format
        var bangIdx = text.IndexOf('!');
        if (bangIdx > 0)
        {
            var sheetName = text[..bangIdx].Trim();
            var rangeAddr = text[(bangIdx + 1)..].Trim();
            var sheet = _workbook.GetSheet(sheetName);
            if (sheet == null)
            {
                // Fall back to first sheet
                if (_workbook.SheetCount == 0) return false;
                sheet = _workbook.GetSheetAt(0);
            }
            return TryParseRangeAddr(rangeAddr, sheet.Id, out range);
        }

        // No sheet qualifier — use first sheet
        if (_workbook.SheetCount == 0) return false;
        var defaultSheet = _workbook.GetSheetAt(0);
        return TryParseRangeAddr(text, defaultSheet.Id, out range);
    }

    private static bool TryParseRangeAddr(string addr, SheetId sheetId, out GridRange range)
    {
        range = default;
        var parts = addr.Split(':');
        if (parts.Length != 2) return false;

        if (!CellAddress.TryParse(parts[0].Trim(), sheetId, out var start)) return false;
        if (!CellAddress.TryParse(parts[1].Trim(), sheetId, out var end)) return false;

        range = new GridRange(start, end);
        return true;
    }
}

/// <summary>View model for a row in the named ranges list.</summary>
internal sealed class NamedRangeViewModel(string name, string address)
{
    public string Name { get; } = name;
    public string Address { get; } = address;
}
