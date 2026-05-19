using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class ErrorCheckingDialog : Window
{
    private readonly Action<CellAddress> _navigateTo;
    private readonly Func<FormulaErrorIssue, bool> _ignoreError;
    private readonly Action<FormulaErrorIssue> _traceError;
    private readonly ObservableCollection<FormulaErrorIssue> _issues = [];
    private readonly ListView _listView;
    private readonly TextBlock _header;

    public ErrorCheckingDialog(
        IReadOnlyList<FormulaErrorIssue> issues,
        Action<CellAddress> navigateTo,
        Func<FormulaErrorIssue, bool> ignoreError,
        Action<FormulaErrorIssue> traceError)
    {
        _navigateTo = navigateTo;
        _ignoreError = ignoreError;
        _traceError = traceError;

        Title = "Error Checking";
        Width = 720;
        Height = 360;
        MinWidth = 540;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;

        _header = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var goTo = new Button { Content = "Go To", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        goTo.Click += (_, _) => NavigateSelected();
        buttons.Children.Add(goTo);

        var previous = new Button { Content = "Previous", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        previous.Click += (_, _) => MoveSelection(-1);
        buttons.Children.Add(previous);

        var next = new Button { Content = "Next", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        next.Click += (_, _) => MoveSelection(1);
        buttons.Children.Add(next);

        var ignore = new Button { Content = "Ignore Error", Width = 92, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        ignore.Click += (_, _) => IgnoreSelected();
        buttons.Children.Add(ignore);

        var trace = new Button { Content = "Trace Error", Width = 88, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        trace.Click += (_, _) => TraceSelected();
        buttons.Children.Add(trace);

        var close = new Button { Content = "Close", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        _listView = new ListView { ItemsSource = _issues };
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Sheet", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.SheetName)) },
                new GridViewColumn { Header = "Cell", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Cell)) },
                new GridViewColumn { Header = "Issue", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.ErrorCode)) },
                new GridViewColumn { Header = "Formula", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.FormulaText)) },
                new GridViewColumn { Header = "Description", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Description)) }
            }
        };
        root.Children.Add(_listView);

        foreach (var issue in issues)
            _issues.Add(issue);
        RefreshHeader();
        if (_issues.Count > 0)
        {
            _listView.SelectedIndex = 0;
            Loaded += (_, _) => NavigateSelected();
        }
    }

    private void NavigateSelected()
    {
        if (_listView.SelectedItem is FormulaErrorIssue issue)
            _navigateTo(issue.Address);
    }

    private void MoveSelection(int delta)
    {
        if (_issues.Count == 0)
            return;

        var nextIndex = _listView.SelectedIndex < 0 ? 0 : _listView.SelectedIndex + delta;
        nextIndex = Math.Clamp(nextIndex, 0, _issues.Count - 1);
        _listView.SelectedIndex = nextIndex;
        _listView.ScrollIntoView(_issues[nextIndex]);
        NavigateSelected();
    }

    private void IgnoreSelected()
    {
        if (_listView.SelectedItem is not FormulaErrorIssue issue || !_ignoreError(issue))
            return;

        var index = _listView.SelectedIndex;
        var sameCellIssues = _issues
            .Where(candidate =>
                candidate.SheetId == issue.SheetId &&
                candidate.Address.Equals(issue.Address))
            .ToList();

        foreach (var sameCellIssue in sameCellIssues)
            _issues.Remove(sameCellIssue);

        RefreshHeader();

        if (_issues.Count == 0)
        {
            Close();
            return;
        }

        _listView.SelectedIndex = Math.Min(index, _issues.Count - 1);
        _listView.ScrollIntoView(_listView.SelectedItem);
        NavigateSelected();
    }

    private void RefreshHeader()
    {
        _header.Text = $"{_issues.Count} issue(s) found.";
    }

    private void TraceSelected()
    {
        if (_listView.SelectedItem is FormulaErrorIssue issue)
            _traceError(issue);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => NavigateSelected();
}
