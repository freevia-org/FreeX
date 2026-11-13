using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed partial class WorkbookSession
{
    /// <summary>
    /// Excel's "Convert to Number" (the error smart tag's first command for a number stored as
    /// text): every selected cell flagged with that error becomes a real number, keeping its
    /// formatting, as one undo step.
    /// </summary>
    public WorkbookCellEditResult ConvertSelectionTextToNumbers()
    {
        var sheet = ActiveSheet;
        var indicators = FormulaAuditingService.FindBackgroundErrorIndicators(Workbook, sheet.Id);
        var ranges = SelectedRanges.Count > 0 ? SelectedRanges : [SelectedRange];
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        foreach (var (address, code) in indicators)
        {
            if (!string.Equals(code, FormulaAuditingService.NumberStoredAsTextErrorCode, StringComparison.Ordinal) ||
                !ranges.Any(range => range.Contains(address)) ||
                sheet.GetCell(address)?.Value is not TextValue text ||
                CellEntryParser.ParseScalarValue(text.Value.Trim()) is not NumberValue number)
            {
                continue;
            }

            edits.Add((address, Cell.FromValue(number)));
        }

        if (edits.Count == 0)
            return SuccessfulNoOpEditResult();

        edits.Sort((left, right) => CompareAddresses(left.Address, right.Address));
        return ExecuteCommandPreservingSelection(new EditCellsCommand(sheet.Id, edits));
    }

    /// <summary>
    /// Excel's "Ignore Error" from the error smart tag: every selected cell showing an error
    /// indicator stops showing it, as one undo step.
    /// </summary>
    public WorkbookCellEditResult IgnoreSelectionErrors()
    {
        var sheet = ActiveSheet;
        var indicators = FormulaAuditingService.FindBackgroundErrorIndicators(Workbook, sheet.Id);
        var ranges = SelectedRanges.Count > 0 ? SelectedRanges : [SelectedRange];
        var addresses = indicators.Keys
            .Where(address => ranges.Any(range => range.Contains(address)))
            .ToList();
        if (addresses.Count == 0)
            return SuccessfulNoOpEditResult();

        addresses.Sort(CompareAddresses);
        IWorkbookCommand command = addresses.Count == 1
            ? new SetFormulaErrorIgnoredCommand(sheet.Id, addresses[0], ignored: true)
            : new CompositeWorkbookCommand(
                "Ignore Error",
                addresses.Select(address => (IWorkbookCommand)new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true)).ToList());
        return ExecuteCommandPreservingSelection(command);
    }

    private static int CompareAddresses(CellAddress left, CellAddress right) =>
        left.Row != right.Row ? left.Row.CompareTo(right.Row) : left.Col.CompareTo(right.Col);
}
