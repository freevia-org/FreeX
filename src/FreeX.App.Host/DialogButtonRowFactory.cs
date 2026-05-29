using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

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
        SetAcceleratorKey(ok, acceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        var cancel = new Button
        {
            Content = "_Cancel",
            Width = buttonWidth,
            IsCancel = true
        };
        AutomationProperties.SetName(cancel, "Cancel");
        SetAcceleratorKey(cancel, "_Cancel");
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
        SetAcceleratorKey(ok, acceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        return row;
    }

    private static string CreateAutomationName(string content) =>
        content.Replace("_", string.Empty, StringComparison.Ordinal);

    private static void SetAcceleratorKey(Button button, string content)
    {
        var accelerator = CreateAcceleratorKey(content);
        if (!string.IsNullOrEmpty(accelerator))
        {
            AutomationProperties.SetAcceleratorKey(button, accelerator);
        }
    }

    private static string CreateAcceleratorKey(string content)
    {
        for (var i = 0; i < content.Length - 1; i++)
        {
            if (content[i] == '_' && content[i + 1] != '_')
            {
                return "Alt+" + char.ToUpperInvariant(content[i + 1]);
            }
        }

        return string.Empty;
    }
}
