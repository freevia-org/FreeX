using Free.Shared.Ribbon;
using FreeP.Ribbon.Definitions;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia host adapter for the shared FreeP ribbon definition.
/// </summary>
internal static class FreePRibbonAvalonia
{
    public static RibbonDefinition Build() =>
        FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);
}
