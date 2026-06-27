using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAppStatusBarSpec(
    IBrush Background,
    Control LeftContent,
    IReadOnlyList<Control>? RightItems = null,
    double Height = 26,
    IBrush? BorderBrush = null,
    Thickness BorderThickness = default,
    Control? CenterContent = null,
    Thickness Padding = default);

public sealed record SisterAppStatusBarBuildResult(
    Border Root,
    DockPanel Layout);

/// <summary>
/// Builds shared Avalonia status-bar chrome for the simpler sister-app shells.
/// </summary>
public static class SisterAppStatusBarChrome
{
    public static SisterAppStatusBarBuildResult Build(SisterAppStatusBarSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Background);
        ArgumentNullException.ThrowIfNull(spec.LeftContent);

        var layout = new DockPanel
        {
            LastChildFill = true,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };

        foreach (var item in spec.RightItems ?? [])
        {
            ArgumentNullException.ThrowIfNull(item);
            DockPanel.SetDock(item, Dock.Right);
            layout.Children.Add(item);
        }

        if (spec.CenterContent is { } centerContent)
        {
            DockPanel.SetDock(spec.LeftContent, Dock.Left);
            layout.Children.Add(spec.LeftContent);
            layout.Children.Add(centerContent);
        }
        else
        {
            layout.Children.Add(spec.LeftContent);
        }

        var root = new Border
        {
            Background = spec.Background,
            BorderBrush = spec.BorderBrush,
            BorderThickness = spec.BorderThickness,
            Height = spec.Height,
            Padding = spec.Padding,
            Child = layout,
        };

        return new SisterAppStatusBarBuildResult(root, layout);
    }
}
