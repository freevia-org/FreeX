using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Calc;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed class GoalSeekStatusDialog : Window
{
    public bool ApplyResult { get; private set; }

    public GoalSeekStatusDialog(GoalSeekResult result, double targetValue)
    {
        Title = UiText.Get("GoalSeekStatus_GoalSeekStatus");
        Width = GoalSeekStatusDialogPlanner.WindowWidth;
        Height = GoalSeekStatusDialogPlanner.WindowHeight(result.Converged);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        var statusBlock = new TextBlock
        {
            Text = CreateMessage(result, targetValue),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        AutomationProperties.SetName(statusBlock, UiText.Get("GoalSeekStatus_GoalSeekStatus"));
        AutomationProperties.SetAutomationId(statusBlock, "GoalSeekStatusSummary");
        AutomationProperties.SetHelpText(statusBlock, UiText.Get("GoalSeekStatus_ReportsWhetherGoalSeekReachedTheTargetValue"));
        stack.Children.Add(statusBlock);

        StackPanel buttons;
        if (result.Converged)
        {
            var keepButton = new Button
            {
                Content = UiText.Get("GoalSeekStatus_KeepResult"),
                Width = GoalSeekStatusDialogPlanner.KeepResultButtonWidth,
                IsDefault = true
            };
            AutomationProperties.SetName(keepButton, UiText.Get("GoalSeekStatus_KeepResult2"));
            AutomationProperties.SetAutomationId(keepButton, "GoalSeekKeepResultButton");
            AutomationProperties.SetHelpText(keepButton, UiText.Get("GoalSeekStatus_KeepTheGoalSeekResultInTheChangingCell"));
            keepButton.Click += (_, _) =>
            {
                ApplyResult = true;
                DialogResult = true;
            };

            var restoreButton = new Button
            {
                Content = UiText.Get("GoalSeekStatus_RestoreOriginalValues"),
                Width = GoalSeekStatusDialogPlanner.RestoreOriginalValuesButtonWidth,
                IsCancel = true
            };
            AutomationProperties.SetName(restoreButton, UiText.Get("GoalSeekStatus_RestoreOriginalValues2"));
            AutomationProperties.SetAutomationId(restoreButton, "GoalSeekRestoreOriginalValuesButton");
            AutomationProperties.SetHelpText(restoreButton, UiText.Get("GoalSeekStatus_RestoreTheOriginalWorkbookValuesBeforeGoalSeekRan"));
            buttons = DialogButtonRowFactory.Create(keepButton, restoreButton);
        }
        else
        {
            // Excel's Goal Seek Status dialog keeps its OK/Cancel pair even when a solution "may
            // not have been found": OK accepts the last-iterated (closest) changing-cell value,
            // Cancel restores the original value. Collapsing this to an OK-only button (as before)
            // left the user no click-through way to accept the approximation.
            buttons = DialogButtonRowFactory.Create(() =>
            {
                ApplyResult = true;
                DialogResult = true;
            }, 76);
            var okButton = (Button)buttons.Children[0];
            AutomationProperties.SetName(okButton, UiText.Get("GoalSeekStatus_Ok"));
            AutomationProperties.SetAutomationId(okButton, "GoalSeekStatusOkButton");
            AutomationProperties.SetHelpText(okButton, UiText.Get("GoalSeekStatus_KeepTheGoalSeekResultInTheChangingCell"));

            var cancelButton = (Button)buttons.Children[1];
            AutomationProperties.SetName(cancelButton, UiText.Get("GoalSeekStatus_RestoreOriginalValues2"));
            AutomationProperties.SetAutomationId(cancelButton, "GoalSeekStatusCancelButton");
            AutomationProperties.SetHelpText(cancelButton, UiText.Get("GoalSeekStatus_RestoreTheOriginalWorkbookValuesBeforeGoalSeekRan"));
        }

        stack.Children.Add(buttons);
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(GoalSeekResult result) =>
        CreateMessage(result, result.ActualResult);

    public static string CreateMessage(GoalSeekResult result, double targetValue)
        => GoalSeekStatusDialogPlanner.DescribeStatus(
            result.Converged,
            targetValue,
            result.ActualResult,
            result.FoundValue).Resolve(UiText.Get, UiText.Format);

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
