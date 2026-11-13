using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia realization of the PowerPoint-style Design theme thumbnail strip. Theme selection is planned by
/// <see cref="ThemeGalleryPlanner"/> and dispatched through the shared ribbon registry; this class owns only
/// the platform-native preview controls.
/// </summary>
internal static class PresentationThemeGallery
{
    public static Control Build(
        IRibbonCommandRegistry registry,
        RibbonAdaptiveGroupState state = RibbonAdaptiveGroupState.Full,
        Func<string>? activeThemeName = null)
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

        var entries = BuiltInThemes.GetAll();
        var plan = ThemeGalleryPlanner.Plan(entries, state, activeThemeName?.Invoke());
        foreach (var entry in plan.Thumbnails)
            strip.Children.Add(BuildThemeButton(entry, registry));
        if (plan.ShowMore)
            strip.Children.Add(BuildMoreThemesButton(entries, registry));
        return strip;
    }

    private static Control BuildMoreThemesButton(
        IReadOnlyList<BuiltInThemeEntry> entries,
        IRibbonCommandRegistry registry)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = FreePRibbonGalleryMetrics.ThemeMoreText,
                TextAlignment = TextAlignment.Center,
                FontSize = FreePRibbonGalleryMetrics.AvaloniaThemeMoreFontSize,
            },
            MinWidth = FreePRibbonGalleryMetrics.ThemeMoreMinWidth,
            Height = FreePRibbonGalleryMetrics.ThemeMoreHeight,
            Padding = new Thickness(FreePRibbonGalleryMetrics.ThemeMorePaddingSide, 0),
        };
        ToolTip.SetTip(button, FreePRibbonGalleryMetrics.ThemeMoreLabel);
        AutomationProperties.SetName(button, FreePRibbonGalleryMetrics.ThemeMoreLabel);

        var flyout = new MenuFlyout();
        foreach (var entry in entries)
        {
            var captured = entry;
            var item = new MenuItem { Header = captured.DisplayName };
            AutomationProperties.SetName(item, captured.DisplayName);
            item.Click += (_, _) => ExecuteTheme(captured, registry);
            flyout.Items.Add(item);
        }

        button.Flyout = flyout;
        return button;
    }

    private static Control BuildThemeButton(BuiltInThemeEntry entry, IRibbonCommandRegistry registry)
    {
        var theme = entry.Theme;
        var preview = new Grid
        {
            Width = FreePRibbonGalleryMetrics.ThemePreviewWidth,
            Height = FreePRibbonGalleryMetrics.ThemePreviewHeight,
            Margin = new Thickness(FreePRibbonGalleryMetrics.ThemePreviewSideMargin, 0),
        };
        preview.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        preview.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var page = new Border
        {
            Background = Brush(theme.ColorScheme[ThemeColorSlot.Lt1]),
            BorderBrush = Brush(FreePRibbonGalleryMetrics.ThumbnailBorder),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(
                FreePRibbonGalleryMetrics.ThemePagePaddingSide,
                FreePRibbonGalleryMetrics.ThemePagePaddingTop,
                FreePRibbonGalleryMetrics.ThemePagePaddingSide,
                FreePRibbonGalleryMetrics.ThemePagePaddingBottom),
        };
        var previewContent = new Grid();
        previewContent.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        previewContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        previewContent.Children.Add(new TextBlock
        {
            Text = FreePRibbonGalleryMetrics.ThemeSampleText,
            FontSize = FreePRibbonGalleryMetrics.ThemeSampleFontSize,
            Foreground = Brush(theme.ColorScheme[ThemeColorSlot.Dk2]),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var slot in ThemeGalleryPlanner.AccentSlots)
        {
            swatches.Children.Add(Bar(theme.ColorScheme[slot], FreePRibbonGalleryMetrics.ThemeSwatchWidth, FreePRibbonGalleryMetrics.ThemeSwatchHeight));
            swatches.Children.Add(new Border { Width = FreePRibbonGalleryMetrics.ThemeSwatchGap });
        }
        Grid.SetRow(swatches, 1);
        previewContent.Children.Add(swatches);
        page.Child = previewContent;
        preview.Children.Add(page);

        var caption = new TextBlock
        {
            Text = entry.DisplayName,
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
        preview.Children.Add(caption);

        var button = new Button
        {
            Content = preview,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(FreePRibbonGalleryMetrics.ButtonPadding),
        };
        ToolTip.SetTip(button, entry.DisplayName);
        AutomationProperties.SetName(button, entry.DisplayName);
        button.PointerEntered += (_, _) =>
        {
            button.Background = Brush(FreePRibbonGalleryMetrics.HoverBackground);
            button.BorderBrush = Brush(FreePRibbonGalleryMetrics.HoverBorder);
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) => ExecuteTheme(entry, registry);
        return button;
    }

    private static void ExecuteTheme(BuiltInThemeEntry entry, IRibbonCommandRegistry registry) =>
        FreePRibbonGalleryCommands.Execute(registry, ThemeGalleryPlanner.CommandIdFor(entry));

    private static Border Bar(SrgbColor color, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = Brush(color),
    };

    private static SolidColorBrush Brush(SrgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));
}
