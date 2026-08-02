namespace FreeX.App.Presentation.ScenarioManager;

public static class ScenarioManagerDialogLayout
{
    public const double DialogWidth = 360;
    public const double DialogHeight = 420;
    public const double ScenarioListHeight = 118;
    public const double FieldLabelColumnWidth = 92;
    public const double ActionButtonWidth = 82;
    public const double CloseButtonWidth = 72;
    // These are the WPF ScenarioManagerDialog margins. The Avalonia route uses a
    // smaller route-local control style so the complete WPF rhythm still fits the
    // fixed 360x420 client frame.
    public const double ScenarioListHeaderBottomMargin = 4;
    public const double FieldBottomMargin = 8;
    public const double LockedCheckBoxBottomMargin = 6;
    public const double HiddenCheckBoxBottomMargin = 8;
    public const double GroupTopMargin = 12;
    public const double CloseRowTopMargin = 12;
    public const int RootRowCount = 3;
    public const int FieldRowCount = 6;
}
