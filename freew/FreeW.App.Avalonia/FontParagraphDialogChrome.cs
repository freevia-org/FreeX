using Avalonia;
using Avalonia.Controls;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

/// <summary>
/// Route-specific checkbox inset for the Font and Paragraph dialog family.
/// Shared compact chrome owns all control templates and state painting.
/// </summary>
internal static class FontParagraphDialogChrome
{
    private const string CheckBoxClass = "freew-font-paragraph-checkbox";

    public static void ApplyCheckBox(CheckBox checkBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(style);

        if (checkBox.Classes.Contains(CheckBoxClass))
            return;

        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, style, contentSpacing: 5);
        checkBox.Classes.Add(CheckBoxClass);
        checkBox.Margin = new Thickness(
            checkBox.Margin.Left + 1,
            checkBox.Margin.Top,
            checkBox.Margin.Right,
            checkBox.Margin.Bottom);
    }
}
