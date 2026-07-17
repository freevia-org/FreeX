using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAppWindowFrameSpec(
    Window Window,
    Control Body,
    IBrush TitleBarBackground,
    IBrush TitleBarForeground,
    double TitleBarHeight = 34,
    double NativeLeadingInset = 34,
    double NativeTrailingInset = 140);

public sealed record SisterAppWindowFrameBuildResult(
    Grid Root,
    Border TitleBar,
    StackPanel QatHost,
    TextBlock TitleText);

/// <summary>
/// Builds the shared Avalonia outer window frame. The operating system keeps its normal icon and
/// caption buttons; the client extension contributes the draggable title surface and interactive QAT.
/// </summary>
public static class SisterAppWindowFrameBuilder
{
    public static SisterAppWindowFrameBuildResult Build(SisterAppWindowFrameSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Window);
        ArgumentNullException.ThrowIfNull(spec.Body);
        ArgumentNullException.ThrowIfNull(spec.TitleBarBackground);
        ArgumentNullException.ThrowIfNull(spec.TitleBarForeground);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.TitleBarHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.NativeLeadingInset);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.NativeTrailingInset);

        spec.Window.ExtendClientAreaToDecorationsHint = true;
        spec.Window.ExtendClientAreaTitleBarHeightHint = spec.TitleBarHeight;

        var titleText = new TextBlock
        {
            Foreground = spec.TitleBarForeground,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
            Margin = new Thickness(spec.NativeTrailingInset, 0),
        };
        AutomationProperties.SetAutomationId(titleText, "SisterAppTitleText");
        titleText.Bind(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = spec.Window });

        var qatHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(spec.NativeLeadingInset, 0, spec.NativeTrailingInset, 0),
        };
        AutomationProperties.SetAutomationId(qatHost, "TitleBarQuickAccessToolbarHost");
        AutomationProperties.SetName(qatHost, "Quick Access Toolbar");
        WindowDecorationProperties.SetElementRole(qatHost, WindowDecorationsElementRole.User);

        var titleSurface = new Grid();
        titleSurface.Children.Add(titleText);
        titleSurface.Children.Add(qatHost);

        var titleBar = new Border
        {
            Height = spec.TitleBarHeight,
            Background = spec.TitleBarBackground,
            Child = titleSurface,
        };
        AutomationProperties.SetAutomationId(titleBar, "SisterAppTitleBar");
        WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.TitleBar);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);
        Grid.SetRow(spec.Body, 1);
        root.Children.Add(spec.Body);

        return new SisterAppWindowFrameBuildResult(root, titleBar, qatHost, titleText);
    }
}
