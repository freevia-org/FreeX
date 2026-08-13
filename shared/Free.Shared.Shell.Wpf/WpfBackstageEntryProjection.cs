using System.Windows;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Lossless adapter between the portable Backstage entry plan and the legacy WPF entry surface.
/// </summary>
public static class WpfBackstageEntryProjection
{
    public static BackstageEntry FromPlan(SisterBackstageEntryPlan<UIElement> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new BackstageEntry
        {
            Label = plan.Label,
            StableId = plan.StableId,
            Icon = plan.Icon,
            IconCommandName = plan.IconCommandName,
            ContentFactory = plan.ContentFactory,
            Action = plan.Action,
            Separator = plan.Kind == SisterBackstageEntryKind.Divider,
            DockBottom = plan.DockBottom,
            KeyTip = plan.KeyTip,
            AutomationId = plan.AutomationId,
            AutomationName = plan.AutomationName,
            AutomationHelpText = plan.AutomationHelpText,
            TooltipTitle = plan.TooltipTitle,
            TooltipDescription = plan.TooltipDescription,
            DismissOnActivate = plan.DismissOnActivate,
        };
    }

    public static SisterBackstageEntryPlan<UIElement> ToPlan(BackstageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var kind = entry.Separator
            ? SisterBackstageEntryKind.Divider
            : entry.ContentFactory is not null
                ? SisterBackstageEntryKind.Pane
                : SisterBackstageEntryKind.Command;

        return new SisterBackstageEntryPlan<UIElement>(
            entry.Label,
            entry.Icon,
            kind,
            entry.ContentFactory,
            entry.Action,
            entry.DockBottom,
            entry.IconCommandName)
        {
            StableId = entry.StableId,
            KeyTip = entry.KeyTip,
            AutomationId = entry.AutomationId,
            AutomationName = entry.AutomationName,
            AutomationHelpText = entry.AutomationHelpText,
            TooltipTitle = entry.TooltipTitle,
            TooltipDescription = entry.TooltipDescription,
            DismissOnActivate = entry.DismissOnActivate,
        };
    }
}
