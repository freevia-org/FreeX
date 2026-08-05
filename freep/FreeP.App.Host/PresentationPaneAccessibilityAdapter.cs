using System.Windows;
using System.Windows.Automation;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>WPF-only writer for the shared presentation-pane accessibility contract.</summary>
internal sealed class PresentationPaneAccessibilityAdapter
{
    private readonly PresentationPaneAccessibilitySession _session = new();

    public void ApplyPane(FrameworkElement control, string paneId, bool isVisible, int itemCount = 0, int selectedIndex = -1)
    {
        ApplyPaneProjection(control, _session.UpdatePane(paneId, isVisible, itemCount, selectedIndex));
    }

    public static void ApplyPaneMetadata(
        FrameworkElement control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
    {
        ApplyPaneProjection(
            control,
            PresentationPaneAccessibilityPlanner.ProjectPane(paneId, isVisible, itemCount, selectedIndex));
    }

    public static void ApplyItem(
        FrameworkElement control,
        string paneId,
        int index,
        string name,
        string? state = null,
        string? stableKey = null)
    {
        var item = PresentationPaneAccessibilityPlanner.ProjectItem(paneId, index, name, state, stableKey);
        AutomationProperties.SetAutomationId(control, item.AutomationId);
        AutomationProperties.SetName(control, item.Name);
        AutomationProperties.SetHelpText(control, item.HelpText);
        AutomationProperties.SetItemStatus(control, item.ItemStatus);
    }

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        _session.BuildSnapshot();

    public string SerializeSnapshot() =>
        _session.SerializeSnapshot();

    private static void ApplyPaneProjection(
        FrameworkElement control,
        PresentationPaneAccessibilityPaneProjection pane)
    {
        AutomationProperties.SetAutomationId(control, pane.AutomationId);
        AutomationProperties.SetName(control, pane.Name);
        AutomationProperties.SetHelpText(control, pane.HelpText);
        AutomationProperties.SetItemStatus(control, pane.ItemStatus);
    }
}
