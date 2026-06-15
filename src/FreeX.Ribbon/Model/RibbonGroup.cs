namespace FreeX.Ribbon;

/// <summary>Optional first-frame width estimates per size variant, used before the renderer measures.</summary>
public sealed record RibbonWidthHints(
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth);

public sealed record RibbonGroupSizing(
    IReadOnlyList<RibbonAdaptiveGroupState> SupportedVariants,
    RibbonWidthHints? Hints = null)
{
    public static readonly RibbonGroupSizing Default = new(new[]
    {
        RibbonAdaptiveGroupState.Full,
        RibbonAdaptiveGroupState.SmallWithLabels,
        RibbonAdaptiveGroupState.IconOnly,
        RibbonAdaptiveGroupState.Collapsed
    });
}

public sealed record RibbonGroup(
    string Id,
    string Header,
    string? KeyTip,
    int Priority,
    IReadOnlyList<RibbonControl> Controls,
    RibbonGroupSizing Sizing);
