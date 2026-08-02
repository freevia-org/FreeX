using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SmartArtNodeStyle(
    ThemeAwareColor Fill,
    ThemeAwareColor Outline,
    ThemeAwareColor Text,
    double OutlineWidthPt);

public sealed record SmartArtConnectorStyle(ThemeAwareColor Outline, double WidthPt);

public sealed class SmartArtStylePlan
{
    private readonly SrgbColor[] _palette;
    private readonly bool _singleColor;
    private readonly SmartArtStyleProfile _profile;

    internal SmartArtStylePlan(SrgbColor[] palette, bool singleColor, SmartArtStyleProfile profile)
    {
        _palette = palette.Length == 0 ? [SrgbColor.FromRgb(0x4472C4)] : palette;
        _singleColor = singleColor;
        _profile = profile;
        Connector = new SmartArtConnectorStyle(new ThemeAwareColor(ConnectorColor()), ConnectorWidthPt());
    }

    public SmartArtConnectorStyle Connector { get; }

    public SmartArtNodeStyle GetNodeStyle(int nodeIndex, int level, SmartArtFamily family)
    {
        var paletteIndex = family == SmartArtFamily.Hierarchy ? Math.Max(level, 0) : Math.Max(nodeIndex, 0);
        var baseFill = SelectBaseFill(paletteIndex);
        var fill = ApplyStyleFill(baseFill);
        var outline = ApplyStyleOutline(baseFill);
        var text = PickReadableText(fill);

        return new SmartArtNodeStyle(
            new ThemeAwareColor(fill),
            new ThemeAwareColor(outline),
            new ThemeAwareColor(text),
            _profile switch
            {
                SmartArtStyleProfile.SimpleFill => 1.0,
                SmartArtStyleProfile.Subtle => 0.85,
                SmartArtStyleProfile.Intense => 1.4,
                _ => 1.1
            });
    }

    private SrgbColor SelectBaseFill(int index)
    {
        if (!_singleColor || _palette.Length == 1)
            return _palette[index % _palette.Length];

        var baseColor = _palette[0];
        return (index % 4) switch
        {
            0 => baseColor,
            1 => ThemeColorTransform.ApplyTint(baseColor, 0.78),
            2 => ThemeColorTransform.ApplyTint(baseColor, 0.58),
            _ => ThemeColorTransform.ApplyShade(baseColor, 0.82)
        };
    }

    private SrgbColor ApplyStyleFill(SrgbColor color) =>
        _profile switch
        {
            SmartArtStyleProfile.SimpleFill => color,
            SmartArtStyleProfile.Subtle => ThemeColorTransform.ApplyTint(color, 0.32),
            SmartArtStyleProfile.Intense => ThemeColorTransform.ApplyShade(color, 0.72),
            SmartArtStyleProfile.Polished => ThemeColorTransform.ApplyShade(color, 0.78),
            SmartArtStyleProfile.Inset => ThemeColorTransform.ApplyTint(color, 0.52),
            SmartArtStyleProfile.Cartoon => color,
            SmartArtStyleProfile.Powder => ThemeColorTransform.ApplyTint(color, 0.42),
            SmartArtStyleProfile.BrickScene => ThemeColorTransform.ApplyShade(color, 0.86),
            SmartArtStyleProfile.FlatScene => color,
            SmartArtStyleProfile.MetallicScene => ThemeColorTransform.ApplyShade(color, 0.65),
            SmartArtStyleProfile.SunsetScene => ThemeColorTransform.ApplyTint(color, 0.18),
            SmartArtStyleProfile.BirdsEyeScene => ThemeColorTransform.ApplyShade(color, 0.92),
            _ => ThemeColorTransform.ApplyTint(color, 0.88)
        };

    private SrgbColor ApplyStyleOutline(SrgbColor color) =>
        _profile switch
        {
            SmartArtStyleProfile.WhiteOutline => SrgbColor.White,
            SmartArtStyleProfile.Subtle => ThemeColorTransform.ApplyShade(color, 0.72),
            SmartArtStyleProfile.Intense => ThemeColorTransform.ApplyShade(color, 0.45),
            SmartArtStyleProfile.Polished => ThemeColorTransform.ApplyShade(color, 0.32),
            SmartArtStyleProfile.Inset => ThemeColorTransform.ApplyShade(color, 0.58),
            SmartArtStyleProfile.Cartoon => ThemeColorTransform.ApplyShade(color, 0.28),
            SmartArtStyleProfile.Powder => ThemeColorTransform.ApplyShade(color, 0.78),
            SmartArtStyleProfile.BrickScene => ThemeColorTransform.ApplyShade(color, 0.36),
            SmartArtStyleProfile.FlatScene => ThemeColorTransform.ApplyShade(color, 0.62),
            SmartArtStyleProfile.MetallicScene => SrgbColor.White,
            SmartArtStyleProfile.SunsetScene => ThemeColorTransform.ApplyShade(color, 0.48),
            SmartArtStyleProfile.BirdsEyeScene => ThemeColorTransform.ApplyShade(color, 0.40),
            _ => ThemeColorTransform.ApplyShade(color, 0.62)
        };

    private SrgbColor ConnectorColor() =>
        _profile switch
        {
            SmartArtStyleProfile.WhiteOutline => SrgbColor.White,
            SmartArtStyleProfile.Subtle => ThemeColorTransform.ApplyShade(_palette[0], 0.68),
            SmartArtStyleProfile.Polished => ThemeColorTransform.ApplyShade(_palette[0], 0.36),
            SmartArtStyleProfile.Inset => ThemeColorTransform.ApplyShade(_palette[0], 0.58),
            SmartArtStyleProfile.Cartoon => ThemeColorTransform.ApplyShade(_palette[0], 0.30),
            SmartArtStyleProfile.Powder => ThemeColorTransform.ApplyShade(_palette[0], 0.76),
            SmartArtStyleProfile.BrickScene => ThemeColorTransform.ApplyShade(_palette[0], 0.40),
            SmartArtStyleProfile.FlatScene => ThemeColorTransform.ApplyShade(_palette[0], 0.50),
            SmartArtStyleProfile.MetallicScene => SrgbColor.White,
            SmartArtStyleProfile.SunsetScene => ThemeColorTransform.ApplyShade(_palette[0], 0.46),
            SmartArtStyleProfile.BirdsEyeScene => ThemeColorTransform.ApplyShade(_palette[0], 0.38),
            _ => ThemeColorTransform.ApplyShade(_palette[0], 0.50)
        };

    private double ConnectorWidthPt() =>
        _profile switch
        {
            SmartArtStyleProfile.WhiteOutline => 1.25,
            SmartArtStyleProfile.Intense => 1.75,
            SmartArtStyleProfile.Polished => 1.8,
            SmartArtStyleProfile.Inset => 1.15,
            SmartArtStyleProfile.Cartoon => 2.0,
            SmartArtStyleProfile.Powder => 1.0,
            SmartArtStyleProfile.BrickScene => 1.7,
            SmartArtStyleProfile.FlatScene => 1.35,
            SmartArtStyleProfile.MetallicScene => 1.9,
            SmartArtStyleProfile.SunsetScene => 1.5,
            SmartArtStyleProfile.BirdsEyeScene => 1.65,
            _ => 1.35
        };

    private static SrgbColor PickReadableText(SrgbColor fill)
    {
        double lum = 0.2126 * fill.R / 255.0 + 0.7152 * fill.G / 255.0 + 0.0722 * fill.B / 255.0;
        return lum < 0.52 ? SrgbColor.White : SrgbColor.Black;
    }
}

public static class SmartArtStylePlanner
{
    internal static SrgbColor ResolveNeutralConnector(PresentationTheme theme)
    {
        var lt2 = theme.ColorScheme[ThemeColorSlot.Lt2];
        if (lt2 == SrgbColor.FromRgb(0xE8E8E8))
            return SrgbColor.FromRgb(0xAAB6C1);

        return ThemeColorTransform.ApplyShade(lt2, 0.72);
    }

    public static SmartArtStylePlan Build(
        SmartArtFamily family,
        SmartArtQuickStyleMetadata? quickStyle,
        SmartArtColorMetadata? colors,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var palette = ResolvePalette(colors, theme, effectiveClrMap);
        var singleColor = UsesSingleColor(colors, family);
        var profile = InferProfile(quickStyle);
        return new SmartArtStylePlan(palette, singleColor, profile);
    }

    private static SrgbColor[] ResolvePalette(
        SmartArtColorMetadata? colors,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (colors?.Palette.Count > 0)
        {
            return colors.Palette
                .Select(c => ThemeColorResolver.Resolve(c, theme, effectiveClrMap))
                .Distinct()
                .Take(12)
                .ToArray();
        }

        return
        [
            theme.ColorScheme[ThemeColorSlot.Accent1],
            theme.ColorScheme[ThemeColorSlot.Accent2],
            theme.ColorScheme[ThemeColorSlot.Accent3],
            theme.ColorScheme[ThemeColorSlot.Accent4],
            theme.ColorScheme[ThemeColorSlot.Accent5],
            theme.ColorScheme[ThemeColorSlot.Accent6]
        ];
    }

    private static bool UsesSingleColor(SmartArtColorMetadata? colors, SmartArtFamily family)
    {
        if (colors is null || colors.Palette.Count <= 1) return true;

        var hint = JoinHints(colors.UniqueId, colors.Title, colors.Category);
        if (hint.Contains("same", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("mono", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("single", StringComparison.OrdinalIgnoreCase))
            return true;

        return family == SmartArtFamily.Hierarchy
            && !hint.Contains("colorful", StringComparison.OrdinalIgnoreCase);
    }

    private static SmartArtStyleProfile InferProfile(SmartArtQuickStyleMetadata? quickStyle)
    {
        var hint = quickStyle is null
            ? string.Empty
            : JoinHints(quickStyle.UniqueId, quickStyle.Title, quickStyle.Category, quickStyle.StyleLabels);

        if (hint.Contains("simple2", StringComparison.OrdinalIgnoreCase)
            || (hint.Contains("white", StringComparison.OrdinalIgnoreCase)
                && hint.Contains("outline", StringComparison.OrdinalIgnoreCase)))
            return SmartArtStyleProfile.WhiteOutline;

        if (hint.Contains("simple1", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("simple fill", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.SimpleFill;

        if (hint.Contains("simple5", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("intense", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Intense;

        if (hint.Contains("simple4", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("moderate", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Moderate;

        if (hint.Contains("simple3", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("subtle", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Subtle;

        // The native 3D scene gallery is not one style.  Keep the IDs distinct so the
        // live WPF/Avalonia route reflects a user's selected scene profile instead of
        // collapsing every 3d1-3d9 quick style to Intense.
        if (hint.Contains("3d1", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("polish", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Polished;

        if (hint.Contains("3d2", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("inset", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Inset;

        if (hint.Contains("3d3", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("cartoon", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Cartoon;

        if (hint.Contains("3d4", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("powder", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.Powder;

        if (hint.Contains("3d5", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("brick scene", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.BrickScene;

        if (hint.Contains("3d6", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("flat scene", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.FlatScene;

        if (hint.Contains("3d7", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("metallic scene", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.MetallicScene;

        if (hint.Contains("3d8", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("sunset scene", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.SunsetScene;

        if (hint.Contains("3d9", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("bird's eye scene", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("birds eye scene", StringComparison.OrdinalIgnoreCase))
            return SmartArtStyleProfile.BirdsEyeScene;

        return SmartArtStyleProfile.Moderate;
    }

    private static string JoinHints(params string[] values) =>
        string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string JoinHints(string a, string b, string c, IEnumerable<string> values) =>
        JoinHints(new[] { a, b, c }.Concat(values).ToArray());
}

internal enum SmartArtStyleProfile
{
    SimpleFill,
    Subtle,
    WhiteOutline,
    Moderate,
    Intense,
    Polished,
    Inset,
    Cartoon,
    Powder,
    BrickScene,
    FlatScene,
    MetallicScene,
    SunsetScene,
    BirdsEyeScene
}
