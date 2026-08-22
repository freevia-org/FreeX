using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BrandTheme = Free.Shared.Theme.Theme;

namespace Free.Shared.Shell.Wpf;

/// <summary>Applies theme-selected application artwork to WPF windows and title-bar badges.</summary>
public static class WpfWindowIconLoader
{
    public static bool TryApply(Window window, BrandTheme theme, string assemblyName, Image? badge = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(theme);

        try
        {
            var frame = BitmapFrame.Create(new Uri(
                theme.VisualAssets.GetWpfPackUri(assemblyName),
                UriKind.Absolute));
            frame.Freeze();
            window.Icon = frame;
            if (badge is not null)
                badge.Source = frame;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
