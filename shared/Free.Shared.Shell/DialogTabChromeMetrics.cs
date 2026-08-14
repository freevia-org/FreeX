namespace Free.Shared.Shell;

/// <summary>
/// Host-neutral geometry for classic tabbed-dialog chrome.
/// </summary>
public static class DialogTabChromeMetrics
{
    /// <summary>The classic Windows border around the selected tab pane.</summary>
    public const string PaneBorderHex = "#C0C0C0";

    /// <summary>The classic Windows border around an inactive tab header.</summary>
    public const string InactiveTabBorderHex = "#808080";

    /// <summary>The classic Windows fill behind an inactive tab header.</summary>
    public const string InactiveTabBackgroundHex = "#F5F5F5";

    /// <summary>The shared selected-tab and content-pane surface.</summary>
    public const string SelectedTabBackgroundHex = "#FFFFFF";

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
