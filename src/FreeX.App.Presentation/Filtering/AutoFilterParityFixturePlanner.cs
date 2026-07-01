using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public sealed record AutoFilterParityFixturePlan(GridRange Range, AutoFilterMenuPlan MenuPlan);

public static class AutoFilterParityFixturePlanner
{
    private static readonly string[] Headers = ["score", "name", "date", "note"];

    private static readonly object?[][] Rows =
    [
        [1d, "North", "2026-06-01", "alpha"],
        [2d, "South", "2026-06-02", "beta"],
        [3d, "East", "2026-06-03", "gamma"],
        [4d, "West", "2026-06-04", "delta"],
        [null, "Blank score", "2026-06-05", "blank"],
    ];

    public static AutoFilterParityFixturePlan CreateFixturePlan(
        Workbook workbook,
        Sheet sheet,
        IAutoFilterMenuTextProvider textProvider,
        string blankDisplayText)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textProvider);
        ArgumentNullException.ThrowIfNull(blankDisplayText);

        var range = SeedRange(sheet);
        if (!AutoFilterDropdownMenuPlanner.TryPlan(range, range.Start, out var plan))
            throw new InvalidOperationException("Could not create AutoFilter parity plan.");

        var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook,
            sheet,
            plan,
            textProvider,
            blankDisplayText);
        return new AutoFilterParityFixturePlan(range, menuPlan);
    }

    private static GridRange SeedRange(Sheet sheet)
    {
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));

        foreach (var address in range.AllCells())
            sheet.ClearCell(address);

        for (var col = 0; col < Headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(Headers[col]));

        for (var row = 0; row < Rows.Length; row++)
        {
            for (var col = 0; col < Headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                switch (Rows[row][col])
                {
                    case double number:
                        sheet.SetCell(address, new NumberValue(number));
                        break;
                    case string text:
                        sheet.SetCell(address, new TextValue(text));
                        break;
                    case null:
                        sheet.ClearCell(address);
                        break;
                }
            }
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        return range;
    }
}
