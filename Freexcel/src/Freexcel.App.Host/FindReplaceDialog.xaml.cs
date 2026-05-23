using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class FindReplaceDialog : Window
{
    private readonly Func<Workbook> _getWorkbook;
    private readonly Func<SheetId?> _getCurrentSheetId;
    private readonly ICommandBus _commandBus;
    private readonly Action<CellAddress> _navigateTo;
    private IReadOnlyList<FindResult> _results = [];
    private int _currentIndex = -1;
    private string _lastSearch = string.Empty;
    private StyleDiff? _findFormatDiff;
    private StyleDiff? _replaceFormatDiff;

    public FindReplaceDialog(
        Func<Workbook> getWorkbook,
        ICommandBus commandBus,
        Action<CellAddress> navigateTo,
        bool replaceMode = false,
        Func<SheetId?>? getCurrentSheetId = null)
    {
        _getWorkbook = getWorkbook;
        _getCurrentSheetId = getCurrentSheetId ?? (() => null);
        _commandBus = commandBus;
        _navigateTo = navigateTo;
        InitializeComponent();
        if (replaceMode)
        {
            FindReplaceTabs.SelectedItem = ReplaceTab;
            ReplaceFindBox.Focus();
        }
        else
        {
            FindBox.Focus();
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindAll_Click(object sender, RoutedEventArgs e) => FindAll();
    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OptionsExpander_Expanded(object sender, RoutedEventArgs e) => OptionsExpander.Header = "_Options <<";
    private void OptionsExpander_Collapsed(object sender, RoutedEventArgs e) => OptionsExpander.Header = "_Options >>";
    private void FindFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _findFormatDiff, FindFormatButton, ReplaceFindFormatButton);
    private void ReplaceWithFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _replaceFormatDiff, ReplaceWithFormatButton);

    private void FindBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) FindNext();
    }

    private void FindNext()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search)) return;

        if (search != _lastSearch)
        {
            _currentIndex = -1;
            _lastSearch = search;
        }

        _results = FindReplaceService.Find(
            _getWorkbook(), search,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        UpdateResultsGrid();

        if (_results.Count == 0)
        {
            StatusLabel.Text = "No matches found.";
            _currentIndex = -1;
            return;
        }

        _currentIndex = (_currentIndex + 1) % _results.Count;
        var result = _results[_currentIndex];
        StatusLabel.Text = $"Match {_currentIndex + 1} of {_results.Count}";
        _navigateTo(result.Address);
    }

    private void FindAll()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search)) return;

        _lastSearch = search;
        _currentIndex = -1;
        _results = FindReplaceService.Find(
            _getWorkbook(), search,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        UpdateResultsGrid();
        StatusLabel.Text = _results.Count == 0 ? "No matches found." : $"{_results.Count} cell(s) found.";
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search)) return;

        var count = FindReplaceService.ReplaceAll(
            _getWorkbook(), _commandBus, search, ReplaceBox.Text,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        StatusLabel.Text = count == 0 ? "No matches found." : $"Replaced {count} cell(s).";
        _results = [];
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private void ReplaceOne()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search)) return;

        if (_results.Count == 0 || _currentIndex < 0 || search != _lastSearch)
            FindNext();

        if (_results.Count == 0 || _currentIndex < 0)
            return;

        var result = _results[_currentIndex];
        var replaced = FindReplaceDialogPlanner.ReplaceSingleMatch(
            _getWorkbook(),
            _commandBus,
            result,
            search,
            ReplaceBox.Text,
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        if (!replaced)
        {
            StatusLabel.Text = "No replaceable match found.";
            return;
        }

        StatusLabel.Text = "Replaced 1 cell.";
        _results = [];
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private string SearchText => FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox.Text : FindBox.Text;

    private FindOptions CreateFindOptions() =>
        new(
            Within: WithinCombo.SelectedIndex == 1 ? FindWithin.Sheet : FindWithin.Workbook,
            CurrentSheetId: _getCurrentSheetId(),
            SearchOrder: SearchCombo.SelectedIndex == 1 ? FindSearchOrder.ByColumns : FindSearchOrder.ByRows,
            LookIn: LookInCombo.SelectedIndex switch
            {
                0 => FindLookIn.Formulas,
                2 => FindLookIn.Notes,
                3 => FindLookIn.Comments,
                _ => FindLookIn.Values
            },
            RequiredFormat: _findFormatDiff);

    private void PickFormat(ref StyleDiff? target, params Button[] buttons)
    {
        var baseStyle = target?.ApplyTo(CellStyle.Default) ?? CellStyle.Default;
        var dialog = new FormatCellsDialog(baseStyle, FormatCellsDialogTab.Font) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultDiff is null)
            return;

        target = dialog.ResultDiff;
        foreach (var button in buttons)
            button.Content = "For_mat...";
    }

    private void UpdateResultsGrid()
    {
        FindResultsGrid.ItemsSource = _results
            .Select(result => new FindResultRow(result.Address.ToA1(), result.MatchedText))
            .ToList();
    }

    private sealed record FindResultRow(string Address, string Value);
}

internal static class FindReplaceDialogPlanner
{
    public static bool ReplaceSingleMatch(
        Workbook workbook,
        ICommandBus commandBus,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell)
    {
        if (string.IsNullOrEmpty(searchText))
            return false;

        var sheet = workbook.GetSheet(match.Address.Sheet);
        var cell = sheet?.GetCell(match.Address);
        if (cell is null || cell.HasFormula)
            return false;

        var currentText = GetDisplayText(cell.Value);
        if (currentText is null)
            return false;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var isMatch = matchEntireCell
            ? currentText.Equals(searchText, comparison)
            : currentText.Contains(searchText, comparison);

        if (!isMatch)
            return false;

        var newText = matchEntireCell
            ? replaceText
            : currentText.Replace(searchText, replaceText, comparison);

        ScalarValue newValue = double.TryParse(newText, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? new NumberValue(d)
            : new TextValue(newText);

        var command = new EditCellsCommand(match.Address.Sheet, [(match.Address, Cell.FromValue(newValue))]);
        commandBus.Execute(workbook.Id, command);
        return true;
    }

    private static string? GetDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue err => err.Code,
        _ => null
    };
}
