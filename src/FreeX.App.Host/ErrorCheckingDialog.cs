using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ErrorCheckingDialog : Window
{
    private readonly Action<CellAddress> _navigateTo;
    private readonly Func<FormulaErrorIssue, bool> _ignoreError;
    private readonly Action<FormulaErrorIssue> _traceError;
    private readonly Action<FormulaErrorIssue> _showCalculationSteps;
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
        Action<FormulaErrorIssue>? showCalculationSteps = null,
        Action? openOptions = null)
    {
        _navigateTo = navigateTo;
        _ignoreError = ignoreError;
        _traceError = traceError;
        _showCalculationSteps = showCalculationSteps ?? traceError;
        _openOptions = openOptions;

        Title = UiText.Get(ErrorCheckingDialogPlanner.TitleKey);
        Width = ErrorCheckingDialogPlanner.Width;
        Height = ErrorCheckingDialogPlanner.Height;
        MinWidth = ErrorCheckingDialogPlanner.MinWidth;
        MinHeight = ErrorCheckingDialogPlanner.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, ErrorCheckingDialogPlanner.DialogAutomationId);

        var root = new DockPanel { Margin = new Thickness(ErrorCheckingDialogPlanner.RootMargin) };
        Content = root;

        _header = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);

        var actionPanel = new GroupBox
        {
            Header = UiText.Get(ErrorCheckingDialogPlanner.HelpGroupHeaderKey),
            Width = ErrorCheckingDialogPlanner.ActionPanelWidth,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8)
        };
        DockPanel.SetDock(actionPanel, Dock.Right);
        var actionStack = new StackPanel();
        actionPanel.Content = actionStack;
        actionStack.Children.Add(new TextBlock
        {
            Text = UiText.Get(ErrorCheckingDialogPlanner.ActionIntroTextKey),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _helpButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.HelpButtonKey), Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(0, 0, 0, 6) };
        _helpButton.Click += (_, _) => ShowSelectedIssueHelp();
        actionStack.Children.Add(_helpButton);
        _showStepsButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.ShowCalculationStepsButtonKey), Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(0, 0, 0, 6) };
        _showStepsButton.Click += (_, _) => ShowCalculationStepsSelected();
        actionStack.Children.Add(_showStepsButton);
        _sideIgnoreButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey), Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(0, 0, 0, 6) };
        _sideIgnoreButton.Click += (_, _) => IgnoreSelected();
        actionStack.Children.Add(_sideIgnoreButton);
        _editFormulaButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.EditInFormulaBarButtonKey), Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(0, 0, 0, 6) };
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

        _goToButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.GoToButtonKey), Width = ErrorCheckingDialogPlanner.GoToButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        _goToButton.Click += (_, _) => NavigateSelected();
        buttons.Children.Add(_goToButton);

        _previousButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.PreviousButtonKey), Width = ErrorCheckingDialogPlanner.PreviousButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        _previousButton.Click += (_, _) => MoveSelection(-1);
        buttons.Children.Add(_previousButton);

        _nextButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.NextButtonKey), Width = ErrorCheckingDialogPlanner.NextButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) => MoveSelection(1);
        buttons.Children.Add(_nextButton);

        _ignoreButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey), Width = ErrorCheckingDialogPlanner.IgnoreButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        _ignoreButton.Click += (_, _) => IgnoreSelected();
        buttons.Children.Add(_ignoreButton);

        _traceButton = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.TraceErrorButtonKey), Width = ErrorCheckingDialogPlanner.TraceButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        _traceButton.Click += (_, _) => TraceSelected();
        buttons.Children.Add(_traceButton);

        var options = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.OptionsButtonKey), Width = ErrorCheckingDialogPlanner.OptionsButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0) };
        options.Click += (_, _) => _openOptions?.Invoke();
        buttons.Children.Add(options);

        var close = new Button { Content = UiText.Get(ErrorCheckingDialogPlanner.CloseButtonKey), Width = ErrorCheckingDialogPlanner.CloseButtonWidth, Height = ErrorCheckingDialogPlanner.ButtonHeight, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var listPanel = new DockPanel();
        _listView = new ListView { ItemsSource = _issues };
        AutomationProperties.SetAutomationId(_listView, ErrorCheckingDialogPlanner.IssuesAutomationId);
        AutomationProperties.SetName(_listView, UiText.Get(ErrorCheckingDialogPlanner.IssuesAutomationNameKey));
        var listLabel = new Label { Content = UiText.Get(ErrorCheckingDialogPlanner.IssuesLabelKey), Target = _listView, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(listLabel, Dock.Top);
        listPanel.Children.Add(listLabel);
        _listView.SelectionChanged += (_, _) => UpdateCommandStates();
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.KeyDown += ListView_KeyDown;
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn { Header = UiText.Get(ErrorCheckingDialogPlanner.SheetColumnHeaderKey), Width = ErrorCheckingDialogPlanner.SheetColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.SheetName)) },
                new GridViewColumn { Header = UiText.Get(ErrorCheckingDialogPlanner.CellColumnHeaderKey), Width = ErrorCheckingDialogPlanner.CellColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Cell)) },
                new GridViewColumn { Header = UiText.Get(ErrorCheckingDialogPlanner.IssueColumnHeaderKey), Width = ErrorCheckingDialogPlanner.IssueColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.ErrorCode)) },
                new GridViewColumn { Header = UiText.Get(ErrorCheckingDialogPlanner.FormulaColumnHeaderKey), Width = ErrorCheckingDialogPlanner.FormulaColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.FormulaText)) },
                new GridViewColumn { Header = UiText.Get(ErrorCheckingDialogPlanner.DescriptionColumnHeaderKey), Width = ErrorCheckingDialogPlanner.DescriptionColumnWidth, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FormulaErrorIssue.Description)) }
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
        _header.Text = UiText.Format(ErrorCheckingDialogPlanner.IssueCountHeaderKey, _issues.Count);
    }

    private void UpdateCommandStates()
    {
        var selectedIndex = _listView.SelectedIndex;
        var selectedIssue = _listView.SelectedItem as FormulaErrorIssue;
        var state = ErrorCheckingDialogPlanner.CreateCommandState(selectedIndex, _issues.Count, selectedIssue);
        _helpButton.IsEnabled = state.HasSelection;
        _showStepsButton.IsEnabled = state.CanShowCalculationSteps;
        _sideIgnoreButton.IsEnabled = state.HasSelection;
        _editFormulaButton.IsEnabled = state.HasSelection;
        _goToButton.IsEnabled = state.HasSelection;
        _ignoreButton.IsEnabled = state.HasSelection;
        _traceButton.IsEnabled = state.HasSelection;
        _previousButton.IsEnabled = state.CanPrevious;
        _nextButton.IsEnabled = state.CanNext;
    }

    private static bool HasCalculationSteps(FormulaErrorIssue issue) =>
        ErrorCheckingDialogPlanner.HasCalculationSteps(issue);

    private void ShowCalculationStepsSelected()
    {
        if (_listView.SelectedItem is FormulaErrorIssue issue && HasCalculationSteps(issue))
            _showCalculationSteps(issue);
    }

    private void TraceSelected()
    {
        if (_listView.SelectedItem is FormulaErrorIssue issue)
            _traceError(issue);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_listView.SelectedItem is not FormulaErrorIssue issue)
            return;

        _navigateTo(issue.Address);
        e.Handled = true;
    }

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
            ? UiText.Format(ErrorCheckingDialogPlanner.SelectedIssueHelpBodyKey, issue.ErrorCode, issue.Description)
            : UiText.Get(ErrorCheckingDialogPlanner.NoSelectionHelpBodyKey);

        DialogMessageHelper.ShowInfo(this, message, UiText.Get(ErrorCheckingDialogPlanner.HelpTitleKey));
    }
}
