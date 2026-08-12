using FreeX.Core.Model;

namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// Builds the deterministic workbook state used by the sheet-tab overflow parity surface.
/// Evidence preparation mutates the fixture directly so it cannot enter the user command pipeline.
/// </summary>
public static class SheetTabsOverflowParityFixture
{
    public const int TargetSheetCount = 20;

    public static SheetId Prepare(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Sheets.Count == 0)
            throw new ArgumentException("The sheet-tab overflow fixture requires a worksheet.", nameof(workbook));
        if (workbook.Sheets.Any(sheet => sheet.Id.Value == Guid.Empty))
            throw new ArgumentException("The sheet-tab overflow fixture requires non-empty sheet identities.", nameof(workbook));

        while (workbook.Sheets.Count < TargetSheetCount)
            workbook.AddSheet(SheetTabListPlanner.GenerateUniqueSheetName(workbook));

        var activeSheetIndex = workbook.Sheets.Count - 1;
        var activeSheet = workbook.Sheets[activeSheetIndex];
        workbook.ActiveSheetIndex = activeSheetIndex;
        activeSheet.ResetViewStateToA1();
        return activeSheet.Id;
    }
}
