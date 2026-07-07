using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

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
    double VerticalInset,
    bool IsNegative = false,
    double AxisFraction = 0d,
    PresentationRgb? AxisColor = null,
    PresentationRgb? BorderColor = null)
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
    ConditionalIconGlyphKind GlyphKind,
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
    public const double DataBarHorizontalInset = ConditionalDataBarLayoutPlanner.HorizontalInset;

    /// <summary>Vertical inset (device pixels at 100% zoom) of a data bar from the cell edges.</summary>
    public const double DataBarVerticalInset = ConditionalDataBarLayoutPlanner.VerticalInset;

    /// <summary>Width (device pixels at 100% zoom) of the gutter reserved for an icon-set glyph.</summary>
    public const double IconGutterWidth = ConditionalIconCellLayoutPlanner.GutterWidth;

    /// <summary>
    /// Build a data-bar render instruction from the model record, or <c>null</c> when the bar would
    /// be empty. Start/end fractions are clamped to [0, 1] and normalized so start ≤ end.
    /// </summary>
    public static CfDataBarRenderInstruction? PlanDataBar(ConditionalFormatDataBar? dataBar)
    {
        if (dataBar is not { } bar)
            return null;

        if (ConditionalDataBarLayoutPlanner.Plan(bar.StartFraction, bar.EndFraction)
                is not { } layout)
            return null;

        return new CfDataBarRenderInstruction(
            layout.Start,
            layout.End,
            PresentationRgb.FromRgbColor(bar.FillColor),
            bar.Gradient,
            bar.Border,
            layout.HorizontalInset,
            layout.VerticalInset,
            bar.IsNegative,
            bar.AxisFraction,
            bar.AxisColor is { } axisColor ? PresentationRgb.FromRgbColor(axisColor) : null,
            bar.BorderColor is { } borderColor ? PresentationRgb.FromRgbColor(borderColor) : null);
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
            ConditionalIconGlyphResolver.ResolveGlyphKind(resolved.Style),
            index,
            resolved.IconCount,
            ConditionalIconGlyphResolver.ResolveIconColor(resolved.Style, index, resolved.IconCount),
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
}
