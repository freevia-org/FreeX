using Free.Shared.Ribbon;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// The editor effects Home &gt; Text Effects and Typography needs from a renderer: focus the editor,
/// then apply a pure run-formatting edit to the selection through that renderer's normal undoable
/// character-formatting path.
/// </summary>
public sealed record TextEffectsRibbonPorts(
    Action PrepareExecution,
    Action<Func<RunFormatting, RunFormatting>> ApplyRunFormatting);

/// <summary>
/// Owns the menu structure and the command registration for Home &gt; Text Effects and Typography, so
/// the WPF and Avalonia ribbons expose the same entries backed by the same edits.
/// </summary>
public static class TextEffectsRibbonWorkflow
{
    public static string RootLabel => Loc.Get("Ribbon_Command_TextEffects_Label");

    /// <summary>The dropdown contents: preset tiles, then one submenu per effect family, then Clear.</summary>
    public static RibbonMenu BuildMenu()
    {
        var items = new List<RibbonMenuItem>();
        AddChoices(items, TextEffectsRibbonCatalog.Presets);
        items.Add(RibbonMenuItem.Separator());
        items.Add(Submenu("TextEffects_Menu_Outline", "O", TextEffectsRibbonCatalog.Outline));
        items.Add(Submenu("TextEffects_Menu_Shadow", "S", TextEffectsRibbonCatalog.Shadow));
        items.Add(Submenu("TextEffects_Menu_Reflection", "R", TextEffectsRibbonCatalog.Reflection));
        items.Add(Submenu("TextEffects_Menu_Glow", "G", TextEffectsRibbonCatalog.Glow));
        items.Add(RibbonMenuItem.Separator());
        items.Add(Submenu("TextEffects_Menu_NumberStyles", "N", TextEffectsRibbonCatalog.NumberStyles));
        items.Add(Submenu("TextEffects_Menu_Ligatures", "L", TextEffectsRibbonCatalog.Ligatures));
        items.Add(Submenu("TextEffects_Menu_StylisticSets", "T", TextEffectsRibbonCatalog.StylisticSets));
        items.Add(RibbonMenuItem.Separator());
        items.Add(new RibbonMenuItem(
            TextEffectsRibbonCatalog.Clear.Label,
            new RibbonCommandId(TextEffectsRibbonCatalog.Clear.CommandId),
            KeyTip: "C"));
        return new RibbonMenu(items);
    }

    public static void Register(IRibbonCommandRegistry registry, TextEffectsRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        // The root only opens its dropdown; every entry is its own command.
        registry.Register(TextEffectsRibbonCatalog.RootCommandId, new ChoiceCommand(ports, null));
        foreach (var choice in TextEffectsRibbonCatalog.All)
            registry.Register(choice.CommandId, new ChoiceCommand(ports, choice));
    }

    private static RibbonMenuItem Submenu(string headerKey, string keyTip, IReadOnlyList<TextEffectChoice> choices)
    {
        var children = new List<RibbonMenuItem>(choices.Count + 3);
        AddChoices(children, choices);
        return new RibbonMenuItem(Loc.Get(headerKey), CommandId: null, KeyTip: keyTip, Children: children);
    }

    private static void AddChoices(List<RibbonMenuItem> items, IReadOnlyList<TextEffectChoice> choices)
    {
        foreach (var choice in choices)
        {
            if (choice.StartsNewGroup && items.Count > 0)
                items.Add(RibbonMenuItem.Separator());
            items.Add(new RibbonMenuItem(choice.Label, new RibbonCommandId(choice.CommandId)));
        }
    }

    private sealed class ChoiceCommand(TextEffectsRibbonPorts ports, TextEffectChoice? choice) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (choice is null)
                return;

            ports.PrepareExecution();
            ports.ApplyRunFormatting(choice.Apply);
        }
    }
}
