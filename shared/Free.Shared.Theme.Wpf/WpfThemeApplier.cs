using System.Windows;
using System.Windows.Media;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Wpf;

/// <summary>
/// Converts a <see cref="Theme"/> into WPF resources and merges them into an
/// <see cref="Application"/>'s resource dictionary.
/// </summary>
public static class WpfThemeApplier
{
    /// <summary>Converts a portable <see cref="ThemeColor"/> to a WPF <see cref="Color"/>.</summary>
    public static Color ToColor(ThemeColor color) =>
        Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Converts a portable <see cref="ThemeFontWeight"/> to a WPF <see cref="FontWeight"/>.</summary>
    public static FontWeight ToFontWeight(ThemeFontWeight weight) => weight switch
    {
        ThemeFontWeight.Bold => FontWeights.Bold,
        ThemeFontWeight.SemiBold => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    /// <summary>
    /// Materializes the portable theme resource plan as WPF brushes, metrics, and typography.
    /// </summary>
    public static ResourceDictionary BuildResources(Theme theme, string keyPrefix)
    {
        var plan = ThemeResourcePlan.Create(theme, keyPrefix);
        var resources = new ResourceDictionary();

        void AddBrush(string key, ThemeColor color)
        {
            var brush = new SolidColorBrush(ToColor(color));
            brush.Freeze();
            resources[key] = brush;
        }

        foreach (var color in plan.Colors)
            AddBrush(color.BrushKey, color.Value);

        foreach (var metric in plan.Metrics)
            resources[metric.Key] = metric.Value;

        foreach (var typography in plan.Typography)
        {
            var token = typography.Value;
            var fontFamily = string.IsNullOrEmpty(token.FontFamily)
                ? new FontFamily()
                : new FontFamily(token.FontFamily);
            resources[typography.FontFamilyKey] = fontFamily;
            resources[typography.FontSizeKey] = token.SizePt;
            resources[typography.FontWeightKey] = ToFontWeight(token.Weight);
        }

        foreach (var sharedBrush in plan.SharedBrushes)
            AddBrush(sharedBrush.Key, sharedBrush.Value);

        return resources;
    }

    /// <summary>
    /// Merges the theme resources into <paramref name="app"/> as the last dictionary so they
    /// override same-keyed resources from earlier dictionaries.
    /// </summary>
    public static void Apply(Application app, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Resources.MergedDictionaries.Add(BuildResources(theme, keyPrefix));
    }

    /// <summary>
    /// Merges the theme resources into <paramref name="target"/> as the last dictionary so they
    /// override same-keyed resources from earlier dictionaries.
    /// </summary>
    public static void Apply(ResourceDictionary target, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.MergedDictionaries.Add(BuildResources(theme, keyPrefix));
    }
}
