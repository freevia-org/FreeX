using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class FindReplaceDialog : Window
{
    private readonly Func<Workbook> _getWorkbook;
    private readonly Func<SheetId?> _getCurrentSheetId;
    private readonly ICommandBus _commandBus;
    private readonly Action<CellAddress> _navigateTo;
    private readonly Func<CellAddress?> _getActiveSelectionCell;
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
        Func<SheetId?>? getCurrentSheetId = null,
        Func<CellAddress?>? getActiveSelectionCell = null)
    {
        _getWorkbook = getWorkbook;
        _getCurrentSheetId = getCurrentSheetId ?? (() => null);
        _commandBus = commandBus;
        _navigateTo = navigateTo;
        _getActiveSelectionCell = getActiveSelectionCell ?? (() => null);
        InitializeComponent();
        if (replaceMode)
        {
            FindReplaceTabs.SelectedItem = ReplaceTab;
        }
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        var target = FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox : FindBox;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindAll_Click(object sender, RoutedEventArgs e) => FindAll();
    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void FindResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FindResultsGrid.SelectedItem is FindResultRow row)
            _navigateTo(row.Address);
    }

    private void OptionsExpander_Expanded(object sender, RoutedEventArgs e) => OptionsExpander.Header = "_Options <<";
    private void OptionsExpander_Collapsed(object sender, RoutedEventArgs e) => OptionsExpander.Header = "_Options >>";
    private void FindFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _findFormatDiff, FindFormatButton, ReplaceFindFormatButton);
    private void ReplaceWithFormatButton_Click(object sender, RoutedEventArgs e) => PickFormat(ref _replaceFormatDiff, ReplaceWithFormatButton);
    private void ChooseFindFormatFromCellButton_Click(object sender, RoutedEventArgs e) => PickFormatFromCell(ref _findFormatDiff);
    private void ChooseReplaceWithFormatFromCellButton_Click(object sender, RoutedEventArgs e) => PickFormatFromCell(ref _replaceFormatDiff);
    private void FindClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        _findFormatDiff = null;
        UpdateFormatStateButtons();
    }

    private void ReplaceWithClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        _replaceFormatDiff = null;
        UpdateFormatStateButtons();
    }

    private void FindBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) FindNext();
    }

    private void FindNext()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

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
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

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
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

        var result = FindReplaceService.TryReplaceAll(
            _getWorkbook(), _commandBus, search, ReplaceBox.Text,
            CreateFindOptions(),
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true,
            replacementFormat: _replaceFormatDiff);

        if (ShowReplaceFailureWarning(result.Failure))
            return;

        StatusLabel.Text = result.ReplacedCount == 0 ? "No matches found." : $"Replaced {result.ReplacedCount} cell(s).";
        _results = [];
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private void ReplaceOne()
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search) && ShowBlankSearchWarning()) return;

        if (_results.Count == 0 || _currentIndex < 0 || search != _lastSearch)
            FindNext();

        if (_results.Count == 0 || _currentIndex < 0)
            return;

        var match = _results[_currentIndex];
        var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
            _getWorkbook(),
            _commandBus,
            match,
            search,
            ReplaceBox.Text,
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true,
            replacementFormat: _replaceFormatDiff);

        if (ShowReplaceFailureWarning(result.Failure))
            return;

        if (!result.Replaced)
        {
            StatusLabel.Text = "No replaceable match found.";
            return;
        }

        StatusLabel.Text = "Replaced 1 cell.";
        _results = [];
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private bool ShowReplaceFailureWarning(CommandOutcome? failure)
    {
        if (failure is null)
            return false;

        MessageBox.Show(
            this,
            failure.ErrorMessage ?? "The replacement could not be completed.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        FocusSearchBox();
        return true;
    }

    private string SearchText => FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox.Text : FindBox.Text;

    private bool ShowBlankSearchWarning()
    {
        StatusLabel.Text = "Enter text in Find what.";
        FocusSearchBox();
        return true;
    }

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
        UpdateFormatStateButtons();
    }

    private void PickFormatFromCell(ref StyleDiff? target)
    {
        var address = FindResultsGrid.SelectedItem is FindResultRow row
            ? row.Address
            : _getActiveSelectionCell();
        if (address is null)
        {
            StatusLabel.Text = "Select a result cell or worksheet cell to choose its format.";
            return;
        }

        var diff = FindReplaceDialogPlanner.CreateFormatDiffFromCell(_getWorkbook(), address.Value);
        if (diff is null)
        {
            StatusLabel.Text = "No cell format found.";
            return;
        }

        target = diff;
        StatusLabel.Text = FindResultsGrid.SelectedItem is FindResultRow
            ? "Format chosen from selected result cell."
            : "Format chosen from active worksheet cell.";
        UpdateFormatStateButtons();
    }

    private void UpdateFormatStateButtons()
    {
        SetFormatState(_findFormatDiff is not null, "Find format is set", FindFormatButton, FindClearFormatButton);
        SetFormatState(_findFormatDiff is not null, "Find format is set", ReplaceFindFormatButton, ReplaceFindClearFormatButton);
        SetFormatState(_replaceFormatDiff is not null, "Replace format is set", ReplaceWithFormatButton, ReplaceWithClearFormatButton);
    }

    private static void SetFormatState(bool isSet, string toolTip, Button formatButton, Button clearButton)
    {
        formatButton.Content = isSet ? "Format Set..." : "For_mat...";
        formatButton.ToolTip = isSet ? toolTip : null;
        clearButton.Visibility = isSet ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateResultsGrid()
    {
        FindResultsGrid.ItemsSource = FindReplaceDialogPlanner.BuildFindResultRows(_getWorkbook(), _results);
    }
}
