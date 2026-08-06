using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Bundles the common WPF Backstage kit, pane composer, and pane-spec planner used by sister apps.
/// </summary>
public sealed class SisterBackstagePaneResources
{
    public SisterBackstagePaneResources(
        Color linkColor,
        double tileWidth,
        double tileHeight,
        SisterBackstagePaneTextSpec text,
        BackstagePaneComposerProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        Kit = new BackstageVisualKit(linkColor, tileWidth, tileHeight);
        Panes = new BackstagePaneComposer(Kit, profile);
        PaneSpecs = new SisterBackstagePaneSpecPlanner(text);
    }

    public BackstageVisualKit Kit { get; }

    public BackstagePaneComposer Panes { get; }

    public SisterBackstagePaneSpecPlanner PaneSpecs { get; }

}
