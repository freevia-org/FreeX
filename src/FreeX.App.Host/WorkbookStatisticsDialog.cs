using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

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
        var statisticsBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        AutomationProperties.SetName(statisticsBlock, "Workbook statistics");
        AutomationProperties.SetAutomationId(statisticsBlock, "WorkbookStatisticsSummary");
        AutomationProperties.SetHelpText(statisticsBlock, "Summarizes sheet, cell, formula, comment, and object counts for the workbook.");
        stack.Children.Add(statisticsBlock);
        stack.Children.Add(DialogButtonRowFactory.CreateOkOnly(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
