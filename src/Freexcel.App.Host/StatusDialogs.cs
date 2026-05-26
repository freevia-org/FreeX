using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;

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
        stack.Children.Add(new TextBlock
        {
            Text = CreateMessage(result, targetValue),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

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
            buttons.Children.Add(restoreButton);
        }
        else
        {
            var okButton = new Button
            {
                Content = "_OK",
                Width = 76,
                Margin = new Thickness(4, 0, 0, 0),
                IsDefault = true
            };
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
        var currentFormulaResult = result.ActualResult.ToString("G10", CultureInfo.InvariantCulture);
        var changingCellValue = result.FoundValue.ToString("G10", CultureInfo.InvariantCulture);
        return result.Converged
            ? $"Goal Seek found a solution.\nTarget value: {target}\nCurrent formula result: {currentFormulaResult}\nChanging cell value: {changingCellValue}"
            : $"Goal Seek could not find a solution.\nTarget value: {target}\nCurrent formula result: {currentFormulaResult}\nChanging cell value: {changingCellValue}";
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}

public sealed class WorkbookStatisticsDialog : Window
{
    public WorkbookStatisticsDialog(WorkbookStatistics statistics)
    {
        Title = "Workbook Statistics";
        Width = 360;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateTextContent(CreateMessage(statistics));
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(WorkbookStatistics statistics) =>
        WorkbookStatisticsFormatter.Format(statistics);

    private static StackPanel CreateTextContent(string message)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        stack.Children.Add(DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}

internal static class StatusDialogKeyboardFocus
{
    public static void FocusDefaultButton(DependencyObject parent)
    {
        if (FindDefaultButton(parent) is not { } button)
            return;

        button.Focus();
        Keyboard.Focus(button);
    }

    private static Button? FindDefaultButton(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is Button { IsDefault: true } button)
                return button;

            if (child is DependencyObject dependencyObject &&
                FindDefaultButton(dependencyObject) is { } nestedButton)
            {
                return nestedButton;
            }
        }

        return null;
    }
}

public sealed class AccessibilityCheckerDialog : Window
{
    private readonly TextBox _messageBox = new();

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        Title = "Accessibility Checker";
        Width = 520;
        Height = issues.Count == 0 ? 170 : 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Content = CreateContent(CreateMessage(issues));
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? "No accessibility issues found."
            : AccessibilityIssueFormatter.Format(issues);

    private StackPanel CreateContent(string message)
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
        stack.Children.Add(DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _messageBox.Focus();
        Keyboard.Focus(_messageBox);
    }
}
