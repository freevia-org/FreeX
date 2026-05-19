using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed class GoalSeekStatusDialog : Window
{
    public bool ApplyResult { get; private set; }

    public GoalSeekStatusDialog(GoalSeekResult result)
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
            Text = CreateMessage(result),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 76,
            Margin = new Thickness(4, 0, 0, 0),
            IsDefault = true
        };
        okButton.Click += (_, _) =>
        {
            ApplyResult = result.Converged;
            DialogResult = result.Converged;
        };
        buttons.Children.Add(okButton);

        if (result.Converged)
        {
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 76,
                Margin = new Thickness(4, 0, 0, 0),
                IsCancel = true
            };
            buttons.Children.Add(cancelButton);
        }

        stack.Children.Add(buttons);
        Content = stack;
    }

    public static string CreateMessage(GoalSeekResult result)
    {
        var foundValue = result.FoundValue.ToString("G10", CultureInfo.InvariantCulture);
        var actualResult = result.ActualResult.ToString("G10", CultureInfo.InvariantCulture);
        return result.Converged
            ? $"Goal Seek found a solution.\nChanging cell value: {foundValue}"
            : $"Goal Seek could not find a solution.\nClosest value: {foundValue}\nActual result: {actualResult}";
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
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() => Window.GetWindow(stack)!.DialogResult = true));
        return stack;
    }
}

public sealed class AccessibilityCheckerDialog : Window
{
    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        Title = "Accessibility Checker";
        Width = 520;
        Height = issues.Count == 0 ? 170 : 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Content = CreateContent(CreateMessage(issues));
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? "No accessibility issues found."
            : AccessibilityIssueFormatter.Format(issues);

    private static StackPanel CreateContent(string message)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 80,
            Margin = new Thickness(0, 0, 0, 16)
        });
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() => Window.GetWindow(stack)!.DialogResult = true));
        return stack;
    }
}
