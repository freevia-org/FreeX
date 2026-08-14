using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CoverPageRibbonPorts(Action<CoverPagePreset> InsertPreset);

public sealed record CoverPageRibbonChoice(
    RibbonCommandId CommandId,
    RibbonCommandId LegacyCommandId,
    CoverPagePreset Preset);

/// <summary>
/// Owns Insert &gt; Cover Page preset identity and dispatch for both renderers. The WPF command IDs
/// are canonical; Avalonia's earlier dotted IDs remain registry-only compatibility aliases.
/// </summary>
public static class CoverPageRibbonWorkflow
{
    private static readonly CoverPageRibbonChoice[] ChoiceItems =
    [
        new("freew.cover-page-default", "freew.cover-page.default", CoverPagePreset.Default),
        new("freew.cover-page-banded", "freew.cover-page.banded", CoverPagePreset.Banded),
        new("freew.cover-page-motion", "freew.cover-page.motion", CoverPagePreset.Motion),
    ];

    public static IReadOnlyList<CoverPageRibbonChoice> Choices => ChoiceItems;

    public static void Register(
        IRibbonCommandRegistry bindings,
        CoverPageRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertPreset);

        var defaultCommand = bindings.BindAction(
            FreeWRibbonCommandAction.CoverPage,
            () => ports.InsertPreset(CoverPagePreset.Default));

        foreach (var choice in ChoiceItems)
        {
            var captured = choice;
            var command = captured.Preset == CoverPagePreset.Default
                ? defaultCommand
                : new ActionRibbonCommand(() => ports.InsertPreset(captured.Preset));
            bindings.Register(captured.CommandId, command);
            bindings.Register(captured.LegacyCommandId, command);
        }
    }
}
