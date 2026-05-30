using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AccessibilityCheckerDialogResult(AccessibilityIssue Issue);

public sealed class AccessibilityCheckerDialog : Window
{
    private readonly TextBox _messageBox = new();
    private readonly ListBox _issueList = new();
    private readonly Button _goToButton = new() { Content = UiText.Get("AccessibilityChecker_GoToButton"), Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _closeButton = new() { Content = UiText.Get("AccessibilityChecker_CloseButton"), Width = 76, IsCancel = true };

    public AccessibilityCheckerDialogResult? Result { get; private set; }

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        Title = UiText.Get("AccessibilityChecker_Title");
        Width = 520;
        Height = issues.Count == 0 ? 170 : 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        AutomationProperties.SetName(_messageBox, UiText.Get("AccessibilityChecker_ResultAutomationName"));
        AutomationProperties.SetAutomationId(_messageBox, "AccessibilityCheckerResultText");
        AutomationProperties.SetHelpText(_messageBox, UiText.Get("AccessibilityChecker_ResultHelpText"));
        AutomationProperties.SetName(_issueList, UiText.Get("AccessibilityChecker_IssueListAutomationName"));
        AutomationProperties.SetAutomationId(_issueList, "AccessibilityCheckerIssueList");
        AutomationProperties.SetHelpText(_issueList, UiText.Get("AccessibilityChecker_IssueListHelpText"));
        AutomationProperties.SetName(_goToButton, UiText.Get("AccessibilityChecker_GoToAutomationName"));
        AutomationProperties.SetAutomationId(_goToButton, "AccessibilityCheckerGoToButton");
        AutomationProperties.SetHelpText(_goToButton, UiText.Get("AccessibilityChecker_GoToHelpText"));
        AutomationProperties.SetName(_closeButton, UiText.Get("AccessibilityChecker_CloseAutomationName"));
        AutomationProperties.SetAutomationId(_closeButton, "AccessibilityCheckerCloseButton");
        AutomationProperties.SetHelpText(_closeButton, UiText.Get("AccessibilityChecker_CloseHelpText"));
        Content = issues.Count == 0
            ? CreateCleanContent(CreateMessage(issues))
            : CreateIssueContent(issues);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? UiText.Get("AccessibilityChecker_NoIssuesMessage")
            : AccessibilityIssueFormatter.Format(issues);

    public static CellAddress GetNavigationTarget(AccessibilityIssue issue)
    {
        var location = issue.Location.Trim();
        var firstLocation = location.Split(':', 2)[0];
        return CellAddress.TryParse(firstLocation, issue.SheetId, out var address)
            ? address
            : new CellAddress(issue.SheetId, 1, 1);
    }

    private StackPanel CreateCleanContent(string message)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        _messageBox.Text = message;
        _messageBox.IsReadOnly = true;
        _messageBox.AcceptsReturn = true;
        _messageBox.TextWrapping = TextWrapping.Wrap;
        _messageBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _messageBox.MinHeight = 80;
        _messageBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_messageBox);
        stack.Children.Add(DialogButtonRowFactory.CreateOkOnly(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private StackPanel CreateIssueContent(IReadOnlyList<AccessibilityIssue> issues)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = UiText.Get("AccessibilityChecker_IssuesLabel"), Target = _issueList, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _issueList.ItemsSource = issues
            .Select(issue => new AccessibilityIssueListItem(issue))
            .ToList();
        _issueList.DisplayMemberPath = nameof(AccessibilityIssueListItem.Text);
        _issueList.MinHeight = 190;
        _issueList.Margin = new Thickness(0, 0, 0, 16);
        _issueList.SelectedIndex = 0;
        _issueList.SelectionChanged += (_, _) => UpdateGoToButtonState();
        _issueList.MouseDoubleClick += (_, _) => GoToSelectedIssue();
        stack.Children.Add(_issueList);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        _goToButton.Click += (_, _) => GoToSelectedIssue();
        buttons.Children.Add(_goToButton);
        buttons.Children.Add(_closeButton);
        stack.Children.Add(buttons);
        UpdateGoToButtonState();
        return stack;
    }

    private void GoToSelectedIssue()
    {
        if (_issueList.SelectedItem is not AccessibilityIssueListItem item)
            return;

        Result = new AccessibilityCheckerDialogResult(item.Issue);
        DialogResult = true;
    }

    private void UpdateGoToButtonState()
    {
        _goToButton.IsEnabled = _issueList.SelectedItem is not null;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_issueList.Items.Count > 0)
        {
            _issueList.Focus();
            Keyboard.Focus(_issueList);
            return;
        }

        _messageBox.Focus();
        Keyboard.Focus(_messageBox);
    }

    private sealed record AccessibilityIssueListItem(AccessibilityIssue Issue)
    {
        public string Text => $"{Issue.SheetName}!{Issue.Location}: {Issue.Message}";
    }
}
