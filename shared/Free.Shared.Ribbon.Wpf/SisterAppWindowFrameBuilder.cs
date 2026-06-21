using System.Windows;
using System.Windows.Controls;

namespace Free.Shared.Ribbon.Wpf;

public sealed record SisterAppWindowFrameSpec(
    UIElement TitleBar,
    UIElement Body,
    UIElement Backstage);

public sealed record SisterAppWindowFrameBuildResult(
    Grid Root,
    Grid BelowTitle);

/// <summary>
/// Builds the common WPF sister-app frame: title bar in the top row, and the app body with Backstage
/// overlay in the row below. Apps keep their workarea/ribbon/status construction; this owns only the shell.
/// </summary>
public static class SisterAppWindowFrameBuilder
{
    public static SisterAppWindowFrameBuildResult Build(SisterAppWindowFrameSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.TitleBar);
        ArgumentNullException.ThrowIfNull(spec.Body);
        ArgumentNullException.ThrowIfNull(spec.Backstage);

        var belowTitle = new Grid();
        belowTitle.Children.Add(spec.Body);
        belowTitle.Children.Add(spec.Backstage);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(spec.TitleBar, 0);
        root.Children.Add(spec.TitleBar);
        Grid.SetRow(belowTitle, 1);
        root.Children.Add(belowTitle);

        return new SisterAppWindowFrameBuildResult(root, belowTitle);
    }
}
