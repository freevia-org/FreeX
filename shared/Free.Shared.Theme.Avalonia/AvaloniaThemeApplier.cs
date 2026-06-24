using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Theme;

namespace Free.Shared.Theme.Avalonia;

/// <summary>
/// Converts a <see cref="Theme"/> into Avalonia resources.
/// Not live-wired to a running application in round 1 — built and unit-tested only.
/// </summary>
public static class AvaloniaThemeApplier
{
    /// <summary>Converts a portable <see cref="ThemeColor"/> to an Avalonia <see cref="Color"/>.</summary>
    public static global::Avalonia.Media.Color ToColor(ThemeColor c) =>
        global::Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>
    /// Builds an Avalonia <see cref="ResourceDictionary"/> containing an
    /// <see cref="ImmutableSolidColorBrush"/> for each of the 21 color roles under
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

        // ── Metrics (4 doubles) ───────────────────────────────────────────────────
        var m = theme.Metrics;
        dict[$"{keyPrefix}RibbonRowHeight"] = m.RibbonRowHeight;
        dict[$"{keyPrefix}ControlHeight"]   = m.ControlHeight;
        dict[$"{keyPrefix}IconSize"]         = m.IconSize;
        dict[$"{keyPrefix}CornerRadius"]     = m.CornerRadius;

        return dict;
    }
}
