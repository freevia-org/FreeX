using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class FindReplaceDialog : Window
{
    private readonly Func<Workbook> _getWorkbook;
    private readonly ICommandBus _commandBus;
    private readonly Action<CellAddress> _navigateTo;
    private IReadOnlyList<FindResult> _results = [];
    private int _currentIndex = -1;
    private string _lastSearch = string.Empty;

    public FindReplaceDialog(Func<Workbook> getWorkbook, ICommandBus commandBus, Action<CellAddress> navigateTo, bool replaceMode = false)
    {
        _getWorkbook = getWorkbook;
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
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true,
            searchFormulas: SearchFormulas);

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
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true,
            searchFormulas: SearchFormulas);

        UpdateResultsGrid();
        StatusLabel.Text = _results.Count == 0 ? "No matches found." : $"{_results.Count} cell(s) found.";
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var search = SearchText;
        if (string.IsNullOrEmpty(search)) return;

        var count = FindReplaceService.ReplaceAll(
            _getWorkbook(), _commandBus, search, ReplaceBox.Text,
            matchCase: MatchCaseBox.IsChecked == true,
            matchEntireCell: MatchEntireBox.IsChecked == true);

        StatusLabel.Text = count == 0 ? "No matches found." : $"Replaced {count} cell(s).";
        _results = [];
        _currentIndex = -1;
        UpdateResultsGrid();
    }

    private string SearchText => FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox.Text : FindBox.Text;

    private bool SearchFormulas => LookInCombo.SelectedIndex == 0;

    private void UpdateResultsGrid()
    {
        FindResultsGrid.ItemsSource = _results
            .Select(result => new FindResultRow(result.Address.ToA1(), result.MatchedText))
            .ToList();
    }

    private sealed record FindResultRow(string Address, string Value);
}
