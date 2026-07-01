using Free.Shared.Ribbon;
using FreeP.Ribbon.Definitions;

namespace FreeP.App.Host;

/// <summary>
/// WPF host adapter for the shared FreeP ribbon definition.
/// </summary>
internal static class FreePRibbon
{
    public static RibbonDefinition Build() =>
        FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
}
