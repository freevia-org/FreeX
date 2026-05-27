using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AccessibilityCheckerDialogResult(AccessibilityIssue Issue);

public sealed class AccessibilityCheckerDialog : Window
{
    private readonly TextBox _messageBox = new();
    private readonly ListBox _issueList = new();
    private readonly Button _goToButton = new() { Content = "_Go To", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };

    public AccessibilityCheckerDialogResult? Result { get; private set; }

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        Title = "Accessibility Checker";
        Width = 520;
        Height = issues.Count == 0 ? 170 : 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        AutomationProperties.SetName(_messageBox, "Accessibility checker result");
        AutomationProperties.SetHelpText(_messageBox, "Summarizes the workbook accessibility check when no issues are found.");
        AutomationProperties.SetName(_issueList, "Accessibility issues");
        AutomationProperties.SetHelpText(_issueList, "Select an accessibility issue and choose Go To to navigate to its workbook location.");
        AutomationProperties.SetName(_goToButton, "Go to selected accessibility issue");
        Content = issues.Count == 0
            ? CreateCleanContent(CreateMessage(issues))
            : CreateIssueContent(issues);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? "No accessibility issues found."
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
        stack.Children.Add(new Label { Content = "_Issues:", Target = _issueList, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
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
        buttons.Children.Add(new Button { Content = "_Close", Width = 76, IsCancel = true });
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
