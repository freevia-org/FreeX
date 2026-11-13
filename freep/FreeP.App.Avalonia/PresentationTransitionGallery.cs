using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia-native preview strip for the common PowerPoint transition commands. The entry table, density
/// ladder and thumbnail geometry live in <see cref="TransitionGalleryPlanner"/>.
/// </summary>
internal static class PresentationTransitionGallery
{
    public static Control Build(
        IRibbonCommandRegistry registry,
        RibbonAdaptiveGroupState state = RibbonAdaptiveGroupState.Full,
        double availableRibbonWidth = double.PositiveInfinity)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                FreePRibbonGalleryMetrics.StripMarginSide,
                FreePRibbonGalleryMetrics.StripMarginTop,
                FreePRibbonGalleryMetrics.StripMarginSide,
                0),
        };
        foreach (var entry in TransitionGalleryPlanner.VisibleEntries(state, availableRibbonWidth))
            strip.Children.Add(BuildButton(entry, registry));
        return strip;
    }

    private static Control BuildButton(TransitionGalleryEntry entry, IRibbonCommandRegistry registry)
    {
        var label = UiText.Get(entry.LabelKey);
        var content = new Grid
        {
            Width = FreePRibbonGalleryMetrics.TransitionButtonWidth,
            Height = FreePRibbonGalleryMetrics.TransitionButtonHeight,
        };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.Children.Add(BuildPreview(entry.Preview));
        var caption = new TextBlock
        {
            Text = label,
            FontSize = FreePRibbonGalleryMetrics.CaptionFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(
                FreePRibbonGalleryMetrics.CaptionMarginSide,
                FreePRibbonGalleryMetrics.CaptionMarginTop,
                FreePRibbonGalleryMetrics.CaptionMarginSide,
                0),
        };
        Grid.SetRow(caption, 1);
        content.Children.Add(caption);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(FreePRibbonGalleryMetrics.ButtonPadding),
            Margin = new Thickness(FreePRibbonGalleryMetrics.ButtonSideMargin, 0),
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.PointerEntered += (_, _) => SetHover(button, true);
        button.PointerExited += (_, _) => SetHover(button, false);
        button.Click += (_, _) => FreePRibbonGalleryCommands.Execute(registry, new RibbonCommandId(entry.CommandId));
        return button;
    }

    private static Control BuildPreview(TransitionPreviewKind preview)
    {
        var plan = TransitionGalleryPlanner.PreviewFor(preview);
        var frame = new Border
        {
            Width = FreePRibbonGalleryMetrics.TransitionPreviewWidth,
            Height = FreePRibbonGalleryMetrics.TransitionPreviewHeight,
            BorderBrush = Brush(FreePRibbonGalleryMetrics.ThumbnailBorder),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
        };
        var surface = new Grid { ClipToBounds = true };
        surface.Children.Add(new Rectangle { Fill = Brush(FreePRibbonGalleryMetrics.TransitionOutgoingColor) });
        var incoming = new Border
        {
            Background = Brush(FreePRibbonGalleryMetrics.TransitionIncomingColor),
            Margin = new Thickness(plan.HorizontalMargin, plan.VerticalMargin),
            Opacity = plan.Opacity,
            HorizontalAlignment = Horizontal(plan.Horizontal),
            VerticalAlignment = Vertical(plan.Vertical),
            IsVisible = plan.IncomingVisible,
        };
        if (plan.Width is { } width)
            incoming.Width = width;
        if (plan.Height is { } height)
            incoming.Height = height;

        foreach (var bar in plan.Bars)
        {
            surface.Children.Add(new Rectangle
            {
                Fill = Brush(FreePRibbonGalleryMetrics.TransitionIncomingColor),
                Width = bar.Width,
                Height = bar.Height,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(bar.LeftMargin, 0, 0, 0),
            });
        }

        surface.Children.Add(incoming);
        frame.Child = surface;
        return frame;
    }

    private static void SetHover(Button button, bool hovering)
    {
        button.Background = hovering ? Brush(FreePRibbonGalleryMetrics.HoverBackground) : Brushes.Transparent;
        button.BorderBrush = hovering ? Brush(FreePRibbonGalleryMetrics.HoverBorder) : Brushes.Transparent;
    }

    private static HorizontalAlignment Horizontal(RibbonGalleryPreviewAlignment alignment) => alignment switch
    {
        RibbonGalleryPreviewAlignment.Start => HorizontalAlignment.Left,
        RibbonGalleryPreviewAlignment.Center => HorizontalAlignment.Center,
        RibbonGalleryPreviewAlignment.End => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Stretch,
    };

    private static VerticalAlignment Vertical(RibbonGalleryPreviewAlignment alignment) => alignment switch
    {
        RibbonGalleryPreviewAlignment.Start => VerticalAlignment.Top,
        RibbonGalleryPreviewAlignment.Center => VerticalAlignment.Center,
        RibbonGalleryPreviewAlignment.End => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Stretch,
    };

    private static SolidColorBrush Brush(SrgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));
}
