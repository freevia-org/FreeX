using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Avalonia;

/// <summary>
/// Converts a <see cref="Theme"/> into Avalonia resources.
/// Can merge the generated resources into a running application or resource dictionary.
/// </summary>
public static class AvaloniaThemeApplier
{
    /// <summary>Converts a portable <see cref="ThemeColor"/> to an Avalonia <see cref="Color"/>.</summary>
    public static global::Avalonia.Media.Color ToColor(ThemeColor c) =>
        global::Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Converts a portable <see cref="ThemeFontWeight"/> to an Avalonia <see cref="FontWeight"/>.</summary>
    public static FontWeight ToFontWeight(ThemeFontWeight w) => w switch
    {
        ThemeFontWeight.Bold     => FontWeight.Bold,
        ThemeFontWeight.SemiBold => FontWeight.SemiBold,
        _                        => FontWeight.Normal,
    };

    /// <summary>
    /// Builds an Avalonia <see cref="ResourceDictionary"/> containing an
    /// <see cref="ImmutableSolidColorBrush"/> for each of the 22 color roles under
    /// <c>{keyPrefix}{Role}Brush</c> keys (matching the WPF applier's key pattern).
    /// Also adds raw <see cref="global::Avalonia.Media.Color"/> entries under
    /// <c>{keyPrefix}{Role}Color</c> and double metrics under
    /// <c>{keyPrefix}{MetricName}</c>.
    /// </summary>
    public static ResourceDictionary BuildResources(Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);

        var dict = new ResourceDictionary();

        // ── Color brushes (21 roles) ──────────────────────────────────────────────
        void AddBrush(string role, ThemeColor themeColor)
        {
            var avColor = ToColor(themeColor);
            dict[$"{keyPrefix}{role}Brush"]  = new ImmutableSolidColorBrush(avColor);
            dict[$"{keyPrefix}{role}Color"]  = avColor;
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
        AddBrush("RibbonInlineDivider",  c.RibbonInlineDivider);
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
        // TitleBarCaptionHeight is a WPF-only metric (Avalonia uses the native OS title bar).
        // The resource is still emitted so both appliers have a symmetric key set, but the Avalonia
        // MainWindow does not consume it — any consumer can safely ignore it.
        dict[$"{keyPrefix}TitleBarCaptionHeight"]  = m.TitleBarCaptionHeight;

        // ── Typography: FontFamily, FontSize (double), FontWeight ─────────────────
        // Emits {prefix}{Role}FontFamily, {prefix}{Role}FontSize, {prefix}{Role}FontWeight.
        // When FontFamily is empty, emits FontFamily.Default (the Avalonia system-default family).
        void AddTypo(string role, ThemeTypeToken tok)
        {
            var ff = string.IsNullOrEmpty(tok.FontFamily)
                ? FontFamily.Default
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

        // Prefix-free aliases keep shared renderers app-neutral. These mirror the WPF
        // resource contract exactly; accent values remain specific to each brand theme.
        AddAliasBrush(dict, "ThemeNeutralTextBrush",         c.Text);
        AddAliasBrush(dict, "ThemeNeutralMutedTextBrush",    c.MutedText);
        AddAliasBrush(dict, "ThemeNeutralWhiteBrush",        c.White);
        AddAliasBrush(dict, "ThemeNeutralDangerBrush",       c.Danger);
        AddAliasBrush(dict, "ThemeNeutralSheetSurfaceBrush", c.SheetSurface);

        AddAliasBrush(dict, "ThemeAccentBrush",            c.Accent);
        AddAliasBrush(dict, "ThemeAccentDarkBrush",        c.AccentDark);
        AddAliasBrush(dict, "ThemeAccentSoftBrush",        c.AccentSoft);
        AddAliasBrush(dict, "ThemeAccentPressedBrush",     c.AccentPressed);
        AddAliasBrush(dict, "ThemeRibbonButtonHoverBrush", c.RibbonButtonHover);

        return dict;
    }

    private static void AddAliasBrush(ResourceDictionary dict, string key, ThemeColor color) =>
        dict[key] = new ImmutableSolidColorBrush(ToColor(color));

    /// <summary>
    /// Merges the generated theme resources as the last application dictionary so they
    /// override earlier fallback resources, matching the WPF applier's precedence.
    /// </summary>
    public static void Apply(Application app, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(app);
        Apply(app.Resources, theme, keyPrefix);
    }

    /// <summary>Merges the generated resources as the last dictionary in <paramref name="target"/>.</summary>
    public static void Apply(IResourceDictionary target, Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.MergedDictionaries.Add(BuildResources(theme, keyPrefix));
    }
}
