using System.Windows;
using FreeX.App.Presentation.Shell;
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
        ShowOwnedMessage(
            UiText.Get("MainWindowMessage_UpToDate"),
            UiText.Get("MainWindowMessage_CheckForUpdatesTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateReadyIndicator_Click(object sender, RoutedEventArgs e)
    {
        var updates = App.Services.GetService<IUpdateService>();
        if (updates is null) return;

        var plan = FreeXSynchronousPromptCatalog.ForUpdateReady(_stagedUpdateVersion);
        if (plan.ShouldApply(ShowOwnedSynchronousPrompt(plan.Confirmation)))
            updates.ApplyAndRestart();
    }
}
