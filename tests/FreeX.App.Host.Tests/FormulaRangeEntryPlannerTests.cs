using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class FormulaRangeEntryPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly CellAddress FormulaCell = new(SheetId, 10, 5);

    private static GridRange Range(string start, string end) =>
        new(CellAddress.Parse(start, SheetId), CellAddress.Parse(end, SheetId));
}
