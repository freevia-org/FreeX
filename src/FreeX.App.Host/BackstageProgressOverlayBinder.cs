using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host;

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

        statusText.Text = string.IsNullOrEmpty(title) ? detail : $"{title}: {detail}";
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

        progressBar.IsIndeterminate = !percent.HasValue;
        if (percent.HasValue)
            progressBar.Value = Math.Clamp(percent.Value, progressBar.Minimum, progressBar.Maximum);
    }
}
