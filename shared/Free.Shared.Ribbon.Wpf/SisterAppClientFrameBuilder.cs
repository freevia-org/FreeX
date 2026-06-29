using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Ribbon.Wpf;

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

        foreach (var slot in contract.Slots)
        {
            switch (slot.Role)
            {
                case SisterAppClientFrameSlotRole.Chrome:
                    AddRow(root, spec.Chrome, GridLength.Auto);
                    break;
                case SisterAppClientFrameSlotRole.TopPanelBelowChrome:
                    AddRow(root, topPanelsBelowChrome[slot.Index], GridLength.Auto);
                    break;
                case SisterAppClientFrameSlotRole.WorkArea:
                    AddRow(root, spec.WorkArea, new GridLength(1, GridUnitType.Star));
                    break;
                case SisterAppClientFrameSlotRole.BottomPanelAboveStatus:
                    AddRow(root, bottomPanelsAboveStatus[slot.Index], GridLength.Auto);
                    break;
                case SisterAppClientFrameSlotRole.StatusBar:
                    AddRow(root, spec.StatusBar, GridLength.Auto);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot.Role, "Unknown sister-app frame slot.");
            }
        }

        return new SisterAppClientFrameBuildResult(root);
    }

    private static void AddRow(Grid root, UIElement child, GridLength height)
    {
        ArgumentNullException.ThrowIfNull(child);

        var row = root.RowDefinitions.Count;
        root.RowDefinitions.Add(new RowDefinition { Height = height });
        Grid.SetRow(child, row);
        root.Children.Add(child);
    }
}
