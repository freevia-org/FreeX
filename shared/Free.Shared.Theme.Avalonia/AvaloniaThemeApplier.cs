using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Avalonia;

/// <summary>Converts a portable <see cref="Theme"/> into Avalonia resources.</summary>
public static class AvaloniaThemeApplier
{
    /// <summary>Converts a portable <see cref="ThemeColor"/> to an Avalonia <see cref="Color"/>.</summary>
    public static Color ToColor(ThemeColor color) =>
        Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Converts a portable <see cref="ThemeFontWeight"/> to an Avalonia <see cref="FontWeight"/>.</summary>
    public static FontWeight ToFontWeight(ThemeFontWeight weight) => weight switch
    {
        ThemeFontWeight.Bold => FontWeight.Bold,
        ThemeFontWeight.SemiBold => FontWeight.SemiBold,
        _ => FontWeight.Normal,
    };

    /// <summary>
    /// Materializes the portable theme resource plan as Avalonia brushes, colors, metrics,
    /// and typography.
    /// </summary>
    public static ResourceDictionary BuildResources(Theme theme, string keyPrefix)
    {
        var plan = ThemeResourcePlan.Create(theme, keyPrefix);
        var resources = new ResourceDictionary();

        foreach (var color in plan.Colors)
        {
            var nativeColor = ToColor(color.Value);
            resources[color.BrushKey] = new ImmutableSolidColorBrush(nativeColor);
            resources[color.ColorKey] = nativeColor;
        }

        foreach (var metric in plan.Metrics)
            resources[metric.Key] = metric.Value;

        foreach (var typography in plan.Typography)
        {
            var token = typography.Value;
            var fontFamily = string.IsNullOrEmpty(token.FontFamily)
                ? FontFamily.Default
                : new FontFamily(token.FontFamily);
            resources[typography.FontFamilyKey] = fontFamily;
            resources[typography.FontSizeKey] = token.SizePt;
            resources[typography.FontWeightKey] = ToFontWeight(token.Weight);
        }

        var colors = theme.Colors;
        AddAliasBrush(resources, "ThemeNeutralTextBrush", colors.Text);
        AddAliasBrush(resources, "ThemeNeutralMutedTextBrush", colors.MutedText);
        AddAliasBrush(resources, "ThemeNeutralWhiteBrush", colors.White);
        AddAliasBrush(resources, "ThemeNeutralDangerBrush", colors.Danger);
        AddAliasBrush(resources, "ThemeNeutralSheetSurfaceBrush", colors.SheetSurface);
        AddAliasBrush(resources, "ThemeAccentBrush", colors.Accent);
        AddAliasBrush(resources, "ThemeAccentDarkBrush", colors.AccentDark);
        AddAliasBrush(resources, "ThemeAccentSoftBrush", colors.AccentSoft);
        AddAliasBrush(resources, "ThemeAccentPressedBrush", colors.AccentPressed);
        AddAliasBrush(resources, "ThemeRibbonButtonHoverBrush", colors.RibbonButtonHover);

        return resources;
    }

    private static void AddAliasBrush(ResourceDictionary resources, string key, ThemeColor color) =>
        resources[key] = new ImmutableSolidColorBrush(ToColor(color));

    public static void Apply(Application application, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(application);
        Apply(application.Resources, theme, keyPrefix);
    }

    public static void Apply(IResourceDictionary target, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.MergedDictionaries.Add(BuildResources(theme, keyPrefix));
    }
}
