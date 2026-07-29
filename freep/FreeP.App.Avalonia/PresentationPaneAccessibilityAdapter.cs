using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Avalonia-only writer for the shared presentation-pane accessibility contract.</summary>
internal sealed class PresentationPaneAccessibilityAdapter
{
    private readonly Dictionary<string, PresentationPaneAccessibilityState> _states = new(StringComparer.Ordinal);

    public void ApplyPane(Control control, string paneId, bool isVisible, int itemCount = 0, int selectedIndex = -1)
    {
        ApplyPaneMetadata(control, paneId, isVisible, itemCount, selectedIndex);
        _states[paneId] = new PresentationPaneAccessibilityState(paneId, isVisible, itemCount, selectedIndex);
    }

    public static void ApplyPaneMetadata(
        Control control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
    {
        var descriptor = PresentationPaneAccessibilityPlanner.Get(paneId);
        // Pane hosts are keyboard landmarks in the WPF shell. Keep the
        // Avalonia route explicit so hidden panes cannot capture Tab and the
        // shared planner order remains observable by assistive technology.
        control.Focusable = isVisible;
        control.IsTabStop = isVisible;
        control.TabIndex = descriptor.Order + 1;
        AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
        AutomationProperties.SetName(control, descriptor.Name);
        AutomationProperties.SetHelpText(control, descriptor.HelpText);
        AutomationProperties.SetItemStatus(control, FormatStatus(isVisible, descriptor.Order));
    }

    public static void ApplyItem(
        Control control,
        string paneId,
        int index,
        string name,
        string? state = null,
        string? stableKey = null)
    {
        var item = PresentationPaneAccessibilityPlanner.Item(paneId, index, name, state, stableKey);
        AutomationProperties.SetAutomationId(control, item.AutomationId);
        AutomationProperties.SetName(control, item.Name);
        AutomationProperties.SetHelpText(control, item.HelpText);
        AutomationProperties.SetItemStatus(control, FormatItemStatus(item));
    }

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        PresentationPaneAccessibilityPlanner.BuildSnapshot(_states.Values);

    public string SerializeSnapshot() =>
        PresentationPaneAccessibilityPlanner.SerializeSnapshot(_states.Values);

    private static string FormatStatus(bool isVisible, int order) =>
        $"{(isVisible ? "Visible" : "Hidden")}; Order {order + 1}";

    private static string FormatItemStatus(PresentationPaneAccessibilityItemDescriptor item) =>
        string.IsNullOrWhiteSpace(item.State)
            ? $"Order {item.Order + 1}"
            : $"{item.State}; Order {item.Order + 1}";
}
