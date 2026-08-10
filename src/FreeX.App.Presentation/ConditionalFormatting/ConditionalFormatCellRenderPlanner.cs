using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

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
    public double FractionWidth => EndFraction - StartFraction;
}

public readonly record struct CfIconRenderInstruction(
    ConditionalIconGlyphKind GlyphKind,
    int IconIndex,
    int IconCount,
    string ColorHex,
    bool ShowValue,
    double TextGutter);

/// <summary>
/// Maps conditional-format model results into framework-neutral cell render instructions.
/// </summary>
public static class ConditionalFormatCellRenderPlanner
{
    public const double DataBarHorizontalInset = ConditionalDataBarLayoutPlanner.HorizontalInset;
    public const double DataBarVerticalInset = ConditionalDataBarLayoutPlanner.VerticalInset;
    public const double IconGutterWidth = ConditionalIconCellLayoutPlanner.GutterWidth;

    public static CfDataBarRenderInstruction? PlanDataBar(ConditionalFormatDataBar? dataBar)
    {
        if (dataBar is not { } bar)
            return null;

        if (ConditionalDataBarLayoutPlanner.Plan(bar.StartFraction, bar.EndFraction) is not { } layout)
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
