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
}

/// <summary>
/// Shared English fallback labels for status-bar customize commands. WPF resolves these resource keys through
/// <c>UiText</c>; portable shells without that resource system can use this table and still mirror the same text.
/// </summary>
public static class StatusBarCustomizeLabelPlanner
{
    public static string EnglishHeader(string resourceKey) =>
        resourceKey switch
        {
            StatusBarCustomizeResourceKeys.CustomizeStatusBar => "Customize Status Bar",
            StatusBarCustomizeResourceKeys.CellMode => "Cell Mode",
            StatusBarCustomizeResourceKeys.EndMode => "End Mode",
            StatusBarCustomizeResourceKeys.SelectionMode => "Selection Mode",
            StatusBarCustomizeResourceKeys.PageNumber => "Page Number",
            StatusBarCustomizeResourceKeys.Average => "Average",
            StatusBarCustomizeResourceKeys.Count => "Count",
            StatusBarCustomizeResourceKeys.NumericalCount => "Numerical Count",
            StatusBarCustomizeResourceKeys.Minimum => "Minimum",
            StatusBarCustomizeResourceKeys.Maximum => "Maximum",
            StatusBarCustomizeResourceKeys.Sum => "Sum",
            StatusBarCustomizeResourceKeys.ViewShortcuts => "View Shortcuts",
            StatusBarCustomizeResourceKeys.Zoom => "Zoom",
            StatusBarCustomizeResourceKeys.ZoomSlider => "Zoom Slider",
            _ => resourceKey
        };
}
