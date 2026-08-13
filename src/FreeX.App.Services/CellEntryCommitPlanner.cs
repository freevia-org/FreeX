using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record CellEntryCommitPlan(
    IReadOnlyList<(CellAddress Address, Cell NewCell)> Edits,
    string? ErrorMessage)
{
    public bool Success => ErrorMessage is null;
}

public static class CellEntryCommitPlanner
{
    public static CellEntryCommitPlan BuildSingle(
        string text,
        CellAddress address,
        bool useR1C1ReferenceStyle,
        Workbook? workbook = null) =>
        BuildSelection(text, [address], useR1C1ReferenceStyle, workbook);

    public static CellEntryCommitPlan BuildSelection(
        string text,
        IEnumerable<CellAddress> addresses,
        bool useR1C1ReferenceStyle,
        Workbook? workbook = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(addresses);

        var edits = new List<(CellAddress Address, Cell NewCell)>();
        try
        {
            foreach (var address in addresses)
            {
                edits.Add((
                    address,
                    CellEntryParser.CreateCell(text, address, useR1C1ReferenceStyle, workbook)));
            }
        }
        catch (FormulaParseException ex)
        {
            return new CellEntryCommitPlan([], ex.Message);
        }

        return new CellEntryCommitPlan(edits, ErrorMessage: null);
    }
}
