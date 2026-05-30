using System.Windows;
using FreeX.App.UI;

namespace FreeX.App.Host;

/// <summary>
/// Production WPF implementation of <see cref="IUserMessageService"/>.
/// Uses <see cref="MessageBox.Show"/> with the application main window as owner.
/// This is the only legitimate call site for MessageBox in the application.
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
            ResolveDefaultTitle(title, DefaultErrorTitle, UiText.ErrorTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowWarning(string message, string title = DefaultWarningTitle)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultWarningTitle, UiText.WarningTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public void ShowInfo(string message, string title = DefaultInformationTitle)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultInformationTitle, UiText.InformationTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public bool AskYesNo(string message, string title = DefaultConfirmTitle)
    {
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            ResolveDefaultTitle(title, DefaultConfirmTitle, UiText.ConfirmTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static string ResolveDefaultTitle(string title, string defaultTitle, string localizedTitle) =>
        string.Equals(title, defaultTitle, StringComparison.Ordinal)
            ? localizedTitle
            : title;
}
