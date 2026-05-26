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
    private readonly Action? _openOptions;
    private readonly ObservableCollection<FormulaErrorIssue> _issues = [];
    private readonly ListView _listView;
    private readonly TextBlock _header;
    private readonly Button _helpButton;
    private readonly Button _showStepsButton;
    private readonly Button _sideIgnoreButton;
    private readonly Button _editFormulaButton;
    private readonly Button _goToButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _ignoreButton;
    private readonly Button _traceButton;

    public ErrorCheckingDialog(
        IReadOnlyList<FormulaErrorIssue> issues,
        Action<CellAddress> navigateTo,
        Func<FormulaErrorIssue, bool> ignoreError,
        Action<FormulaErrorIssue> traceError,
        Action? openOptions = null)
    {
        _navigateTo = navigateTo;
        _ignoreError = ignoreError;
        _traceError = traceError;
        _openOptions = openOptions;

        Title = "Error Checking";
        Width = 720;
        Height = 420;
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

        var actionPanel = new GroupBox
        {
            Header = "Error help",
            Width = 180,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8)
        };
        DockPanel.SetDock(actionPanel, Dock.Right);
        var actionStack = new StackPanel();
        actionPanel.Content = actionStack;
        actionStack.Children.Add(new TextBlock
        {
            Text = "Choose an action for the selected issue.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _helpButton = new Button { Content = "_Help on this error", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _helpButton.Click += (_, _) => ShowSelectedIssueHelp();
        actionStack.Children.Add(_helpButton);
        _showStepsButton = new Button { Content = "Show _Calculation Steps", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _showStepsButton.Click += (_, _) => TraceSelected();
        actionStack.Children.Add(_showStepsButton);
        _sideIgnoreButton = new Button { Content = "_Ignore Error", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _sideIgnoreButton.Click += (_, _) => IgnoreSelected();
        actionStack.Children.Add(_sideIgnoreButton);
        _editFormulaButton = new Button { Content = "_Edit in Formula Bar", Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _editFormulaButton.Click += (_, _) => NavigateSelected();
        actionStack.Children.Add(_editFormulaButton);
        root.Children.Add(actionPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _goToButton = new Button { Content = "_Go To", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _goToButton.Click += (_, _) => NavigateSelected();
        buttons.Children.Add(_goToButton);

        _previousButton = new Button { Content = "_Previous", Width = 84, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _previousButton.Click += (_, _) => MoveSelection(-1);
        buttons.Children.Add(_previousButton);

        _nextButton = new Button { Content = "_Next", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) => MoveSelection(1);
        buttons.Children.Add(_nextButton);

        _ignoreButton = new Button { Content = "_Ignore Error", Width = 104, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _ignoreButton.Click += (_, _) => IgnoreSelected();
        buttons.Children.Add(_ignoreButton);

        _traceButton = new Button { Content = "_Trace Error", Width = 96, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _traceButton.Click += (_, _) => TraceSelected();
        buttons.Children.Add(_traceButton);

        var options = new Button { Content = "_Options...", Width = 92, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        options.Click += (_, _) => _openOptions?.Invoke();
        buttons.Children.Add(options);

        var close = new Button { Content = "_Close", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        _listView = new ListView { ItemsSource = _issues };
        _listView.SelectionChanged += (_, _) => UpdateCommandStates();
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.KeyDown += ListView_KeyDown;
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
        }
        UpdateCommandStates();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _listView.Focus();
        Keyboard.Focus(_listView);
        NavigateSelected();
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
        UpdateCommandStates();
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
        UpdateCommandStates();
    }

    private void RefreshHeader()
    {
        _header.Text = $"{_issues.Count} issue(s) found.";
    }

    private void UpdateCommandStates()
    {
        var selectedIndex = _listView.SelectedIndex;
        var hasSelection = selectedIndex >= 0 && selectedIndex < _issues.Count;
        _helpButton.IsEnabled = hasSelection;
        _showStepsButton.IsEnabled = hasSelection;
        _sideIgnoreButton.IsEnabled = hasSelection;
        _editFormulaButton.IsEnabled = hasSelection;
        _goToButton.IsEnabled = hasSelection;
        _ignoreButton.IsEnabled = hasSelection;
        _traceButton.IsEnabled = hasSelection;
        _previousButton.IsEnabled = hasSelection && selectedIndex > 0;
        _nextButton.IsEnabled = hasSelection && selectedIndex < _issues.Count - 1;
    }

    private void TraceSelected()
    {
        if (_listView.SelectedItem is FormulaErrorIssue issue)
            _traceError(issue);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => NavigateSelected();

    private void ListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateSelected();
            e.Handled = true;
        }
    }

    private void ShowSelectedIssueHelp()
    {
        var message = _listView.SelectedItem is FormulaErrorIssue issue
            ? $"{issue.ErrorCode}\n\n{issue.Description}\n\nUse Show Calculation Steps to trace the formula, Ignore Error to suppress this issue, or Edit in Formula Bar to correct the formula."
            : "Select an issue to see its description and available correction actions.";

        MessageBox.Show(this, message, "Error Checking Help", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
