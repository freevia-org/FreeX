using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

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
        stack.Children.Add(DialogButtonRowFactory.CreateOkOnly(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
