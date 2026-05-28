using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Freexcel.App.Host;

internal static class DialogButtonRowFactory
{
    public static StackPanel Create(Action accept, double buttonWidth, Thickness rowMargin = default, string acceptContent = "_OK")
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        var ok = new Button
        {
            Content = acceptContent,
            Width = buttonWidth,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        AutomationProperties.SetName(ok, CreateAutomationName(acceptContent));
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        var cancel = new Button
        {
            Content = "_Cancel",
            Width = buttonWidth,
            IsCancel = true
        };
        AutomationProperties.SetName(cancel, "Cancel");
        row.Children.Add(cancel);
        return row;
    }

    public static StackPanel CreateOkOnly(Action accept, double buttonWidth, Thickness rowMargin = default, string acceptContent = "_OK")
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        var ok = new Button
        {
            Content = acceptContent,
            Width = buttonWidth,
            IsDefault = true,
            IsCancel = true
        };
        AutomationProperties.SetName(ok, CreateAutomationName(acceptContent));
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        return row;
    }

    private static string CreateAutomationName(string content) =>
        content.Replace("_", string.Empty, StringComparison.Ordinal);
}
