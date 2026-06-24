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
    public static Color ToColor(ThemeColor c) =>
        Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Converts a portable <see cref="ThemeFontWeight"/> to a WPF <see cref="FontWeight"/>.</summary>
    public static FontWeight ToFontWeight(ThemeFontWeight w) => w switch
    {
        ThemeFontWeight.Bold     => FontWeights.Bold,
        ThemeFontWeight.SemiBold => FontWeights.SemiBold,
        _                        => FontWeights.Normal,
    };

    /// <summary>
    /// Builds a <see cref="ResourceDictionary"/> containing a frozen
    /// <see cref="SolidColorBrush"/> for each of the 21 color roles, double values for
    /// the 4 metrics, and <see cref="FontFamily"/> resources for the 4 typography roles.
    /// Keys follow the pattern <c>{keyPrefix}{Role}Brush</c> / <c>{keyPrefix}{MetricName}</c>
    /// / <c>{keyPrefix}{TypoRole}FontFamily</c> — matching FreeX's existing XAML keys when
    /// <paramref name="keyPrefix"/> is <c>"FreeX"</c>.
    /// </summary>
    public static ResourceDictionary BuildResources(Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);

        var dict = new ResourceDictionary();

        // ── Color brushes (21 roles) ──────────────────────────────────────────────
        void AddBrush(string role, ThemeColor color)
        {
            var brush = new SolidColorBrush(ToColor(color));
            brush.Freeze();
            dict[$"{keyPrefix}{role}Brush"] = brush;
        }

        var c = theme.Colors;
        AddBrush("Accent",               c.Accent);
        AddBrush("AccentDark",           c.AccentDark);
        AddBrush("AccentSoft",           c.AccentSoft);
        AddBrush("AccentPressed",        c.AccentPressed);
        AddBrush("TitleBar",             c.TitleBar);
        AddBrush("TitleBarHover",        c.TitleBarHover);
        AddBrush("TitleBarPressed",      c.TitleBarPressed);
        AddBrush("TitleBarDisabled",     c.TitleBarDisabled);
        AddBrush("TitleBarButtonBorder", c.TitleBarButtonBorder);
        AddBrush("RibbonButtonHover",    c.RibbonButtonHover);
        AddBrush("Text",                 c.Text);
        AddBrush("MutedText",            c.MutedText);
        AddBrush("SubtleText",           c.SubtleText);
        AddBrush("RibbonSurface",        c.RibbonSurface);
        AddBrush("ChromeSurface",        c.ChromeSurface);
        AddBrush("SheetSurface",         c.SheetSurface);
        AddBrush("StatusSurface",        c.StatusSurface);
        AddBrush("Border",               c.Border);
        AddBrush("BorderStrong",         c.BorderStrong);
        AddBrush("Danger",               c.Danger);
        AddBrush("White",                c.White);

        // ── Metrics (6 doubles) ───────────────────────────────────────────────────
        var m = theme.Metrics;
        dict[$"{keyPrefix}RibbonRowHeight"]       = m.RibbonRowHeight;
        dict[$"{keyPrefix}ControlHeight"]          = m.ControlHeight;
        dict[$"{keyPrefix}IconSize"]               = m.IconSize;
        dict[$"{keyPrefix}CornerRadius"]           = m.CornerRadius;
        dict[$"{keyPrefix}StatusBarHeight"]        = m.StatusBarHeight;
        dict[$"{keyPrefix}TitleBarCaptionHeight"]  = m.TitleBarCaptionHeight;

        // ── Typography: FontFamily, FontSize (double), FontWeight ─────────────────
        // Emits {prefix}{Role}FontFamily, {prefix}{Role}FontSize, {prefix}{Role}FontWeight
        // for every typography role.  When FontFamily is empty the resource is new FontFamily()
        // (runtime inherited font) so XAML DynamicResource bindings always resolve.
        void AddTypo(string role, ThemeTypeToken tok)
        {
            // WPF FontFamily: when the token family is empty, use new FontFamily() which resolves
            // to the window/element's inherited font family at runtime (same as omitting FontFamily).
            var ff = string.IsNullOrEmpty(tok.FontFamily)
                ? new FontFamily()
                : new FontFamily(tok.FontFamily);
            dict[$"{keyPrefix}{role}FontFamily"] = ff;
            dict[$"{keyPrefix}{role}FontSize"]   = tok.SizePt;
            dict[$"{keyPrefix}{role}FontWeight"] = ToFontWeight(tok.Weight);
        }

        var t = theme.Typography;
        AddTypo("Body",          t.Body);
        AddTypo("Caption",       t.Caption);
        AddTypo("RibbonLabel",   t.RibbonLabel);
        AddTypo("Heading",       t.Heading);
        AddTypo("StatusBarText", t.StatusBarText);

        return dict;
    }

    /// <summary>
    /// Merges the theme resources into <paramref name="app"/>'s merged dictionaries as the
    /// <em>last</em> entry so it overrides any same-keyed brushes already defined in earlier
    /// dictionaries (e.g. <c>ThemeResources.xaml</c>).
    /// </summary>
    public static void Apply(Application app, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(app);
        var dict = BuildResources(theme, keyPrefix);
        app.Resources.MergedDictionaries.Add(dict);
    }

    /// <summary>
    /// Merges the theme resources into an arbitrary <see cref="ResourceDictionary"/> as the
    /// <em>last</em> entry so it overrides any same-keyed brushes already present (e.g. from
    /// <c>ThemeResources.xaml</c> when called on a <c>Window.Resources</c> that already
    /// has ThemeResources merged in).
    /// </summary>
    public static void Apply(ResourceDictionary target, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(target);
        var dict = BuildResources(theme, keyPrefix);
        target.MergedDictionaries.Add(dict);
    }
}
