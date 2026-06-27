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

/// <summary>
/// Lightweight <see cref="IRibbonCommand"/> wrapper for a parameterless action.
/// Mirrors the FreeW Avalonia shell's <c>RelayCommand</c>.
/// </summary>
internal sealed class RelayCommand(Action execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute();
}

/// <summary>
/// <see cref="IRibbonCommand"/> that does nothing (deferred command placeholder).
/// The button renders enabled so the toolbar looks complete, but takes no action until
/// the full interaction layer is ported in Wave 14C.
/// </summary>
internal sealed class NoOpCommand : IRibbonCommand
{
    public static readonly NoOpCommand Instance = new();
    public void Execute(RibbonCommandContext context) { }
}
