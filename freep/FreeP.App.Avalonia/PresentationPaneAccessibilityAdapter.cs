using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Avalonia-only writer for the shared presentation-pane accessibility contract.</summary>
internal sealed class PresentationPaneAccessibilityAdapter
{
    private readonly PresentationPaneAccessibilitySession _session = new();

    public void ApplyPane(Control control, string paneId, bool isVisible, int itemCount = 0, int selectedIndex = -1)
    {
        ApplyPaneProjection(control, _session.UpdatePane(paneId, isVisible, itemCount, selectedIndex));
    }

    public static void ApplyPaneMetadata(
        Control control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
    {
        ApplyPaneProjection(
            control,
            PresentationPaneAccessibilityPlanner.ProjectPane(paneId, isVisible, itemCount, selectedIndex));
    }

    private static void ApplyPaneProjection(
        Control control,
        PresentationPaneAccessibilityPaneProjection pane)
    {
        // Pane hosts are keyboard landmarks in the WPF shell. Keep the
        // Avalonia route explicit so hidden panes cannot capture Tab and the
        // shared planner order remains observable by assistive technology.
        control.Focusable = pane.IsKeyboardNavigationEnabled;
        control.IsTabStop = pane.IsKeyboardNavigationEnabled;
        control.TabIndex = pane.KeyboardOrder;
        AutomationProperties.SetAutomationId(control, pane.AutomationId);
        AutomationProperties.SetName(control, pane.Name);
        AutomationProperties.SetHelpText(control, pane.HelpText);
        AutomationProperties.SetItemStatus(control, pane.ItemStatus);
    }

    public static void ApplyItem(
        Control control,
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
}
