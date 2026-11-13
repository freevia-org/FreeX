using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// WPF preview strip for the common Transitions commands. The full transition catalog remains
/// available through the adjacent More menu; this surface mirrors PowerPoint's visible first row.
/// The entry table, density ladder and thumbnail geometry live in <see cref="TransitionGalleryPlanner"/>.
/// </summary>
internal static class PresentationTransitionGallery
{
    public static FrameworkElement Build(
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

    private static FrameworkElement BuildButton(TransitionGalleryEntry entry, IRibbonCommandRegistry registry)
    {
        var label = UiText.Get(entry.LabelKey);
        var content = new Grid
        {
            Width = FreePRibbonGalleryMetrics.TransitionButtonWidth,
            Height = FreePRibbonGalleryMetrics.TransitionButtonHeight,
        };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            Margin = new Thickness(FreePRibbonGalleryMetrics.ButtonSideMargin, 0, FreePRibbonGalleryMetrics.ButtonSideMargin, 0),
            ToolTip = label,
        };
        AutomationProperties.SetName(button, label);
        button.MouseEnter += (_, _) =>
        {
            button.Background = Brush(FreePRibbonGalleryMetrics.HoverBackground);
            button.BorderBrush = Brush(FreePRibbonGalleryMetrics.HoverBorder);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) => FreePRibbonGalleryCommands.Execute(registry, new RibbonCommandId(entry.CommandId));
        return button;
    }

    private static FrameworkElement BuildPreview(TransitionPreviewKind preview)
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
            Margin = new Thickness(plan.HorizontalMargin, plan.VerticalMargin, plan.HorizontalMargin, plan.VerticalMargin),
            Opacity = plan.Opacity,
            HorizontalAlignment = Horizontal(plan.Horizontal),
            VerticalAlignment = Vertical(plan.Vertical),
            Visibility = plan.IncomingVisible ? Visibility.Visible : Visibility.Collapsed,
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
