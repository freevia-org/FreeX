using System.Windows;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Ribbon-specific adapters for the shell Backstage frame.
/// </summary>
public static class BackstageRibbonChrome
{
    public static BackstageFrameChrome Create() => new(
        new Uri("/Free.Shared.Shell.Wpf;component/BackstageChromeResources.xaml", UriKind.Relative),
        CreateIcon,
        RibbonTooltip.SetKeyTip,
        RibbonTooltip.SetTitle,
        RibbonTooltip.SetDescription);

    private static FrameworkElement CreateIcon(BackstageIconSpec icon, double size, Brush brush) =>
        RibbonIconFactory.CreateCommandIcon(
            icon.CommandName ?? string.Empty,
            new RibbonCommandIcon(Map(icon.Kind)),
            size,
            brush);

    private static RibbonCommandIconKind Map(BackstageIconKind kind) =>
        kind switch
        {
            BackstageIconKind.Previous => RibbonCommandIconKind.Previous,
            BackstageIconKind.Grid => RibbonCommandIconKind.Grid,
            BackstageIconKind.Info => RibbonCommandIconKind.Info,
            BackstageIconKind.Insert => RibbonCommandIconKind.Insert,
            BackstageIconKind.GetData => RibbonCommandIconKind.GetData,
            BackstageIconKind.Share => RibbonCommandIconKind.Share,
            BackstageIconKind.Save => RibbonCommandIconKind.Save,
            BackstageIconKind.Print => RibbonCommandIconKind.Print,
            BackstageIconKind.View => RibbonCommandIconKind.View,
            BackstageIconKind.WindowClose => RibbonCommandIconKind.WindowClose,
            _ => RibbonCommandIconKind.Generic
        };
}
