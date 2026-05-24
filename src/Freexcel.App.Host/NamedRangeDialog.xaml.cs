using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly Action<NamedRangeSelectionRequest>? _requestRangeSelection;
    private readonly ObservableCollection<NamedRangeViewModel> _items = [];
    private readonly string _initialRefersTo;

    public NamedRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    /// <param name="workbook">The active workbook.</param>
    /// <param name="commandBus">Command bus for dispatching define/delete commands.</param>
    /// <param name="initialRange">
    ///   Optional initial range (e.g. the current selection). If provided, pre-fills
    ///   the Range text box in Sheet!A1:B10 notation.
    /// </param>
    public NamedRangeDialog(
        Workbook workbook,
        ICommandBus commandBus,
        GridRange? initialRange = null,
        Action<NamedRangeSelectionRequest>? requestRangeSelection = null)
    {
        _workbook = workbook;
        _commandBus = commandBus;
        _requestRangeSelection = requestRangeSelection;
        InitializeComponent();
        RefreshList();
        UpdateSelectionCommands();

        _initialRefersTo = initialRange.HasValue ? FormatRange(initialRange.Value, workbook) : "";
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
        if (NamesList is null)
            return;

        var selected = FilterBox.SelectedIndex switch
        {
            1 => NamedRangeFilterOption.Workbook,
            2 => NamedRangeFilterOption.Worksheet,
            3 => NamedRangeFilterOption.Errors,
            4 => NamedRangeFilterOption.NoErrors,
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
        RangeSelectionRequest = CreateRangeSelectionRequest(
            NamedRangeSelectionTarget.SelectedNameRefersTo,
            RefersToBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        RefersToBox.Focus();
        RefersToBox.SelectAll();
        Keyboard.Focus(RefersToBox);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult("", "Workbook", "", _initialRefersTo),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _)) { Owner = this };
        if (dialog.ShowDialog() == true)
            DefineOrUpdateName(dialog.Result);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamesList.SelectedItem is not NamedRangeViewModel vm)
        {
            MessageBox.Show("Select a named range to edit.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusNamesListOrNewButton();
            return;
        }

        var dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult(vm.Name, vm.Scope, vm.Comment, vm.RefersTo),
            GetScopeOptions(),
            RequestRangeSelection,
            isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _))
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
            FocusNamesListOrNewButton();
            return;
        }

        if (!NamedRangeInputParser.TryParseRange(_workbook, rangeText, out var range))
        {
            MessageBox.Show(
                "Invalid range format. Use: SheetName!A1:B10 or A1:B10",
                "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusRefersToSummary();
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
            FocusNamesListOrNewButton();
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
            FocusNamesListOrNewButton();
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
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Could not delete.", "Named Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusNamesListOrNewButton();
        }
        else
            RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void FocusInitialKeyboardTarget()
    {
        FocusNamesListOrNewButton();
    }

    private void FocusNamesListOrNewButton()
    {
        if (NamesList.Items.Count > 0)
        {
            NamesList.Focus();
            Keyboard.Focus(NamesList);
            return;
        }

        NewButton.Focus();
        Keyboard.Focus(NewButton);
    }

    private void FocusRefersToSummary()
    {
        RefersToBox.Focus();
        RefersToBox.SelectAll();
        Keyboard.Focus(RefersToBox);
    }

    private IReadOnlyList<string> GetScopeOptions() =>
        new[] { "Workbook" }
            .Concat(_workbook.Sheets.Select(sheet => sheet.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static NamedRangeSelectionRequest CreateRangeSelectionRequest(
        NamedRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private void RequestRangeSelection(NamedRangeSelectionRequest request)
    {
        RangeSelectionRequest = request;
        _requestRangeSelection?.Invoke(request);
    }
}

public enum NamedRangeSelectionTarget
{
    SelectedNameRefersTo,
    DefinitionRefersTo
}

public sealed record NamedRangeSelectionRequest(
    NamedRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

