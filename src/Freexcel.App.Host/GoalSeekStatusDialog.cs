using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Calc;

namespace Freexcel.App.Host;

public sealed class GoalSeekStatusDialog : Window
{
    public bool ApplyResult { get; private set; }

    public GoalSeekStatusDialog(GoalSeekResult result, double targetValue)
    {
        Title = "Goal Seek Status";
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
        AutomationProperties.SetName(statusBlock, "Goal Seek status");
        AutomationProperties.SetAutomationId(statusBlock, "GoalSeekStatusSummary");
        AutomationProperties.SetHelpText(statusBlock, "Reports whether Goal Seek reached the target value.");
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
                Content = "_Keep Result",
                Width = 104,
                Margin = new Thickness(4, 0, 0, 0),
                IsDefault = true
            };
            AutomationProperties.SetName(keepButton, "Keep Result");
            AutomationProperties.SetAutomationId(keepButton, "GoalSeekKeepResultButton");
            AutomationProperties.SetHelpText(keepButton, "Keep the Goal Seek result in the changing cell.");
            keepButton.Click += (_, _) =>
            {
                ApplyResult = true;
                DialogResult = true;
            };
            buttons.Children.Add(keepButton);

            var restoreButton = new Button
            {
                Content = "_Restore Original Values",
                Width = 152,
                Margin = new Thickness(4, 0, 0, 0),
                IsCancel = true
            };
            AutomationProperties.SetName(restoreButton, "Restore Original Values");
            AutomationProperties.SetAutomationId(restoreButton, "GoalSeekRestoreOriginalValuesButton");
            AutomationProperties.SetHelpText(restoreButton, "Restore the original workbook values before Goal Seek ran.");
            buttons.Children.Add(restoreButton);
        }
        else
        {
            var okButton = new Button
            {
                Content = "_OK",
                Width = 76,
                Margin = new Thickness(4, 0, 0, 0),
                IsDefault = true,
                IsCancel = true
            };
            AutomationProperties.SetName(okButton, "OK");
            AutomationProperties.SetAutomationId(okButton, "GoalSeekStatusOkButton");
            AutomationProperties.SetHelpText(okButton, "Close the Goal Seek status dialog.");
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
            ? $"Goal Seek found a solution.\nTarget value: {target}\nCurrent value: {currentValue}\nChanging cell value: {changingCellValue}"
            : $"Goal Seek could not find a solution.\nTarget value: {target}\nCurrent value: {currentValue}\nChanging cell value: {changingCellValue}";
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
