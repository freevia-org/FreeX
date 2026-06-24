using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.Core.Commands;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Review ▸ Check Accessibility — dedicated compact issues dialog matching WPF AccessibilityCheckerDialog.
    // Calls AccessibilityCheckerService.FindIssues (via the session's workbook), shows an issues list with a
    // [Go To] [Close] action row when issues are found, or a no-issues message when clean. [Go To] calls
    // _session.GoToAccessibilityIssue which internally calls ReviewWorkflowPlanner.GetAccessibilityNavigationTarget.

    private async Task ShowAccessibilityCheckerDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var issues = AccessibilityCheckerService.FindIssues(_session.Workbook);

        if (issues.Count == 0)
        {
            await ShowAccessibilityCheckerCleanDialogAsync();
            return;
        }

        await ShowAccessibilityCheckerIssuesDialogAsync(issues);
    }

    private async Task ShowAccessibilityCheckerCleanDialogAsync()
    {
        var dialog = new Window
        {
            Title = UiText.Get("ShellLoc_AccessibilityCheckerTitle"),
            Width = 520,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        var messageBlock = new TextBlock
        {
            Text = UiText.Get("ShellLoc_AccessibilityCheckerNoIssues"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        AutomationProperties.SetName(messageBlock, UiText.Get("ShellLoc_AccessibilityCheckerResultAutomationName"));
        AutomationProperties.SetAutomationId(messageBlock, "AccessibilityCheckerResultText");
        AutomationProperties.SetHelpText(messageBlock, UiText.Get("ShellLoc_AccessibilityCheckerResultHelpText"));

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            MinWidth = 76,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        okButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                messageBlock,
                okButton,
            },
        };

        dialog.Opened += (_, _) => okButton.Focus();

        await dialog.ShowDialog(this);
    }

    private async Task ShowAccessibilityCheckerIssuesDialogAsync(IReadOnlyList<AccessibilityIssue> issues)
    {
        var dialog = new Window
        {
            Title = UiText.Get("ShellLoc_AccessibilityCheckerTitle"),
            Width = 520,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        // Issues list
        var issueList = new ListBox
        {
            MinHeight = 190,
            Margin = new Thickness(0, 0, 0, 16),
        };
        AutomationProperties.SetName(issueList, UiText.Get("ShellLoc_AccessibilityCheckerIssueListAutomationName"));
        AutomationProperties.SetAutomationId(issueList, "AccessibilityCheckerIssueList");
        AutomationProperties.SetHelpText(issueList, UiText.Get("ShellLoc_AccessibilityCheckerIssueListHelpText"));

        foreach (var issue in issues)
            issueList.Items.Add(new AccessibilityIssueListItem(issue));

        issueList.SelectedIndex = 0;

        // Buttons
        var goToButton = new Button
        {
            Content = UiText.Get("ShellLoc_AccessibilityCheckerGoToButton"),
            MinWidth = 76,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(goToButton, UiText.Get("ShellLoc_AccessibilityCheckerGoToAutomationName"));
        AutomationProperties.SetAutomationId(goToButton, "AccessibilityCheckerGoToButton");
        AutomationProperties.SetHelpText(goToButton, UiText.Get("ShellLoc_AccessibilityCheckerGoToHelpText"));

        var closeButton = new Button
        {
            Content = UiText.Get("Common_Close"),
            MinWidth = 76,
        };
        AutomationProperties.SetName(closeButton, UiText.Get("ShellLoc_AccessibilityCheckerCloseAutomationName"));
        AutomationProperties.SetAutomationId(closeButton, "AccessibilityCheckerCloseButton");
        AutomationProperties.SetHelpText(closeButton, UiText.Get("ShellLoc_AccessibilityCheckerCloseHelpText"));

        AccessibilityIssue? selectedIssue = null;

        void UpdateGoToState() =>
            goToButton.IsEnabled = issueList.SelectedItem is AccessibilityIssueListItem;

        issueList.SelectionChanged += (_, _) => UpdateGoToState();
        UpdateGoToState();

        void GoToSelected()
        {
            if (issueList.SelectedItem is not AccessibilityIssueListItem item)
                return;
            selectedIssue = item.Issue;
            dialog.Close();
        }

        issueList.DoubleTapped += (_, _) => GoToSelected();
        goToButton.Click += (_, _) => GoToSelected();
        closeButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        var issuesLabel = new TextBlock
        {
            Text = UiText.Get("ShellLoc_AccessibilityCheckerIssuesLabel"),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                goToButton,
                closeButton,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                issuesLabel,
                issueList,
                buttonRow,
            },
        };

        dialog.Opened += (_, _) => issueList.Focus();

        await dialog.ShowDialog(this);

        // Navigate after dialog closes (if Go To was used)
        if (selectedIssue is not null)
        {
            ClearSelectedDrawingObject();
            var result = _session.GoToAccessibilityIssue(selectedIssue);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? "Could not navigate to accessibility issue.");
                return;
            }

            if (result.SelectedRange is { } selectedRange)
                RefreshShell($"Selected {FormatRangeReference(selectedRange)} (accessibility issue)");
        }
    }

    private sealed record AccessibilityIssueListItem(AccessibilityIssue Issue)
    {
        public override string ToString() => $"{Issue.SheetName}!{Issue.Location}: {Issue.Message}";
    }
}
