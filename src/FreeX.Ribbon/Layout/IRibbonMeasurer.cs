namespace FreeX.Ribbon;

/// <summary>
/// The single thing the layout engine needs from a UI framework: the measured width of a
/// group's size variants. Returns the platform-neutral <see cref="RibbonAdaptiveGroup"/>
/// (doubles only), so the core stays renderer-agnostic.
/// </summary>
public interface IRibbonMeasurer
{
    RibbonAdaptiveGroup Measure(string groupId, IReadOnlyList<RibbonAdaptiveGroupState> supportedVariants);
}
