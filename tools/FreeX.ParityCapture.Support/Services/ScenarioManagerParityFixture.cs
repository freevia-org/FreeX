using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class ScenarioManagerParityFixture
{
    public const string ScenarioName = "Tour Base Case";
    public const string ScenarioComment = "Seeded scenario for visual parity.";

    public static GridRange ChangingCellsRange(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 3, 3));

    public static void Seed(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        for (var index = workbook.Scenarios.Count - 1; index >= 0; index--)
        {
            if (string.Equals(workbook.Scenarios[index].Name, ScenarioName, StringComparison.OrdinalIgnoreCase))
                workbook.Scenarios.RemoveAt(index);
        }

        var sheet = workbook.GetSheet(sheetId);
        var changingCells = new List<ScenarioCellValue>();
        foreach (var address in ChangingCellsRange(sheetId).AllCells())
            changingCells.Add(new ScenarioCellValue(address, sheet?.GetValue(address) ?? BlankValue.Instance));

        workbook.Scenarios.Insert(0, new WorkbookScenario(
            ScenarioName,
            changingCells,
            ScenarioComment,
            Hidden: false,
            Locked: true));
    }
}
