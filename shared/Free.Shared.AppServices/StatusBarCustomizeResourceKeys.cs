namespace Free.Shared.AppServices;

public static class StatusBarCustomizeResourceKeys
{
    public const string CustomizeStatusBar = "StatusBar_CustomizeStatusBar";
    public const string CellMode = "StatusBar_CellMode";
    public const string EndMode = "StatusBar_EndMode";
    public const string SelectionMode = "StatusBar_SelectionMode";
    public const string PageNumber = "StatusBar_PageNumber";
    public const string Average = "StatusBar_Average";
    public const string Count = "StatusBar_Count";
    public const string NumericalCount = "StatusBar_NumericalCount";
    public const string Minimum = "StatusBar_Minimum";
    public const string Maximum = "StatusBar_Maximum";
    public const string Sum = "StatusBar_Sum";
    public const string ViewShortcuts = "StatusBar_ViewShortcuts";
    public const string Zoom = "StatusBar_Zoom";
    public const string ZoomSlider = "StatusBar_ZoomSlider";

    public static IReadOnlyList<string> RequiredKeys { get; } =
    [
        CustomizeStatusBar,
        CellMode,
        EndMode,
        SelectionMode,
        PageNumber,
        Average,
        Count,
        NumericalCount,
        Minimum,
        Maximum,
        Sum,
        ViewShortcuts,
        Zoom,
        ZoomSlider
    ];
}
