using Avalonia.Controls;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAppClientFrameSpec(
    Control Ribbon,
    Control WorkArea,
    Control StatusBar,
    IReadOnlyList<Control>? BottomPanelsAboveStatus = null,
    IReadOnlyList<Control>? TopPanelsBelowRibbon = null);

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

        var root = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(spec.Ribbon, Dock.Top);
        root.Children.Add(spec.Ribbon);

        foreach (var panel in spec.TopPanelsBelowRibbon ?? [])
        {
            ArgumentNullException.ThrowIfNull(panel);
            DockPanel.SetDock(panel, Dock.Top);
            root.Children.Add(panel);
        }

        DockPanel.SetDock(spec.StatusBar, Dock.Bottom);
        root.Children.Add(spec.StatusBar);

        foreach (var panel in spec.BottomPanelsAboveStatus ?? [])
        {
            ArgumentNullException.ThrowIfNull(panel);
            DockPanel.SetDock(panel, Dock.Bottom);
            root.Children.Add(panel);
        }

        root.Children.Add(spec.WorkArea);

        return new SisterAppClientFrameBuildResult(root);
    }
}
