namespace FreeX.Ribbon;

/// <summary>A group resolved to a concrete size variant for the current available width.</summary>
public sealed record RibbonResolvedGroup(
    string GroupId,
    RibbonAdaptiveGroupState State);

/// <summary>
/// The engine's output for one tab at one width: the ordered groups with their chosen
/// size variants, plus the breakpoint thresholds the resize gate watches.
/// </summary>
public sealed record RibbonLayoutPlan(
    string TabId,
    IReadOnlyList<RibbonResolvedGroup> Groups,
    IReadOnlyList<double> Thresholds);
