using FreeX.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// The complete FreeX ribbon: the hand-authored, high-fidelity <see cref="HomeRibbonDefinition"/> Home tab
/// followed by the remaining main + contextual tabs generated from the catalog
/// (<see cref="FreeXRibbonDefinition"/>). This is the single source of truth consumed by the renderer.
/// </summary>
public static class FreeXRibbon
{
    public static RibbonDefinition Build()
    {
        var tabs = new List<RibbonTab> { HomeRibbonDefinition.HomeTab() };
        tabs.AddRange(FreeXRibbonDefinition.Build().Tabs);
        return new RibbonDefinition(tabs);
    }
}
