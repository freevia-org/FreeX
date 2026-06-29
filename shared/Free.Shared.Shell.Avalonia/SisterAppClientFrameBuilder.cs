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

        foreach (var slot in contract.Slots)
        {
            switch (slot.Role)
            {
                case SisterAppClientFrameSlotRole.Chrome:
                    AddDocked(root, spec.Chrome, Dock.Top);
                    break;
                case SisterAppClientFrameSlotRole.TopPanelBelowChrome:
                    AddDocked(root, topPanelsBelowChrome[slot.Index], Dock.Top);
                    break;
            }
        }

        AddDocked(root, spec.StatusBar, Dock.Bottom);

        foreach (var panel in bottomPanelsAboveStatus)
            AddDocked(root, panel, Dock.Bottom);

        root.Children.Add(spec.WorkArea);

        return new SisterAppClientFrameBuildResult(root);
    }

    private static void AddDocked(DockPanel root, Control child, Dock dock)
    {
        ArgumentNullException.ThrowIfNull(child);

        DockPanel.SetDock(child, dock);
        root.Children.Add(child);
    }
}
