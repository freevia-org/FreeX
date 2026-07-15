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
        AddVideoExportCommand(AddNotesPagePdfExportCommand(AddImageExportCommand(AddChartDataCommand(FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Avalonia)))));

    private static RibbonDefinition AddImageExportCommand(RibbonDefinition definition)
        => AddCommandToGroup(definition, "home", AddImageExportCommand);

    private static RibbonGroup AddImageExportCommand(RibbonGroup group)
    {
        if (!string.Equals(group.Id, "file", StringComparison.Ordinal)
            || group.Controls.Any(control => string.Equals(
                control.CommandId.Value,
                PresentationExportPlanner.ImageExportCommandId,
                StringComparison.Ordinal)))
        {
            return group;
        }

        var descriptor = PresentationExportPlanner.BuildFormatDescriptors()
            .Single(format => format.CommandId == PresentationExportPlanner.ImageExportCommandId);
        var controls = group.Controls
            .Append(new RibbonButton(
                PresentationExportPlanner.ImageExportCommandId,
                descriptor.DisplayName)
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture),
                KeyTip = "XI",
            })
            .ToArray();

        return group with { Controls = controls };
    }

    private static RibbonDefinition AddVideoExportCommand(RibbonDefinition definition)
        => AddCommandToGroup(definition, "home", AddVideoExportCommand);

    private static RibbonGroup AddVideoExportCommand(RibbonGroup group)
    {
        if (!string.Equals(group.Id, "file", StringComparison.Ordinal)
            || group.Controls.Any(control => string.Equals(
                control.CommandId.Value,
                PresentationExportPlanner.VideoExportCommandId,
                StringComparison.Ordinal)))
        {
            return group;
        }

        var descriptor = PresentationExportPlanner.BuildFormatDescriptors()
            .Single(format => format.CommandId == PresentationExportPlanner.VideoExportCommandId);
        var controls = group.Controls
            .Append(new RibbonButton(
                PresentationExportPlanner.VideoExportCommandId,
                descriptor.DisplayName)
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                KeyTip = "XV",
            })
            .ToArray();

        return group with { Controls = controls };
    }

    private static RibbonDefinition AddNotesPagePdfExportCommand(RibbonDefinition definition)
        => AddCommandToGroup(definition, "home", AddNotesPagePdfExportCommand);

    private static RibbonGroup AddNotesPagePdfExportCommand(RibbonGroup group)
    {
        if (!string.Equals(group.Id, "file", StringComparison.Ordinal)
            || group.Controls.Any(control => string.Equals(
                control.CommandId.Value,
                PresentationExportPlanner.NotesPagePdfExportCommandId,
                StringComparison.Ordinal)))
        {
            return group;
        }

        var descriptor = PresentationExportPlanner.BuildFormatDescriptors()
            .Single(format => format.CommandId == PresentationExportPlanner.NotesPagePdfExportCommandId);
        var controls = group.Controls
            .Append(new RibbonButton(
                PresentationExportPlanner.NotesPagePdfExportCommandId,
                descriptor.DisplayName)
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Print),
                KeyTip = "XN",
            })
            .ToArray();

        return group with { Controls = controls };
    }

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
