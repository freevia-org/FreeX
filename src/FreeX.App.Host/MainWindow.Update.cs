using System.Windows;
using FreeX.App.Services.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private string? _stagedUpdateVersion;

    /// <summary>Reveal the discreet status-bar indicator. Safe to call only on the UI thread.</summary>
    public void ShowUpdateReady(string? version)
    {
        _stagedUpdateVersion = version;
        if (UpdateReadyIndicator is not null)
            UpdateReadyIndicator.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Tell the user no update is available. Lives here (not in a command partial) so the
    /// status-bar/update UI keeps its own lightweight MessageBox confirmations together.
    /// </summary>
    private void ShowUpToDate()
    {
        MessageBox.Show("You're up to date.", "FreeX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateReadyIndicator_Click(object sender, RoutedEventArgs e)
    {
        var updates = App.Services.GetService<IUpdateService>();
        if (updates is null) return;

        var versionText = string.IsNullOrWhiteSpace(_stagedUpdateVersion) ? "" : $" {_stagedUpdateVersion}";
        var choice = MessageBox.Show(
            $"FreeX{versionText} is ready to install. Restart now to update?",
            "Update FreeX",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (choice == MessageBoxResult.OK)
            updates.ApplyAndRestart();
    }
}
