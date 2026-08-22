namespace Free.Shared.Ribbon;

/// <summary>Optional first-frame width estimates per size variant, used before the renderer measures.</summary>
public sealed record RibbonWidthHints(
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth);

public sealed record RibbonGroupSizing(
    IReadOnlyList<RibbonAdaptiveGroupState> SupportedVariants,
    RibbonWidthHints? Hints = null,
    bool EnableCompactPresentation = false,
    bool CompactControlsAsIcons = false)
{
    /// <summary>Opt-in Office-style command compaction before a group becomes overflow-only.</summary>
    public static readonly RibbonGroupSizing OfficeAdaptive = new(new[]
    {
        RibbonAdaptiveGroupState.Full,
        RibbonAdaptiveGroupState.SmallWithLabels,
        RibbonAdaptiveGroupState.Collapsed
    }, EnableCompactPresentation: true);

    /// <summary>Opt-in adaptive form that uses direct command icons where long compact captions would crop.</summary>
    public static readonly RibbonGroupSizing OfficeIconAdaptive = new(new[]
    {
        RibbonAdaptiveGroupState.Full,
        RibbonAdaptiveGroupState.SmallWithLabels,
        RibbonAdaptiveGroupState.Collapsed
    }, EnableCompactPresentation: true, CompactControlsAsIcons: true);

    // Keep this model default broad for renderer-neutral planning. Renderers that historically exposed
    // only a full and collapsed group preserve that behavior until a definition opts in explicitly.
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
