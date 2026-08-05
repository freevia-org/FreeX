using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon.KeyTips;
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

        AssignUniqueKeyTips(items.OfType<MenuItem>());
        return items;
    }

    private static void AssignUniqueKeyTips(IEnumerable<MenuItem> items)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var keyTip = RibbonKeyTipText.CreateUniqueKeyTip(item.Header?.ToString(), used);
            item.InputGesture = KeyGesture.Parse(keyTip);
            used.Add(keyTip);
        }
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
                    InputGesture = KeyGesture.Parse(ExtractAccessKey(command.AccessHeader)),
                    Tag = pointIndex,
                };
                item.ToggleType = MenuItemToggleType.CheckBox;
                AutomationProperties.SetName(item, command.Header);
                item.Click += (_, _) => dispatch();
                return (Control)item;
            })
            .ToArray();
    }

    private static string ExtractAccessKey(string? accessHeader)
    {
        if (string.IsNullOrWhiteSpace(accessHeader))
            return string.Empty;

        var marker = accessHeader.IndexOf('_');
        return marker >= 0 && marker + 1 < accessHeader.Length
            ? accessHeader[(marker + 1)..].Substring(0, 1)
            : string.Empty;
    }
}
