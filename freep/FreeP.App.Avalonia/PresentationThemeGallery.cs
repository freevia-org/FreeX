using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia realization of the PowerPoint-style Design theme thumbnail strip. Theme selection remains
/// in the shared ribbon registry; this class owns only the platform-native preview controls.
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
            Margin = new Thickness(2, 1, 2, 0),
        };

        var entries = BuiltInThemes.GetAll();
        if (state == RibbonAdaptiveGroupState.Full)
        {
            foreach (var entry in entries)
                strip.Children.Add(BuildThemeButton(entry, registry));
        }
        else
        {
            var activeName = activeThemeName?.Invoke();
            var active = entries.FirstOrDefault(entry => string.Equals(entry.DisplayName, activeName, StringComparison.OrdinalIgnoreCase))
                ?? entries[0];
            strip.Children.Add(BuildThemeButton(active, registry));
            strip.Children.Add(BuildMoreThemesButton(entries, registry));
        }
        return strip;
    }

    private static Control BuildMoreThemesButton(
        IReadOnlyList<BuiltInThemeEntry> entries,
        IRibbonCommandRegistry registry)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = "More\u2026", TextAlignment = TextAlignment.Center, FontSize = 10 },
            MinWidth = 28,
            Height = 48,
            Padding = new Thickness(4, 0),
        };
        ToolTip.SetTip(button, "More Themes");
        AutomationProperties.SetName(button, "More Themes");

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
        var preview = new Grid { Width = 82, Height = 50, Margin = new Thickness(3, 0) };
        preview.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        preview.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var page = new Border
        {
            Background = Brush(theme.ColorScheme[ThemeColorSlot.Lt1]),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0x9C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 3, 7, 4),
        };
        var previewContent = new Grid();
        previewContent.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        previewContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        previewContent.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontSize = 26,
            Foreground = Brush(theme.ColorScheme[ThemeColorSlot.Dk2]),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var slot in AccentSlots)
        {
            swatches.Children.Add(Bar(theme.ColorScheme[slot], 7, 4));
            swatches.Children.Add(new Border { Width = 1 });
        }
        Grid.SetRow(swatches, 1);
        previewContent.Children.Add(swatches);
        page.Child = previewContent;
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

        var button = new Button
        {
            Content = preview,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
        };
        ToolTip.SetTip(button, entry.DisplayName);
        AutomationProperties.SetName(button, entry.DisplayName);
        button.PointerEntered += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) => ExecuteTheme(entry, registry);
        return button;
    }

    private static void ExecuteTheme(BuiltInThemeEntry entry, IRibbonCommandRegistry registry)
    {
        var commandId = new RibbonCommandId("freep.theme." + entry.Id.ToLowerInvariant());
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }

    private static Border Bar(SrgbColor color, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = Brush(color),
    };

    private static IBrush Brush(SrgbColor color) =>
        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

    private static readonly ThemeColorSlot[] AccentSlots =
    [
        ThemeColorSlot.Accent1,
        ThemeColorSlot.Accent2,
        ThemeColorSlot.Accent3,
        ThemeColorSlot.Accent4,
        ThemeColorSlot.Accent5,
        ThemeColorSlot.Accent6,
    ];
}
