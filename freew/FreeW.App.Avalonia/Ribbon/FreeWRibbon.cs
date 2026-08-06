global using RibbonHostCallbacks = FreeW.App.Presentation.Ribbon.FreeWRibbonHostExecutionPorts;

using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Ribbon.Definitions;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Avalonia shell adapter for the shared FreeW ribbon definition.
/// </summary>
internal static class FreeWRibbon
{
    public static readonly string[] FontSizes = FreeWRibbonDefinitionData.FontSizes;

    public static readonly string[] FontFamilies = FreeWRibbonDefinitionData.FontFamilies;

    public static readonly string[] FloatSizes = FreeWRibbonDefinitionData.FloatSizes;

    internal static (string CommandId, string Label)[] FontColors => FreeWRibbonDefinitionData.FontColors;

    internal static readonly (string CommandId, string Label)[] PageColors = FreeWRibbonDefinitionData.PageColors;

    internal static string ParaSpacingId(string name) => FreeWRibbonDefinitionData.ParaSpacingId(name);

    public static RibbonDefinition BuildDefinition() =>
        FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);

    public static RibbonCommandRegistry BuildRegistry(DocumentView editor, RibbonHostCallbacks callbacks) =>
        FreeWAvaloniaRibbonCommands.Build(editor, callbacks);

    public static RibbonCommandRegistry BuildRegistry(
        DocumentView editor,
        RibbonHostCallbacks callbacks,
        out MailMergeEngine mailMerge) =>
        FreeWAvaloniaRibbonCommands.Build(editor, callbacks, out mailMerge);
}
