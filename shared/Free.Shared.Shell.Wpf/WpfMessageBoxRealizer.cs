using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Test/headless seam for shared WPF message boxes. When <see cref="Handler"/> is set, the shared
/// realizer returns its result instead of calling the blocking <see cref="MessageBox"/>. Production
/// leaves this null so real dialogs show; test hosts install a handler so prompts (e.g. the
/// Save-on-exit confirmation fired from a window's Closing handler) never deadlock the STA thread.
/// </summary>
public static class HeadlessMessageBox
{
    /// <summary>
    /// Non-interactive responder: given the message text and the button set, returns the result the
    /// dialog would have produced. Null (default) = show the real modal dialog.
    /// </summary>
    public static Func<string?, UserMessageButtons, UserMessageResult>? Handler { get; set; }
}

/// <summary>
/// Single WPF renderer for shared message dialog abstractions.
/// </summary>
internal static class WpfMessageBoxRealizer
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    public static UserMessageResult Show(
        Window? owner,
        string? message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon)
    {
        // Test/headless override — answer without showing a blocking modal (avoids STA-teardown deadlocks).
        if (HeadlessMessageBox.Handler is { } handler)
            return handler(message, buttons);

        var result = MessageBox.Show(
            owner,
            message ?? string.Empty,
            ResolveKnownDefaultTitle(title),
            ToMessageBoxButton(buttons),
            ToMessageBoxImage(icon));

        return ToUserMessageResult(result);
    }

    private static MessageBoxButton ToMessageBoxButton(UserMessageButtons buttons) =>
        buttons switch
        {
            UserMessageButtons.Ok => MessageBoxButton.OK,
            UserMessageButtons.OkCancel => MessageBoxButton.OKCancel,
            UserMessageButtons.YesNo => MessageBoxButton.YesNo,
            UserMessageButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null)
        };

    private static MessageBoxImage ToMessageBoxImage(UserMessageIcon icon) =>
        icon switch
        {
            UserMessageIcon.None => MessageBoxImage.None,
            UserMessageIcon.Information => MessageBoxImage.Information,
            UserMessageIcon.Warning => MessageBoxImage.Warning,
            UserMessageIcon.Error => MessageBoxImage.Error,
            UserMessageIcon.Question => MessageBoxImage.Question,
            _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null)
        };

    private static UserMessageResult ToUserMessageResult(MessageBoxResult result) =>
        result switch
        {
            MessageBoxResult.OK => UserMessageResult.Ok,
            MessageBoxResult.Cancel => UserMessageResult.Cancel,
            MessageBoxResult.Yes => UserMessageResult.Yes,
            MessageBoxResult.No => UserMessageResult.No,
            _ => UserMessageResult.None
        };

    private static string ResolveKnownDefaultTitle(string title)
    {
        title = ResolveDefaultTitle(title, DefaultErrorTitle, ShellStrings.Current.ErrorTitle);
        title = ResolveDefaultTitle(title, DefaultWarningTitle, ShellStrings.Current.WarningTitle);
        title = ResolveDefaultTitle(title, DefaultInformationTitle, ShellStrings.Current.InformationTitle);
        title = ResolveDefaultTitle(title, DefaultConfirmTitle, ShellStrings.Current.ConfirmTitle);
        return title;
    }

    private static string ResolveDefaultTitle(string title, string defaultTitle, string localizedTitle) =>
        string.Equals(title, defaultTitle, StringComparison.Ordinal)
            ? localizedTitle
            : title;
}
