namespace Free.Shared.Ribbon;

/// <summary>
/// A platform's realization of a resolved <see cref="RibbonLayoutPlan"/>.
/// <see cref="Realize"/> builds the native control tree once per tab/context change;
/// <see cref="Apply"/> diff-applies size-variant changes without rebuilding (realtime reflow).
/// </summary>
public interface IRibbonRenderer
{
    void Realize(RibbonLayoutPlan plan);

    void Apply(RibbonLayoutPlan plan);
}
