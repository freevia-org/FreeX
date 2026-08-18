using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds the compact Excel-style ribbon color gallery from the same portable palette plan used by
/// the WPF color picker. Only the controls are platform-specific; colors, theme shades, ordering,
/// recent-color behavior, and hex labels remain shared.
/// </summary>
internal static class RibbonColorPaletteFlyout
{
    private const double SwatchSize = 20;
    private const double SwatchGap = 2;

    public static Flyout Create(
        WorkbookTheme theme,
        RecentColorsStore recentColors,
        string topActionLabel,
        Action topAction,
        Action<CellColor, WorkbookThemeColorReference?> applyColor,
        string moreColorsLabel,
        Func<Task<CellColor?>> showMoreColorsAsync)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(recentColors);
        ArgumentException.ThrowIfNullOrWhiteSpace(topActionLabel);
        ArgumentNullException.ThrowIfNull(topAction);
        ArgumentNullException.ThrowIfNull(applyColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(moreColorsLabel);
        ArgumentNullException.ThrowIfNull(showMoreColorsAsync);

        Flyout? flyout = null;

        // R142-services-theme-colors-1: themeColor is non-null only for an Accent1-6 theme swatch
        // (see CellColorPalettePlanner.ThemeAccentColumn) -- Standard/Recent/Custom swatches and
        // "More Colors..." always pass null, so they keep applying a flat color exactly as before.
        void Apply(CellColor color, WorkbookThemeColorReference? themeColor)
        {
            recentColors.Remember(color);
            applyColor(color, themeColor);
            flyout?.Hide();
        }

        var plan = CellColorPalettePlanner.BuildMenuPlan(
            recentColors.Colors,
            recentColors.Capacity,
            includeCustomSpectrum: false,
            theme);
        var themeSection = plan.Sections.Single(section => section.Kind == CellColorPaletteSectionKind.Theme);
        var standardSection = plan.Sections.Single(section => section.Kind == CellColorPaletteSectionKind.Standard);
        var recentSection = plan.Sections.FirstOrDefault(section => section.Kind == CellColorPaletteSectionKind.Recent);

        var root = new StackPanel
        {
            Width = 238,
            Spacing = 5,
        };

        root.Children.Add(CreateTextAction(topActionLabel, () =>
        {
            topAction();
            flyout?.Hide();
        }, "RibbonColorPaletteTopAction"));
        root.Children.Add(CreateSeparator());
        root.Children.Add(CreateSectionLabel("Theme Colors"));
        root.Children.Add(CreateThemeGrid(themeSection.ThemeColumns, Apply));
        root.Children.Add(CreateSectionLabel("Standard Colors", new Thickness(0, 3, 0, 0)));
        root.Children.Add(CreateSwatchRow(standardSection.Swatches, Apply, "RibbonStandardColor"));

        if (recentSection is { Swatches.Count: > 0 })
        {
            root.Children.Add(CreateSectionLabel("Recent Colors", new Thickness(0, 3, 0, 0)));
            root.Children.Add(CreateSwatchRow(recentSection.Swatches, Apply, "RibbonRecentColor"));
        }

        root.Children.Add(CreateSeparator());
        var moreColors = CreateTextAction(moreColorsLabel, async () =>
        {
            flyout?.Hide();
            if (await showMoreColorsAsync() is { } selected)
                Apply(selected, themeColor: null);
        }, "RibbonColorPaletteMoreColors");
        root.Children.Add(moreColors);

        flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6),
                Child = root,
            },
        };
        return flyout;
    }

    private static Grid CreateThemeGrid(
        IReadOnlyList<CellColorThemeColumn> columns,
        Action<CellColor, WorkbookThemeColorReference?> applyColor)
    {
        var grid = new Grid
        {
            ColumnSpacing = SwatchGap,
            RowSpacing = SwatchGap,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        for (var col = 0; col < columns.Count; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(SwatchSize, GridUnitType.Pixel));
        var rowCount = columns.Count == 0 ? 0 : columns.Max(column => column.Shades.Count);
        for (var row = 0; row < rowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition(SwatchSize, GridUnitType.Pixel));

        for (var col = 0; col < columns.Count; col++)
        {
            var column = columns[col];
            for (var row = 0; row < column.Shades.Count; row++)
            {
                var swatch = CreateSwatchButton(
                    column.Shades[row],
                    applyColor,
                    $"RibbonThemeColor{col}_{row}",
                    $"{column.Name}, {column.Shades[row].Hex}");
                Grid.SetColumn(swatch, col);
                Grid.SetRow(swatch, row);
                grid.Children.Add(swatch);
            }
        }

        return grid;
    }

    private static Grid CreateSwatchRow(
        IReadOnlyList<CellColorSwatch> swatches,
        Action<CellColor, WorkbookThemeColorReference?> applyColor,
        string automationIdPrefix)
    {
        var grid = new Grid
        {
            ColumnSpacing = SwatchGap,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        for (var i = 0; i < swatches.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(SwatchSize, GridUnitType.Pixel));
            var button = CreateSwatchButton(swatches[i], applyColor, $"{automationIdPrefix}{i}", swatches[i].Hex);
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
        }

        return grid;
    }

    private static Button CreateSwatchButton(
        CellColorSwatch swatch,
        Action<CellColor, WorkbookThemeColorReference?> applyColor,
        string automationId,
        string accessibleName)
    {
        var color = swatch.Color;
        var themeColor = swatch.ThemeColor;
        var button = new Button
        {
            Width = SwatchSize,
            Height = SwatchSize,
            MinWidth = SwatchSize,
            MinHeight = SwatchSize,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, accessibleName);
        ToolTip.SetTip(button, accessibleName);
        button.Click += (_, _) => applyColor(color, themeColor);
        return button;
    }

    private static Button CreateTextAction(string label, Action action, string automationId)
    {
        var button = new Button
        {
            Content = label,
            Height = 26,
            Padding = new Thickness(5, 2),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            FontSize = 12,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock CreateSectionLabel(string text, Thickness? margin = null) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = margin ?? new Thickness(0),
        };

    private static Border CreateSeparator() =>
        new()
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)),
            Margin = new Thickness(-8, 1),
        };
}
