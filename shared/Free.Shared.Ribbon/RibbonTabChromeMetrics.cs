namespace Free.Shared.Ribbon;

/// <summary>
/// Platform-neutral geometry for the shared flat ribbon tab strip.
/// </summary>
public static class RibbonTabChromeMetrics
{
    /// <summary>The compact header height used by both ribbon renderers.</summary>
    public const double HeaderHeight = 28;

    /// <summary>The active-tab accent indicator thickness.</summary>
    public const double SelectedUnderlineThickness = 3;

    /// <summary>The horizontal inset around a tab header label.</summary>
    public const double HeaderHorizontalPadding = 5;

    /// <summary>The vertical inset used by the WPF header template.</summary>
    public const double HeaderVerticalPadding = 4;

    /// <summary>The shared ribbon tab label font size.</summary>
    public const double FontSize = 12;

    /// <summary>The trailing space reserved between adjacent tab templates.</summary>
    public const double InterTabGap = 1;
}
