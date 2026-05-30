using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

        Title = UiText.Get("ErrorChecking_Title");
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
            Header = UiText.Get("ErrorChecking_HelpGroupHeader"),
            Width = 180,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8)
        };
        DockPanel.SetDock(actionPanel, Dock.Right);
        var actionStack = new StackPanel();
        actionPanel.Content = actionStack;
        actionStack.Children.Add(new TextBlock
        {
            Text = UiText.Get("ErrorChecking_ActionIntroText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _helpButton = new Button { Content = UiText.Get("ErrorChecking_HelpButton"), Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _helpButton.Click += (_, _) => ShowSelectedIssueHelp();
        actionStack.Children.Add(_helpButton);
        _showStepsButton = new Button { Content = UiText.Get("ErrorChecking_ShowCalculationStepsButton"), Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _showStepsButton.Click += (_, _) => TraceSelected();
        actionStack.Children.Add(_showStepsButton);
        _sideIgnoreButton = new Button { Content = UiText.Get("ErrorChecking_IgnoreErrorButton"), Height = 26, Margin = new Thickness(0, 0, 0, 6) };
        _sideIgnoreButton.Click += (_, _) => IgnoreSelected();
        actionStack.Children.Add(_sideIgnoreButton);
        _editFormulaButton = new Button { Content = UiText.Get("ErrorChecking_EditInFormulaBarButton"), Height = 26, Margin = new Thickness(0, 0, 0, 6) };
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

        _goToButton = new Button { Content = UiText.Get("ErrorChecking_GoToButton"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _goToButton.Click += (_, _) => NavigateSelected();
        buttons.Children.Add(_goToButton);

        _previousButton = new Button { Content = UiText.Get("ErrorChecking_PreviousButton"), Width = 84, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _previousButton.Click += (_, _) => MoveSelection(-1);
        buttons.Children.Add(_previousButton);

        _nextButton = new Button { Content = UiText.Get("ErrorChecking_NextButton"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) => MoveSelection(1);
        buttons.Children.Add(_nextButton);

        _ignoreButton = new Button { Content = UiText.Get("ErrorChecking_IgnoreErrorButton"), Width = 104, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _ignoreButton.Click += (_, _) => IgnoreSelected();
        buttons.Children.Add(_ignoreButton);

        _traceButton = new Button { Content = UiText.Get("ErrorChecking_TraceErrorButton"), Width = 96, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _traceButton.Click += (_, _) => TraceSelected();
        buttons.Children.Add(_traceButton);

        var options = new Button { Content = UiText.Get("ErrorChecking_OptionsButton"), Width = 92, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        options.Click += (_, _) => _openOptions?.Invoke();
        buttons.Children.Add(options);

        var close = new Button { Content = UiText.Get("ErrorChecking_CloseButton"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var listPanel = new DockPanel();
        _listView = new ListView { ItemsSource = _issues };
        AutomationProperties.SetName(_listView, UiText.Get("ErrorChecking_IssuesAutomationName"));
        var listLabel = new Label { Content = UiText.Get("ErrorChecking_IssuesLabel"), Target = _listView, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(listLabel, Dock.Top);
        listPanel.Children.Add(listLabel);
        _listView.SelectionChanged += (_, _) => UpdateCommandStates();
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.KeyDown += ListView_KeyDown;
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn { Header = UiText.Get("ErrorChecking_SheetColumnHeader"), Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.SheetName)) },
                new GridViewColumn { Header = UiText.Get("ErrorChecking_CellColumnHeader"), Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Cell)) },
                new GridViewColumn { Header = UiText.Get("ErrorChecking_IssueColumnHeader"), Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.ErrorCode)) },
                new GridViewColumn { Header = UiText.Get("ErrorChecking_FormulaColumnHeader"), Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.FormulaText)) },
                new GridViewColumn { Header = UiText.Get("ErrorChecking_DescriptionColumnHeader"), Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Description)) }
            }
        };
        listPanel.Children.Add(_listView);
        root.Children.Add(listPanel);

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
        _header.Text = UiText.Format("ErrorChecking_IssueCountHeader", _issues.Count);
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
            ? UiText.Format("ErrorChecking_SelectedIssueHelpBody", issue.ErrorCode, issue.Description)
            : UiText.Get("ErrorChecking_NoSelectionHelpBody");

        DialogMessageHelper.ShowInfo(this, message, UiText.Get("ErrorChecking_HelpTitle"));
    }
}
