using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

internal static class DialogButtonRowFactory
{
    private const string DefaultOkContent = "_OK";

    public static StackPanel Create(Action accept, double buttonWidth, Thickness rowMargin = default, string acceptContent = DefaultOkContent)
    {
        var resolvedAcceptContent = ResolveDefaultAcceptContent(acceptContent);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        var ok = new Button
        {
            Content = resolvedAcceptContent,
            Width = buttonWidth,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        AutomationProperties.SetName(ok, UiText.CreateAutomationName(resolvedAcceptContent));
        SetAcceleratorKey(ok, resolvedAcceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        var cancelContent = UiText.Cancel;
        var cancel = new Button
        {
            Content = cancelContent,
            Width = buttonWidth,
            IsCancel = true
        };
        AutomationProperties.SetName(cancel, UiText.CreateAutomationName(cancelContent));
        SetAcceleratorKey(cancel, cancelContent);
        row.Children.Add(cancel);
        return row;
    }

    public static StackPanel CreateOkOnly(Action accept, double buttonWidth, Thickness rowMargin = default, string acceptContent = DefaultOkContent)
    {
        var resolvedAcceptContent = ResolveDefaultAcceptContent(acceptContent);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        var ok = new Button
        {
            Content = resolvedAcceptContent,
            Width = buttonWidth,
            IsDefault = true,
            IsCancel = true
        };
        AutomationProperties.SetName(ok, UiText.CreateAutomationName(resolvedAcceptContent));
        SetAcceleratorKey(ok, resolvedAcceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        return row;
    }

    private static string ResolveDefaultAcceptContent(string acceptContent) =>
        string.Equals(acceptContent, DefaultOkContent, StringComparison.Ordinal)
            ? UiText.Ok
            : acceptContent;

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
