using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;

namespace FreeX.App.Host;

/// <summary>
/// Thin WPF binding for the backstage progress overlay. The decision logic (status-line
/// composition, indeterminate/determinate, value clamping) lives in the neutral
/// <see cref="BackstageProgressOverlayPlanner"/>; this type only applies the resulting state
/// to WPF controls so an Avalonia/FreeW overlay can reuse the same planner.
/// </summary>
internal static class BackstageProgressOverlayBinder
{
    public static void ShowStatusPanel(
        FrameworkElement? panel,
        TextBlock statusText,
        ProgressBar? progressBar,
        string title,
        string detail,
        double? percent)
    {
        if (panel is null)
            return;

        statusText.Text = BackstageProgressOverlayPlanner.FormatStatusText(title, detail);
        ApplyProgress(progressBar, percent);
        panel.Visibility = Visibility.Visible;
    }

    public static void Hide(FrameworkElement? element)
    {
        if (element is not null)
            element.Visibility = Visibility.Collapsed;
    }

    private static void ApplyProgress(ProgressBar? progressBar, double? percent)
    {
        if (progressBar is null)
            return;

        var state = BackstageProgressOverlayPlanner.Plan(
            title: string.Empty,
            detail: string.Empty,
            percent,
            progressBar.Minimum,
            progressBar.Maximum);

        progressBar.IsIndeterminate = state.IsIndeterminate;
        if (!state.IsIndeterminate)
            progressBar.Value = state.Value;
    }
}
