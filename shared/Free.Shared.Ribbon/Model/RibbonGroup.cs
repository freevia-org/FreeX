namespace Free.Shared.Ribbon;

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
    RibbonGroupSizing Sizing)
{
    /// <summary>
    /// Optional Office-style dialog launcher shown beside the group label in full ribbon layouts.
    /// It intentionally remains outside <see cref="Controls"/> so adaptive collapsed-group menus
    /// continue to expose only the group's primary commands.
    /// </summary>
    public RibbonGroupLauncher? Launcher { get; init; }
}

/// <summary>Command and accessible tooltip metadata for a ribbon group's dialog launcher.</summary>
public sealed record RibbonGroupLauncher(
    RibbonCommandId CommandId,
    string TooltipTitle,
    string? TooltipDescription = null,
    string? KeyTip = null);
