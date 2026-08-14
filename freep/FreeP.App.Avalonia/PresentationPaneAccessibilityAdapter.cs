using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Avalonia-only writer for the shared presentation-pane accessibility contract.</summary>
internal sealed class PresentationPaneAccessibilityAdapter
{
    private readonly PresentationPaneAccessibilityNativeSession<Control> _nativeSession =
        new(WritePane);

    public void ApplyPane(Control control, string paneId, bool isVisible, int itemCount = 0, int selectedIndex = -1)
        => _nativeSession.ApplyPane(control, paneId, isVisible, itemCount, selectedIndex);

    public static void ApplyPaneMetadata(
        Control control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
        => PresentationPaneAccessibilityNativeSession<Control>.ApplyPaneMetadata(
            control,
            paneId,
            isVisible,
            itemCount,
            selectedIndex,
            WritePane);

    private static void WritePane(
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
        PresentationPaneAccessibilityItemPlan plan)
        => PresentationPaneAccessibilityNativeSession<Control>.ApplyItem(
            control,
            plan,
            WriteItem);

    private static void WriteItem(
        Control control,
        PresentationPaneAccessibilityItemProjection item)
    {
        AutomationProperties.SetAutomationId(control, item.AutomationId);
        AutomationProperties.SetName(control, item.Name);
        AutomationProperties.SetHelpText(control, item.HelpText);
        AutomationProperties.SetItemStatus(control, item.ItemStatus);
    }

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        _nativeSession.BuildSnapshot();

    public string SerializeSnapshot() =>
        _nativeSession.SerializeSnapshot();
}
