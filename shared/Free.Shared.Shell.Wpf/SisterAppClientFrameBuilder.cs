using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterAppClientFrameSpec(
    UIElement Chrome,
    UIElement WorkArea,
    UIElement StatusBar,
    IReadOnlyList<UIElement>? BottomPanelsAboveStatus = null,
    IReadOnlyList<UIElement>? TopPanelsBelowChrome = null);

public sealed record SisterAppClientFrameBuildResult(
    Grid Root);

/// <summary>
/// Builds the common sister-app client frame below the custom title bar: chrome, workarea, status bar.
/// </summary>
public static class SisterAppClientFrameBuilder
{
    public static SisterAppClientFrameBuildResult Build(SisterAppClientFrameSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Chrome);
        ArgumentNullException.ThrowIfNull(spec.WorkArea);
        ArgumentNullException.ThrowIfNull(spec.StatusBar);

        var topPanelsBelowChrome = spec.TopPanelsBelowChrome ?? [];
        var bottomPanelsAboveStatus = spec.BottomPanelsAboveStatus ?? [];
        var contract = SisterAppClientFrameContractPlanner.Plan(
            topPanelsBelowChrome.Count,
            bottomPanelsAboveStatus.Count);

        var root = new Grid();

        foreach (var slot in contract.SlotsBeforeWorkArea)
        {
            AddRow(
                root,
                ResolveAutoRow(spec, topPanelsBelowChrome, bottomPanelsAboveStatus, slot),
                GridLength.Auto);
        }

        AddRow(root, spec.WorkArea, new GridLength(1, GridUnitType.Star));

        foreach (var slot in contract.SlotsAfterWorkArea)
        {
            AddRow(
                root,
                ResolveAutoRow(spec, topPanelsBelowChrome, bottomPanelsAboveStatus, slot),
                GridLength.Auto);
        }

        return new SisterAppClientFrameBuildResult(root);
    }

    private static UIElement ResolveAutoRow(
        SisterAppClientFrameSpec spec,
        IReadOnlyList<UIElement> topPanelsBelowChrome,
        IReadOnlyList<UIElement> bottomPanelsAboveStatus,
        SisterAppClientFrameSlotPlan slot) => slot.Role switch
        {
            SisterAppClientFrameSlotRole.Chrome => spec.Chrome,
            SisterAppClientFrameSlotRole.TopPanelBelowChrome => topPanelsBelowChrome[slot.Index],
            SisterAppClientFrameSlotRole.BottomPanelAboveStatus => bottomPanelsAboveStatus[slot.Index],
            SisterAppClientFrameSlotRole.StatusBar => spec.StatusBar,
            _ => throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot.Role,
                "Only fixed-height frame slots can be resolved as automatic rows."),
        };

    private static void AddRow(Grid root, UIElement child, GridLength height)
    {
        ArgumentNullException.ThrowIfNull(child);

        var row = root.RowDefinitions.Count;
        root.RowDefinitions.Add(new RowDefinition { Height = height });
        Grid.SetRow(child, row);
        root.Children.Add(child);
    }
}
