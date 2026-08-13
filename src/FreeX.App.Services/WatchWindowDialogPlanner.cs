using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WatchWindowRowPlan(
    string Book,
    string Sheet,
    string Name,
    string Cell,
    string Value,
    string Formula,
    CellAddress Address);

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

    public static IReadOnlyList<WatchWindowRowPlan> CreateRows(
        IReadOnlyList<WatchWindowEntry> entries,
        string thisWorkbookText)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(thisWorkbookText);

        return entries
            .Select(entry => new WatchWindowRowPlan(
                thisWorkbookText,
                entry.SheetName,
                string.Empty,
                entry.Address.ToA1(),
                entry.ValueText,
                entry.FormulaText ?? string.Empty,
                entry.Address))
            .ToArray();
    }
}
