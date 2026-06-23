using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Production WPF implementation of <see cref="IUserMessageService"/>.
/// </summary>
public sealed class WpfUserMessageService : IUserMessageService
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    public void ShowError(string message, string title = DefaultErrorTitle)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultErrorTitle, ShellStrings.Current.ErrorTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowWarning(string message, string title = DefaultWarningTitle)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultWarningTitle, ShellStrings.Current.WarningTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public void ShowInfo(string message, string title = DefaultInformationTitle)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultInformationTitle, ShellStrings.Current.InformationTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public bool AskYesNo(string message, string title = DefaultConfirmTitle)
    {
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultConfirmTitle, ShellStrings.Current.ConfirmTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static string ResolveDefaultTitle(string title, string defaultTitle, string localizedTitle) =>
        string.Equals(title, defaultTitle, StringComparison.Ordinal)
            ? localizedTitle
            : title;
}
