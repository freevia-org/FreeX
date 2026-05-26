using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

internal static class BackstageProgressOverlayBinder
{
    public static void ShowOverlay(
        FrameworkElement? overlay,
        TextBlock titleText,
        TextBlock detailText,
        ProgressBar? progressBar,
        string title,
        string detail,
        double? percent)
    {
        if (overlay is null)
            return;

        titleText.Text = title;
        detailText.Text = detail;
        ApplyProgress(progressBar, percent);
        overlay.Visibility = Visibility.Visible;
        overlay.UpdateLayout();
    }

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

        statusText.Text = $"{title}: {detail}";
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
