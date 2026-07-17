using Free.Shared.Ribbon;
using FreeX.App.Presentation.InteractionValidation;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia;

internal static class ShortcutInteractionValidationCatalog
{
    private static readonly IReadOnlySet<string> BackstageKeytips = new HashSet<string>(
        ["H", "N", "O", "S", "A", "P", "E", "I", "R", "C", "D", "T", "Z"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> ContextualTabIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JA"] = "PivotTableAnalyzeTab",
            ["JD"] = "PivotTableDesignTab",
            ["JC"] = "ChartDesignTab",
            ["JF"] = "ChartFormatTab",
            ["JS"] = "ShapeFormatTab",
            ["JP"] = "PictureFormatTab",
        };

    internal static bool TryResolveRibbonKeytipInteraction(
        ShortcutInteractionDescriptor interaction,
        out string route)
    {
        route = "";
        if (interaction.Kind != ShortcutInteractionKind.RibbonKeytipSequence ||
            !TryParseDisplayTokens(interaction.DisplayText, out var tokens))
        {
            return false;
        }

        if (tokens.Count == 1 && tokens[0] is "1" or "2" or "3")
        {
            route = $"qat:{tokens[0]}";
            return true;
        }

        if (tokens[0].Equals("F", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Count == 1)
            {
                route = "backstage";
                return true;
            }

            if (tokens.Count == 2 && BackstageKeytips.Contains(tokens[1]))
            {
                route = $"backstage:{tokens[1]}";
                return true;
            }

            return false;
        }

        if (tokens.Count == 2 &&
            ContextualTabIds.TryGetValue(string.Concat(tokens), out var contextualTabId) &&
            FreeXRibbon.Build().FindTab(contextualTabId) is not null)
        {
            route = $"tab:{contextualTabId}";
            return true;
        }

        var definition = FreeXRibbon.Build();
        if (tokens.SequenceEqual(["N", "SH", "R"], StringComparer.OrdinalIgnoreCase))
        {
            var shapes = definition.FindTab("DrawTab")?.Groups
                .SelectMany(group => group.Controls)
                .FirstOrDefault(control => control.KeyTip == "SH");
            if (shapes is not null)
            {
                route = "dynamic-menu:shape.rectangle";
                return true;
            }
        }

        var tab = definition.Tabs.FirstOrDefault(candidate =>
            string.Equals(candidate.KeyTip, tokens[0], StringComparison.OrdinalIgnoreCase));
        if (tab is null)
            return false;
        if (tokens.Count == 1)
        {
            route = $"tab:{tab.Id}";
            return true;
        }

        if (tab.Id == "InsertTab" &&
            tokens.Count == 2 &&
            tokens[1].Equals("CH", StringComparison.OrdinalIgnoreCase) &&
            tab.FindGroup("InsertChartsGroup") is not null)
        {
            route = "group:InsertChartsGroup";
            return true;
        }

        var control = tab.Groups
            .SelectMany(group => group.Controls)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.KeyTip, tokens[1], StringComparison.OrdinalIgnoreCase));
        if (control is null)
            return false;
        if (tokens.Count == 2)
        {
            route = $"command:{control.CommandId.Value}";
            return true;
        }

        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null || !TryResolveMenu(menu.Items, tokens.Skip(2).ToArray(), out var commandId))
            return false;

        route = $"menu:{commandId.Value}";
        return true;
    }

    private static bool TryResolveMenu(
        IReadOnlyList<RibbonMenuItem> items,
        IReadOnlyList<string> tokens,
        out RibbonCommandId commandId)
    {
        commandId = default;
        if (tokens.Count == 0)
            return false;

        var item = items.FirstOrDefault(candidate =>
            candidate.Kind == RibbonMenuItemKind.Command &&
            string.Equals(candidate.KeyTip, tokens[0], StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return false;
        if (tokens.Count == 1)
        {
            if (item.CommandId is not { } resolvedCommandId)
                return false;
            commandId = resolvedCommandId;
            return true;
        }

        return TryResolveMenu(item.Children, tokens.Skip(1).ToArray(), out commandId);
    }

    private static bool TryParseDisplayTokens(string displayText, out IReadOnlyList<string> tokens)
    {
        const string twoStepPrefix = "Alt, then ";
        if (displayText.StartsWith(twoStepPrefix, StringComparison.Ordinal))
        {
            tokens = [displayText[twoStepPrefix.Length..]];
            return true;
        }

        const string sequencePrefix = "Alt,";
        if (displayText.StartsWith(sequencePrefix, StringComparison.Ordinal))
        {
            tokens = displayText[sequencePrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.Count > 0;
        }

        tokens = [];
        return false;
    }
}
