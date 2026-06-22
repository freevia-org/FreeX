using System.Windows;
using System.Windows.Controls;

namespace Free.Shared.Ribbon.Wpf;

public sealed record SisterAppClientFrameSpec(
    UIElement Chrome,
    UIElement Body,
    UIElement Status);

public sealed record SisterAppClientFrameBuildResult(
    Grid Root);

/// <summary>
/// Builds the common sister-app client frame below the custom title bar: chrome, workarea, status bar.
/// </summary>
public static class SisterAppClientFrameBuilder
{
    public static SisterAppClientFrameBuildResult Build(SisterAppClientFrameSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Chrome);
        ArgumentNullException.ThrowIfNull(spec.Body);
        ArgumentNullException.ThrowIfNull(spec.Status);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(spec.Chrome, 0);
        root.Children.Add(spec.Chrome);

        Grid.SetRow(spec.Body, 1);
        root.Children.Add(spec.Body);

        Grid.SetRow(spec.Status, 2);
        root.Children.Add(spec.Status);

        return new SisterAppClientFrameBuildResult(root);
    }
}
