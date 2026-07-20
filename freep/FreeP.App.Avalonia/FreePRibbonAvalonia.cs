using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Ribbon.Definitions;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia host adapter for the shared FreeP ribbon definition.
/// </summary>
internal static class FreePRibbonAvalonia
{
    public static RibbonDefinition Build() =>
        AddChartDataCommand(FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Avalonia));

    private static RibbonDefinition AddChartDataCommand(RibbonDefinition definition)
        => AddCommandToGroup(definition, "insert", AddChartDataCommand);

    private static RibbonDefinition AddCommandToGroup(
        RibbonDefinition definition,
        string tabId,
        Func<RibbonGroup, RibbonGroup> addCommand)
    {
        var tabs = definition.Tabs
            .Select(tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal)
                ? tab with { Groups = tab.Groups.Select(addCommand).ToArray() }
                : tab)
            .ToArray();

        return definition with { Tabs = tabs };
    }

    private static RibbonGroup AddChartDataCommand(RibbonGroup group)
    {
        if (!string.Equals(group.Id, "charts", StringComparison.Ordinal)
            || group.Controls.Any(control => string.Equals(
                control.CommandId.Value,
                ChartDataDialogPlanner.EditDataCommandId,
                StringComparison.Ordinal)))
        {
            return group;
        }

        var controls = group.Controls
            .Append(new RibbonButton(
                ChartDataDialogPlanner.EditDataCommandId,
                ChartDataDialogPlanner.EditDataCommandLabel)
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartTitle),
                KeyTip = "E",
            })
            .ToArray();

        return group with { Controls = controls };
    }
}
