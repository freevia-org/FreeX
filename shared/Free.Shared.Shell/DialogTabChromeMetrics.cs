namespace Free.Shared.Shell;

/// <summary>
/// Host-neutral geometry for classic tabbed-dialog chrome.
/// </summary>
public static class DialogTabChromeMetrics
{
    /// <summary>The shared one-pixel frame around the selected tab body.</summary>
    public const double PaneBorderThickness = 1;

    /// <summary>
    /// The selected header extends one pixel into the body frame so their shared edge has no gap
    /// and no second separating border.
    /// </summary>
    public const double SelectedTabContentOverlap = 1;

    /// <summary>Adjacent tab headers share their one-pixel side edge.</summary>
    public const double AdjacentTabOverlap = 1;
}
