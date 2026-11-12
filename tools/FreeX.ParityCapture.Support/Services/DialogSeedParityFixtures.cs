using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Shared deterministic seed for the paired Custom Sort captures: both shells open the dialog over the
/// same demo-workbook block so the column choices and the header auto-detection agree.
/// </summary>
public static class SortDialogParityFixture
{
    /// <summary>A1:E5 of the parity demo sheet (header row plus four data rows).</summary>
    public static GridRange CreateRange(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 5));

    /// <summary>Non-default Sort Options state so every control shows a seeded value.</summary>
    public static SortDialogOptions CreateOptions() =>
        new(
            CaseSensitive: true,
            LeftToRight: true,
            FirstKeySortOrder: "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec");
}

/// <summary>Shared pivot source headers for the paired Insert Slicer / Insert Timeline captures.</summary>
public static class PivotControlPickerParityFixture
{
    public static IReadOnlyList<string> Headers { get; } = ["Region", "Product", "Units", "Price", "Revenue"];

    /// <summary>The field both shells preselect (the first source header).</summary>
    public static string SelectedField => Headers[0];
}

/// <summary>Shared chart formatting for the paired Format Chart Area captures.</summary>
public static class ChartAreaParityFixture
{
    public static void Apply(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.ShowLegend = true;
        chart.LegendPosition = ChartLegendPosition.Right;
        chart.ChartAreaFillColor = new CellColor(255, 255, 255);
        chart.PlotAreaFillColor = new CellColor(248, 250, 252);
    }
}
