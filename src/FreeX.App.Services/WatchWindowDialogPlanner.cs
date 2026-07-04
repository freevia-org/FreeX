namespace FreeX.App.Services;

public static class WatchWindowDialogPlanner
{
    public const string TitleKey = "WatchWindow_WatchWindow";
    public const string DialogAutomationId = "WatchWindowDialog";

    public const double BookColumnWidth = 90;
    public const double SheetColumnWidth = 110;
    public const double NameColumnWidth = 80;
    public const double CellColumnWidth = 70;
    public const double ValueColumnWidth = 120;
    public const double FormulaColumnWidth = 170;
    public const double ColumnsWidth =
        BookColumnWidth +
        SheetColumnWidth +
        NameColumnWidth +
        CellColumnWidth +
        ValueColumnWidth +
        FormulaColumnWidth;

    public const double ChromeAndPaddingWidth = 120;
    public const double Width = ColumnsWidth + ChromeAndPaddingWidth;
    public const double Height = 320;
    public const double MinWidth = ColumnsWidth + 80;
    public const double MinHeight = 220;
}
