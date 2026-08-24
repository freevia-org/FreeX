using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Compact PowerPoint-style theme thumbnails for the Design ribbon. The catalog remains defined by
/// <see cref="BuiltInThemes"/> and activation continues through the shared command registry; this type
/// owns only the WPF preview surface.
/// </summary>
internal static class PresentationThemeGallery
{
    public static FrameworkElement Build(IRibbonCommandRegistry registry)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };

        foreach (var entry in BuiltInThemes.GetAll())
            strip.Children.Add(BuildThemeButton(entry, registry));

        return strip;
    }

    private static FrameworkElement BuildThemeButton(BuiltInThemeEntry entry, IRibbonCommandRegistry registry)
    {
        var theme = entry.Theme;
        var preview = new Grid { Width = 82, Height = 50, Margin = new Thickness(3, 0, 3, 0) };
        preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var page = new Border
        {
            Background = Brush(theme.ColorScheme[ThemeColorSlot.Lt1]),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0x9C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 5, 7, 4),
        };
        var marks = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        marks.Children.Add(Bar(theme.ColorScheme[ThemeColorSlot.Accent1], 31, 4));
        marks.Children.Add(new Border { Height = 3 });
        marks.Children.Add(Bar(theme.ColorScheme[ThemeColorSlot.Dk2], 48, 2));
        marks.Children.Add(new Border { Height = 2 });
        marks.Children.Add(Bar(theme.ColorScheme[ThemeColorSlot.Accent2], 39, 2));
        page.Child = marks;
        preview.Children.Add(page);

        var caption = new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(1, 1, 1, 0),
        };
        Grid.SetRow(caption, 1);
        preview.Children.Add(caption);

        var commandId = new RibbonCommandId("freep.theme." + entry.Id.ToLowerInvariant());
        var button = new Button
        {
            Content = preview,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            ToolTip = entry.DisplayName,
        };
        AutomationProperties.SetName(button, entry.DisplayName);
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) =>
        {
            if (registry.TryGet(commandId, out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
        };
        return button;
    }

    private static Border Bar(SrgbColor color, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = Brush(color),
    };

    private static SolidColorBrush Brush(SrgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));
}
