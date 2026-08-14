using System.Windows;
using System.Windows.Automation;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>WPF-only writer for the shared presentation-pane accessibility contract.</summary>
internal sealed class PresentationPaneAccessibilityAdapter
{
    private readonly PresentationPaneAccessibilityNativeSession<FrameworkElement> _nativeSession =
        new(WritePane);

    public void ApplyPane(FrameworkElement control, string paneId, bool isVisible, int itemCount = 0, int selectedIndex = -1)
        => _nativeSession.ApplyPane(control, paneId, isVisible, itemCount, selectedIndex);

    public static void ApplyPaneMetadata(
        FrameworkElement control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
        => PresentationPaneAccessibilityNativeSession<FrameworkElement>.ApplyPaneMetadata(
            control,
            paneId,
            isVisible,
            itemCount,
            selectedIndex,
            WritePane);

    public static void ApplyItem(
        FrameworkElement control,
        PresentationPaneAccessibilityItemPlan plan)
        => PresentationPaneAccessibilityNativeSession<FrameworkElement>.ApplyItem(
            control,
            plan,
            WriteItem);

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        _nativeSession.BuildSnapshot();

    public string SerializeSnapshot() =>
        _nativeSession.SerializeSnapshot();

    private static void WritePane(
        FrameworkElement control,
        PresentationPaneAccessibilityPaneProjection pane)
    {
        AutomationProperties.SetAutomationId(control, pane.AutomationId);
        AutomationProperties.SetName(control, pane.Name);
        AutomationProperties.SetHelpText(control, pane.HelpText);
        AutomationProperties.SetItemStatus(control, pane.ItemStatus);
    }

    private static void WriteItem(
        FrameworkElement control,
        PresentationPaneAccessibilityItemProjection item)
    {
        AutomationProperties.SetAutomationId(control, item.AutomationId);
        AutomationProperties.SetName(control, item.Name);
        AutomationProperties.SetHelpText(control, item.HelpText);
        AutomationProperties.SetItemStatus(control, item.ItemStatus);
    }
}
