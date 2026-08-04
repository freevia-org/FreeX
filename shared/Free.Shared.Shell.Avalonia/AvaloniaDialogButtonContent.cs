using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Adapts WPF mnemonic-bearing button text to Avalonia's access-key control.
/// WPF dialog templates recognize underscores in string content; Avalonia's default
/// Fluent button template does not, so passing the string directly can display the
/// marker literally and leaves the access key unregistered.
/// </summary>
internal static class AvaloniaDialogButtonContent
{
    public static void Apply(Button button, string text)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(text);

        button.Content = text.IndexOf('_') >= 0
            ? new AccessText { Text = text }
            : text;
        AutomationProperties.SetName(button, ShellStrings.Current.CreateAutomationName(text));

        if (TryFindAccessKey(text, out var accessKey))
            AutomationProperties.SetAccessKey(button, $"Alt+{accessKey}");
    }

    private static bool TryFindAccessKey(string text, out char accessKey)
    {
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (text[index] != '_')
                continue;

            if (text[index + 1] == '_')
            {
                index++;
                continue;
            }

            accessKey = char.ToUpperInvariant(text[index + 1]);
            return true;
        }

        accessKey = default;
        return false;
    }
}
