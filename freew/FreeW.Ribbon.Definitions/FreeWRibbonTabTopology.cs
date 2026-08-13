using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Applies one ordered canonical tab topology. A section uses the desktop realization by default;
/// portable renderers override only the controls whose native representation actually differs.
/// </summary>
internal sealed class FreeWRibbonTabTopology
{
    private readonly RibbonTabBuilder _tab;
    private readonly FreeWRibbonCapabilities _capabilities;
    private readonly List<SectionEntry> _sections = [];

    internal FreeWRibbonTabTopology(RibbonTabBuilder tab, FreeWRibbonCapabilities capabilities)
    {
        _tab = tab;
        _capabilities = capabilities;
    }

    internal void Section(
        string sectionId,
        Action<RibbonTabBuilder> canonical,
        Action<RibbonTabBuilder>? portableOverride = null,
        int? portableOrder = null)
    {
        if (!_capabilities.IncludesSection(sectionId))
            return;

        var configure = _capabilities.UsesPortableControls && portableOverride is not null
            ? portableOverride
            : canonical;
        var canonicalOrder = _sections.Count;
        _sections.Add(new SectionEntry(
            configure,
            _capabilities.UsesPortableControls ? portableOrder ?? canonicalOrder : canonicalOrder));
    }

    internal void Build()
    {
        foreach (var section in _sections.OrderBy(entry => entry.Order))
            section.Configure(_tab);
    }

    private sealed record SectionEntry(Action<RibbonTabBuilder> Configure, int Order);
}
