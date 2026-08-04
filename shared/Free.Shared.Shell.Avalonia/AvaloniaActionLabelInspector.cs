using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Stable inspection values for a shared Avalonia action button.</summary>
public readonly record struct AvaloniaActionLabelSnapshot(
    string MnemonicText,
    string DisplayText,
    string AutomationName,
    string? AccessKey);

/// <summary>
/// Reads action-label semantics without coupling callers to whether the button uses a
/// string content presenter or an Avalonia <see cref="AccessText"/>.
/// </summary>
public static class AvaloniaActionLabelInspector
{
    public static AvaloniaActionLabelSnapshot Inspect(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        var mnemonicText = button.Content switch
        {
            AccessText accessText => accessText.Text ?? string.Empty,
            string text => text,
            _ => throw new InvalidOperationException("The action button content is not text.")
        };

        return new AvaloniaActionLabelSnapshot(
            mnemonicText,
            ShellStringText.NormalizeAccessText(mnemonicText),
            AutomationProperties.GetName(button) ?? string.Empty,
            AutomationProperties.GetAccessKey(button));
    }
}
