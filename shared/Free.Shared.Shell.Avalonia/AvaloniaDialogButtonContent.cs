using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Free.Shared.Shell;

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

        button.Content = ShellStringText.RequiresAccessText(text)
            ? new AccessText { Text = text }
            : text;
        AutomationProperties.SetName(button, ShellStrings.Current.CreateAutomationName(text));

        var accelerator = ShellStringText.CreateAcceleratorKey(text);
        if (accelerator.Length > 0)
            AutomationProperties.SetAccessKey(button, accelerator);
    }
}
