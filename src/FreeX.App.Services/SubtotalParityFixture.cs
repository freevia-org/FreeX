using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// The deterministic state used by both hosts when capturing dialog.Subtotal. It deliberately
/// points at the real parity workbook range so the dialog's visible headers and selected options
/// cannot drift between capture routes.
/// </summary>
public sealed record SubtotalParityFixtureState(
    GridRange SelectedRange,
    IReadOnlyList<SubtotalDialogColumnChoice> Columns,
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    string FunctionText,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData)
{
    public SubtotalDialogPlanResult CreatePlan()
    {
        if (!SubtotalFunctionService.TryParse(FunctionText, out var functionNumber))
            throw new InvalidOperationException($"Subtotal parity fixture function '{FunctionText}' is not supported.");

        return new(
            GroupColumnOffset,
            SubtotalColumnOffsets,
            functionNumber,
            ReplaceCurrentSubtotals,
            PageBreakBetweenGroups,
            SummaryBelowData);
    }
}

public static class SubtotalParityFixture
{
    public const uint StartRow = 1;
    public const uint EndRow = 4;
    public const uint StartColumn = 1;
    public const uint EndColumn = 4;

    public static SubtotalParityFixtureState CreateState(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var range = new GridRange(
            new CellAddress(sheet.Id, StartRow, StartColumn),
            new CellAddress(sheet.Id, EndRow, EndColumn));
        var columns = SubtotalDialogPlanner.BuildColumnChoices(sheet, range)
            .Select(column => column with { IsSelected = column.Offset is 2 or 3 })
            .ToArray();

        return new SubtotalParityFixtureState(
            range,
            columns,
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [2, 3],
            FunctionText: SubtotalDialogPlanner.DefaultFunctionText,
            ReplaceCurrentSubtotals: true,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true);
    }

    public static void ApplySheetState(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        sheet.OutlineSummaryBelow = true;
    }
}
