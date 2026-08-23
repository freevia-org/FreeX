using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaRecoveryPromptText(
    string Title,
    string RecoverButton,
    string SkipButton);

/// <summary>Realizes the common Avalonia recovery confirmation surface inside an app-owned window.</summary>
public static class AvaloniaRecoveryPromptDialogComposer
{
    public static void Compose(
        Window dialog,
        string message,
        AvaloniaRecoveryPromptText text,
        Action<bool> close)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(close);

        dialog.Title = text.Title;
        dialog.Width = 420;
        dialog.Height = 160;
        dialog.CanResize = false;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 20),
        };

        var style = AvaloniaCompactDialogChrome.WindowsStyle;
        var recover = new Button { Content = text.RecoverButton, MinWidth = 82, IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(recover, style, minWidth: 82, isDefault: true);
        recover.Click += (_, _) => close(true);

        var skip = new Button { Content = text.SkipButton, MinWidth = 82, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(skip, style, minWidth: 82);
        skip.Click += (_, _) => close(false);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [recover, skip],
            new Thickness(16, 0, 16, 16));
        dialog.Content = new StackPanel { Children = { messageBlock, buttons } };
    }
}
