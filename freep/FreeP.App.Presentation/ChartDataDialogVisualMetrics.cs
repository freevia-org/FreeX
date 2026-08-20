namespace FreeP.App.Compositor;

/// <summary>
/// Cross-host geometry for the chart data editor. WPF's <c>Window.Width</c> and
/// <c>Window.Height</c> include native non-client chrome; Avalonia's values describe
/// the client surface. These values keep the app-owned edit surface identical.
/// </summary>
public static class ChartDataDialogVisualMetrics
{
    public const double WpfWindowWidth = ChartDataDialogPlanner.DefaultDialogWidth;
    public const double WpfWindowHeight = ChartDataDialogPlanner.DefaultDialogHeight;
    public const double AvaloniaWindowWidth = 625.3333333333334;
    public const double AvaloniaWindowHeight = 402.6666666666667;

    public const double ToolbarButtonRightMargin = 4;
    public const double ToolbarGroupGap = 12;
    public const double ChartTypeWidth = 170;
    public const double ChartTypeHeight = 24;
    public const double TableHeaderHeight = 32;
    public const double TableCellHeight = 17;

    public static double ToolbarButtonWidth(ChartDataDialogActionId action) => action switch
    {
        ChartDataDialogActionId.AddSeries => 83,
        ChartDataDialogActionId.RemoveSeries => 83,
        ChartDataDialogActionId.MoveSeriesUp => 100,
        ChartDataDialogActionId.MoveSeriesDown => 118,
        ChartDataDialogActionId.AddCategory => 83,
        ChartDataDialogActionId.RemoveCategory => 84,
        ChartDataDialogActionId.MoveCategoryLeft => 124,
        ChartDataDialogActionId.MoveCategoryRight => 127,
        ChartDataDialogActionId.SwitchRowsAndColumns => 123,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
