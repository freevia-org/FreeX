using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAppClientFrameSpec(
    Control Ribbon,
    Control WorkArea,
    Control StatusBar,
    IReadOnlyList<Control>? BottomPanelsAboveStatus = null,
    IReadOnlyList<Control>? TopPanelsBelowRibbon = null)
{
    public Control Chrome => Ribbon;

    public IReadOnlyList<Control>? TopPanelsBelowChrome => TopPanelsBelowRibbon;

    public static SisterAppClientFrameSpec ForWorkArea(
        Control chrome,
        Control workArea,
        Control statusBar,
        IReadOnlyList<Control>? bottomPanelsAboveStatus = null,
        IReadOnlyList<Control>? topPanelsBelowChrome = null) =>
        new(chrome, workArea, statusBar, bottomPanelsAboveStatus, topPanelsBelowChrome);
}

public sealed record SisterAppClientFrameBuildResult(
    DockPanel Root);

/// <summary>
/// Builds the common Avalonia sister-app frame: ribbon at the top, app workarea filling the client,
/// and status chrome at the bottom. Apps keep their own ribbon definitions, content, and callbacks.
/// </summary>
public static class SisterAppClientFrameBuilder
{
    public static SisterAppClientFrameBuildResult Build(SisterAppClientFrameSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Ribbon);
        ArgumentNullException.ThrowIfNull(spec.WorkArea);
        ArgumentNullException.ThrowIfNull(spec.StatusBar);

        var topPanelsBelowChrome = spec.TopPanelsBelowChrome ?? [];
        var bottomPanelsAboveStatus = spec.BottomPanelsAboveStatus ?? [];
        var contract = SisterAppClientFrameContractPlanner.Plan(
            topPanelsBelowChrome.Count,
            bottomPanelsAboveStatus.Count);

        var root = new DockPanel { LastChildFill = true };

        foreach (var slot in contract.SlotsBeforeWorkArea)
            AddDocked(root, ResolveTopSlot(spec, topPanelsBelowChrome, slot), Dock.Top);

        foreach (var slot in contract.SlotsAfterWorkArea.Reverse())
            AddDocked(root, ResolveBottomSlot(spec, bottomPanelsAboveStatus, slot), Dock.Bottom);

        root.Children.Add(spec.WorkArea);

        return new SisterAppClientFrameBuildResult(root);
    }

    private static Control ResolveTopSlot(
        SisterAppClientFrameSpec spec,
        IReadOnlyList<Control> topPanelsBelowChrome,
        SisterAppClientFrameSlotPlan slot) => slot.Role switch
        {
            SisterAppClientFrameSlotRole.Chrome => spec.Chrome,
            SisterAppClientFrameSlotRole.TopPanelBelowChrome => topPanelsBelowChrome[slot.Index],
            _ => throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot.Role,
                "Only top frame slots can be docked above the workarea."),
        };

    private static Control ResolveBottomSlot(
        SisterAppClientFrameSpec spec,
        IReadOnlyList<Control> bottomPanelsAboveStatus,
        SisterAppClientFrameSlotPlan slot) => slot.Role switch
        {
            SisterAppClientFrameSlotRole.BottomPanelAboveStatus => bottomPanelsAboveStatus[slot.Index],
            SisterAppClientFrameSlotRole.StatusBar => spec.StatusBar,
            _ => throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot.Role,
                "Only bottom frame slots can be docked below the workarea."),
        };

    private static void AddDocked(DockPanel root, Control child, Dock dock)
    {
        ArgumentNullException.ThrowIfNull(child);

        DockPanel.SetDock(child, dock);
        root.Children.Add(child);
    }
}
