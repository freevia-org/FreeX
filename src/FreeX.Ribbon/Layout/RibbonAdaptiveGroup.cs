namespace FreeX.Ribbon;

/// <summary>
/// A ribbon group with the measured width of each adaptive size variant.
/// Widths are supplied by the renderer (see <see cref="IRibbonMeasurer"/>) or by
/// declared <see cref="RibbonWidthHints"/>; the core treats them as opaque doubles.
/// </summary>
public sealed record RibbonAdaptiveGroup(
    string Name,
    double FullWidth,
    double SmallWithLabelsWidth,
    double IconOnlyWidth,
    double CollapsedWidth,
    string? CatalogId = null);

public enum RibbonAdaptiveGroupState
{
    Full,
    SmallWithLabels,
    IconOnly,
    Collapsed
}
