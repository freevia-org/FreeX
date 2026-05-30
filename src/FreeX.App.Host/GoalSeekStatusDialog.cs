using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Calc;

namespace FreeX.App.Host;

public sealed class GoalSeekStatusDialog : Window
{
    public bool ApplyResult { get; private set; }

    public GoalSeekStatusDialog(GoalSeekResult result, double targetValue)
    {
        Title = UiText.Get("GoalSeekStatus_GoalSeekStatus");
        Width = 380;
        Height = result.Converged ? 190 : 170;
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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (result.Converged)
        {
            var keepButton = new Button
            {
                Content = UiText.Get("GoalSeekStatus_KeepResult"),
                Width = 104,
                Margin = new Thickness(4, 0, 0, 0),
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
            buttons.Children.Add(keepButton);

            var restoreButton = new Button
            {
                Content = UiText.Get("GoalSeekStatus_RestoreOriginalValues"),
                Width = 152,
                Margin = new Thickness(4, 0, 0, 0),
                IsCancel = true
            };
            AutomationProperties.SetName(restoreButton, UiText.Get("GoalSeekStatus_RestoreOriginalValues2"));
            AutomationProperties.SetAutomationId(restoreButton, "GoalSeekRestoreOriginalValuesButton");
            AutomationProperties.SetHelpText(restoreButton, UiText.Get("GoalSeekStatus_RestoreTheOriginalWorkbookValuesBeforeGoalSeekRan"));
            buttons.Children.Add(restoreButton);
        }
        else
        {
            var okButton = new Button
            {
                Content = UiText.Ok,
                Width = 76,
                Margin = new Thickness(4, 0, 0, 0),
                IsDefault = true,
                IsCancel = true
            };
            AutomationProperties.SetName(okButton, UiText.Get("GoalSeekStatus_Ok"));
            AutomationProperties.SetAutomationId(okButton, "GoalSeekStatusOkButton");
            AutomationProperties.SetHelpText(okButton, UiText.Get("GoalSeekStatus_CloseTheGoalSeekStatusDialog"));
            okButton.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(okButton);
        }

        stack.Children.Add(buttons);
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(GoalSeekResult result) =>
        CreateMessage(result, result.ActualResult);

    public static string CreateMessage(GoalSeekResult result, double targetValue)
    {
        var target = targetValue.ToString("G10", CultureInfo.InvariantCulture);
        var currentValue = result.ActualResult.ToString("G10", CultureInfo.InvariantCulture);
        var changingCellValue = result.FoundValue.ToString("G10", CultureInfo.InvariantCulture);
        return result.Converged
            ? UiText.Format("GoalSeekStatus_SuccessSummary", target, currentValue, changingCellValue)
            : UiText.Format("GoalSeekStatus_FailureSummary", target, currentValue, changingCellValue);
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
