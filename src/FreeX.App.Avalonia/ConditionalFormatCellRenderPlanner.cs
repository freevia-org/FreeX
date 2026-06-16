using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Framework-neutral classification of a conditional-format icon glyph. Mirrors the desktop
/// <c>ConditionalIconGlyphKind</c> so the Avalonia renderer can draw the same shapes on macOS.
/// </summary>
public enum CfIconGlyphKind
{
    Arrow,
    TrafficLight,
    Sign,
    Symbol,
    Flag,
    Rating,
    Quarter,
    Box,
}

/// <summary>
/// Render instruction for a single data bar within a cell, expressed in fractions of the cell's
/// drawable content width so the UI layer only has to scale by pixel size. The bar is inset from
/// the cell edges by <see cref="HorizontalInset"/> / <see cref="VerticalInset"/> device pixels, the
/// same insets the desktop renderer uses.
/// </summary>
public readonly record struct CfDataBarRenderInstruction(
    double StartFraction,
    double EndFraction,
    PresentationRgb FillColor,
    bool Gradient,
    bool Border,
    double HorizontalInset,
    double VerticalInset)
{
    /// <summary>Signed extent of the bar, always ≥ 0.</summary>
    public double FractionWidth => EndFraction - StartFraction;
}

/// <summary>
/// Render instruction for a single icon-set glyph within a cell. <see cref="ColorHex"/> is the
/// resolved fill (e.g. <c>"#C00000"</c>); <see cref="TextGutter"/> is how far the cell text should
/// shift right to make room for the glyph (0 when the rule hides the value).
/// </summary>
public readonly record struct CfIconRenderInstruction(
    CfIconGlyphKind GlyphKind,
    int IconIndex,
    int IconCount,
    string ColorHex,
    bool ShowValue,
    double TextGutter);

/// <summary>
/// Portable mapping from the conditional-format results carried on a <see cref="DisplayCell"/>
/// (computed by the engine / portable <see cref="ConditionalFormatEvaluator"/>) into the
/// framework-neutral render instructions the Avalonia grid draws. All geometry and color logic
/// lives here so it can be unit-tested without a running UI, mirroring the desktop's
/// <c>ConditionalIconLayoutPlanner</c> / data-bar renderer exactly.
/// </summary>
public static class ConditionalFormatCellRenderPlanner
{
    /// <summary>Horizontal inset (device pixels at 100% zoom) of a data bar from the cell edges.</summary>
    public const double DataBarHorizontalInset = 2d;

    /// <summary>Vertical inset (device pixels at 100% zoom) of a data bar from the cell edges.</summary>
    public const double DataBarVerticalInset = 3d;

    /// <summary>Width (device pixels at 100% zoom) of the gutter reserved for an icon-set glyph.</summary>
    public const double IconGutterWidth = 20d;

    /// <summary>
    /// Build a data-bar render instruction from the model record, or <c>null</c> when the bar would
    /// be empty. Start/end fractions are clamped to [0, 1] and normalized so start ≤ end.
    /// </summary>
    public static CfDataBarRenderInstruction? PlanDataBar(ConditionalFormatDataBar? dataBar)
    {
        if (dataBar is not { } bar)
            return null;

        var start = Math.Clamp(bar.StartFraction, 0d, 1d);
        var end = Math.Clamp(bar.EndFraction, 0d, 1d);
        if (end < start)
            (start, end) = (end, start);

        if (end - start <= 0d)
            return null;

        return new CfDataBarRenderInstruction(
            start,
            end,
            PresentationRgb.FromRgbColor(bar.FillColor),
            bar.Gradient,
            bar.Border,
            DataBarHorizontalInset,
            DataBarVerticalInset);
    }

    /// <summary>
    /// Build a data-bar render instruction directly from a rule and cell value via the portable
    /// evaluator. Returns <c>null</c> when the rule is not a data-bar rule or the value produces no
    /// bar. The caller supplies the <see cref="ConditionalFormatStatistics"/> for the rule's range,
    /// built once per range per render pass.
    /// </summary>
    public static CfDataBarRenderInstruction? PlanDataBar(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (rule.RuleType != CfRuleType.DataBar)
            return null;

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, cellValue, stats);
        if (layout is not { } resolved)
            return null;

        return PlanDataBar(new ConditionalFormatDataBar(
            resolved.StartFraction,
            resolved.EndFraction,
            new RgbColor(resolved.FillColor.R, resolved.FillColor.G, resolved.FillColor.B),
            resolved.Gradient,
            resolved.Border,
            resolved.ShowValue));
    }

    /// <summary>
    /// Build an icon render instruction from the model record, or <c>null</c> when no icon applies.
    /// The glyph kind and color are resolved the same way the desktop renderer resolves them.
    /// </summary>
    public static CfIconRenderInstruction? PlanIcon(ConditionalFormatIcon? icon)
    {
        if (icon is not { } resolved)
            return null;

        var index = Math.Clamp(resolved.IconIndex, 0, Math.Max(0, resolved.IconCount - 1));
        return new CfIconRenderInstruction(
            ResolveGlyphKind(resolved.Style),
            index,
            resolved.IconCount,
            ResolveIconColor(resolved.Style, index, resolved.IconCount),
            resolved.ShowValue,
            resolved.ShowValue ? IconGutterWidth : 0d);
    }

    /// <summary>
    /// Build an icon render instruction directly from a rule and cell value via the portable
    /// evaluator. Returns <c>null</c> when the rule is not an icon-set rule or no bucket applies.
    /// </summary>
    public static CfIconRenderInstruction? PlanIcon(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (rule.RuleType != CfRuleType.IconSet)
            return null;

        var result = ConditionalFormatEvaluator.EvaluateIconSet(rule, cellValue, stats);
        if (result is not { } icon)
            return null;

        return PlanIcon(new ConditionalFormatIcon(
            icon.Style,
            icon.BucketIndex,
            icon.IconCount,
            icon.ShowValue));
    }

    /// <summary>
    /// Resolve the framework-neutral glyph kind for an icon-set style name. Identical mapping to the
    /// desktop <c>ConditionalIconLayoutPlanner.ResolveGlyphKind</c>.
    /// </summary>
    public static CfIconGlyphKind ResolveGlyphKind(string? style)
    {
        style ??= string.Empty;

        if (style.Contains("TrafficLights", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("RedToBlack", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.TrafficLight;
        if (style.Contains("Signs", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Sign;
        if (style.Contains("Symbols", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Symbol;
        if (style.Contains("Flags", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Flag;
        if (style.Contains("Rating", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Rating;
        if (style.Contains("Quarters", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Quarter;
        if (style.Contains("Boxes", StringComparison.OrdinalIgnoreCase))
            return CfIconGlyphKind.Box;
        return CfIconGlyphKind.Arrow;
    }

    /// <summary>
    /// Resolve the icon fill color (hex) for a bucket. Identical mapping to the desktop
    /// <c>ConditionalIconLayoutPlanner.ResolveColor</c>, including the gray-style override.
    /// </summary>
    public static string ResolveIconColor(string? style, int iconIndex, int iconCount)
    {
        if ((style ?? string.Empty).Contains("Gray", StringComparison.OrdinalIgnoreCase))
            return "#666666";

        var index = Math.Clamp(iconIndex, 0, Math.Max(0, iconCount - 1));
        return iconCount switch
        {
            >= 5 => index switch
            {
                0 => "#C00000",
                1 => "#ED7D31",
                2 => "#FFC000",
                3 => "#92D050",
                _ => "#00B050",
            },
            4 => index switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                2 => "#92D050",
                _ => "#00B050",
            },
            _ => index switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                _ => "#00B050",
            },
        };
    }
}
