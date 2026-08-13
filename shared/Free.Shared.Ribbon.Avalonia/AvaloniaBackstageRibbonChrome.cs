using Avalonia.Controls;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Shared ribbon-backed icon chrome for the Avalonia Backstage frame.
/// </summary>
public static class AvaloniaBackstageRibbonChrome
{
    /// <summary>
    /// Creates Backstage frame chrome. The close glyph is explicit because sister apps use the
    /// Delete artwork while FreeX uses the dedicated WindowClose artwork.
    /// </summary>
    public static AvaloniaBackstageFrameChrome Create(RibbonCommandIconKind windowCloseIcon) =>
        new((kind, commandName, size, foreground) =>
            CreateIcon(kind, commandName, size, foreground, windowCloseIcon));

    public static RibbonCommandIconKind ResolveIconKind(
        BackstageIconKind kind,
        RibbonCommandIconKind windowCloseIcon) => kind switch
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
        BackstageIconKind.WindowClose => windowCloseIcon,
        _ => RibbonCommandIconKind.Generic,
    };

    private static Control CreateIcon(
        BackstageIconKind kind,
        string? commandName,
        double size,
        IBrush foreground,
        RibbonCommandIconKind windowCloseIcon) =>
        AvaloniaRibbonIcons.BuildMonochrome(
            ResolveIconKind(kind, windowCloseIcon),
            size,
            commandName,
            foreground);
}
