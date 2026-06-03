using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// The canonical first test from §9 of the build plan, plus comprehensive formula coverage.
/// </summary>
public partial class FormulaEvaluatorTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Sheet sheet, CellAddress a1, CellAddress a2, CellAddress a3) SetupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        return (sheet, a1, a2, a3);
    }
}
