using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

internal static class AvaloniaPivotFieldContextMenu
{
    public static IReadOnlyList<Control> BuildItems(
        bool includeRemove,
        Func<string, string> resolveHeader,
        Action<PivotFieldContextMenuAction> dispatch)
    {
        var items = new List<Control>();
        foreach (var command in PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove))
        {
            if (command.IsSeparator)
            {
                items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = resolveHeader(command.ResourceKey),
                IsEnabled = command.IsEnabled,
                InputGesture = KeyGesture.Parse(command.KeyTip),
                Tag = command.Action,
            };
            AutomationProperties.SetName(item, command.CommandName);
            item.Click += (_, _) => dispatch(command.Action);
            items.Add(item);
        }

        return items;
    }
}

internal static class AvaloniaPivotChartFieldContextMenu
{
    public static IReadOnlyList<Control> BuildItems(
        PivotChartFieldContextMenuState state,
        Action<PivotChartFieldContextMenuAction> dispatch)
    {
        var items = new List<Control>();
        foreach (var command in PivotChartFieldContextMenuPlanner.BuildCommands(state))
        {
            if (command.IsSeparator)
            {
                items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = command.Header,
                IsEnabled = command.IsEnabled,
                Tag = command.Action,
            };
            if (!string.IsNullOrWhiteSpace(command.ToolTip))
                ToolTip.SetTip(item, command.ToolTip);
            AutomationProperties.SetName(item, command.Header);
            if (command.Action is not PivotChartFieldContextMenuAction.None and
                not PivotChartFieldContextMenuAction.Summary)
            {
                item.Click += (_, _) => dispatch(command.Action);
            }
            items.Add(item);
        }

        return items;
    }
}

internal static class AvaloniaWaterfallPointContextMenu
{
    public static IReadOnlyList<Control> BuildItems(
        FreeX.Core.Model.ChartModel chart,
        int pointIndex,
        Action dispatch)
    {
        return WaterfallChartContextMenuPlanner.BuildCommands(chart, pointIndex)
            .Select(command =>
            {
                var item = new MenuItem
                {
                    Header = command.AccessHeader,
                    IsEnabled = command.IsEnabled,
                    IsChecked = command.IsChecked,
                    Tag = pointIndex,
                };
                item.ToggleType = MenuItemToggleType.CheckBox;
                AutomationProperties.SetName(item, command.Header);
                item.Click += (_, _) => dispatch();
                return (Control)item;
            })
            .ToArray();
    }
}
