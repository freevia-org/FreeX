using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Avalonia counterpart of the WPF <c>DialogButtonRowFactory</c>.
/// Keeping the action-row contract in the shared shell prevents individual dialogs from
/// drifting in button order, spacing, or default/cancel semantics.
/// </summary>
public static class AvaloniaDialogButtonRowFactory
{
    public static StackPanel CreateOkCancel(
        Action accept,
        Action cancel,
        double buttonWidth,
        Thickness rowMargin = default,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(cancel);
        style ??= AvaloniaCompactDialogChrome.WindowsStyle;

        var ok = CreateButton(Free.Shared.Shell.ShellStrings.Current.Ok, accept, buttonWidth, style, isDefault: true);
        var cancelButton = CreateButton(Free.Shared.Shell.ShellStrings.Current.Cancel, cancel, buttonWidth, style, isCancel: true);
        return CreateRow([ok, cancelButton], rowMargin, style);
    }

    public static StackPanel CreateRow(
        IReadOnlyList<Button> buttons,
        Thickness rowMargin = default,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        style ??= AvaloniaCompactDialogChrome.WindowsStyle;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = style.ActionSpacing,
            Margin = rowMargin,
        };
        foreach (var button in buttons)
            row.Children.Add(button);
        return row;
    }

    private static Button CreateButton(
        string content,
        Action action,
        double buttonWidth,
        AvaloniaCompactDialogChromeStyle style,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, style, buttonWidth, isDefault);
        AvaloniaDialogButtonContent.Apply(button, content);
        button.Click += (_, _) => action();
        return button;
    }
}
