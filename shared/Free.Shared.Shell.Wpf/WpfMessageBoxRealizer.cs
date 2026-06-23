using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

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
