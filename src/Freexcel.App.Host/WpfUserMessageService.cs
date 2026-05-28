using System.Windows;
using Freexcel.App.UI;

namespace Freexcel.App.Host;

/// <summary>
/// Production WPF implementation of <see cref="IUserMessageService"/>.
/// Uses <see cref="MessageBox.Show"/> with the application main window as owner.
/// This is the only legitimate call site for MessageBox in the application.
/// </summary>
public sealed class WpfUserMessageService : IUserMessageService
{
    public void ShowError(string message, string title = "Error")
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowWarning(string message, string title = "Warning")
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public void ShowInfo(string message, string title = "Information")
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public bool AskYesNo(string message, string title = "Confirm")
    {
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}
