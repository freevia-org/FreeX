using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Free.Shared.Shell;

public static class DialogButtonRowFactory
{
    private const string DefaultOkContent = "_OK";

    public static StackPanel Create(
        Action accept,
        double buttonWidth,
        Thickness rowMargin = default,
        string acceptContent = DefaultOkContent,
        string? cancelContent = null)
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
            MinWidth = buttonWidth,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        AutomationProperties.SetName(ok, ShellStrings.Current.CreateAutomationName(resolvedAcceptContent));
        SetAcceleratorKey(ok, resolvedAcceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        var resolvedCancelContent = ResolveCancelContent(cancelContent);
        var cancel = new Button
        {
            Content = resolvedCancelContent,
            MinWidth = buttonWidth,
            IsCancel = true
        };
        AutomationProperties.SetName(cancel, ShellStrings.Current.CreateAutomationName(resolvedCancelContent));
        SetAcceleratorKey(cancel, resolvedCancelContent);
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
            MinWidth = buttonWidth,
            IsDefault = true,
            IsCancel = true
        };
        AutomationProperties.SetName(ok, ShellStrings.Current.CreateAutomationName(resolvedAcceptContent));
        SetAcceleratorKey(ok, resolvedAcceptContent);
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        return row;
    }

    public static StackPanel Create(Button acceptButton, Button cancelButton, Thickness rowMargin = default)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        acceptButton.Margin = new Thickness(0, 0, 8, 0);
        acceptButton.IsDefault = true;
        cancelButton.Margin = new Thickness();
        cancelButton.IsCancel = true;
        row.Children.Add(acceptButton);
        row.Children.Add(cancelButton);
        return row;
    }

    private static string ResolveDefaultAcceptContent(string acceptContent) =>
        string.Equals(acceptContent, DefaultOkContent, StringComparison.Ordinal)
            || string.Equals(acceptContent, "OK", StringComparison.Ordinal)
            ? ShellStrings.Current.Ok
            : acceptContent;

    private static string ResolveCancelContent(string? cancelContent) =>
        cancelContent is null
            || string.Equals(cancelContent, "Cancel", StringComparison.Ordinal)
            || string.Equals(cancelContent, "_Cancel", StringComparison.Ordinal)
            ? ShellStrings.Current.Cancel
            : cancelContent;

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
