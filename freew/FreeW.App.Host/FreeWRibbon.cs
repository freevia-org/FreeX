using Free.Shared.Ribbon;
using FreeW.Ribbon.Definitions;

namespace FreeW.App.Host;

/// <summary>
/// WPF host adapter for the shared FreeW ribbon definition.
/// </summary>
internal static class FreeWRibbon
{
    public static RibbonDefinition Build() =>
        FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
}
