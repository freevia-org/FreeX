using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-calc-selection-semantics-5-3: Go To Special's Precedents/Dependents must offer a
/// Direct-only (default) vs. All-levels choice, matching real Excel's Go To Special dialog.
/// Exercised through the real <see cref="GoToSpecialService.Find"/> entry point (the service
/// the Go To Special dialog and command layer actually call), not a hand-built model.
/// </summary>
public sealed class R91_GoToSpecialPrecedentsDependentsAllLevelsTests
{
    private static (Workbook workbook, Sheet sheet, CellAddress a1, CellAddress b1, CellAddress c1) BuildChain()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1+1"));

        return (workbook, sheet, a1, b1, c1);
    }

    [Fact]
    public void FindPrecedents_AllLevels_IncludesPrecedentOfPrecedent()
    {
        var (workbook, sheet, a1, b1, c1) = BuildChain();
        var range = new GridRange(c1, c1);

        var result = GoToSpecialService.Find(
            workbook,
            sheet,
            range,
            GoToSpecialKind.Precedents,
            activeCell: c1,
            options: new GoToSpecialOptions(AllLevels: true));

        result.Should().Contain(b1).And.Contain(a1);
    }

    [Fact]
    public void FindPrecedents_DirectOnly_ExcludesPrecedentOfPrecedent()
    {
        // No-regression sibling: the default (direct-only) behavior must be unchanged --
        // selecting C1's precedents only reaches B1, never A1.
        var (workbook, sheet, a1, b1, c1) = BuildChain();
        var range = new GridRange(c1, c1);

        var result = GoToSpecialService.Find(
            workbook,
            sheet,
            range,
            GoToSpecialKind.Precedents,
            activeCell: c1);

        result.Should().Equal(b1);
        result.Should().NotContain(a1);
    }

    [Fact]
    public void FindDependents_AllLevels_IncludesDependentOfDependent()
    {
        var (workbook, sheet, a1, b1, c1) = BuildChain();
        var range = new GridRange(a1, a1);

        var result = GoToSpecialService.Find(
            workbook,
            sheet,
            range,
            GoToSpecialKind.Dependents,
            activeCell: a1,
            options: new GoToSpecialOptions(AllLevels: true));

        result.Should().Contain(b1).And.Contain(c1);
    }

    [Fact]
    public void FindDependents_DirectOnly_ExcludesDependentOfDependent()
    {
        var (workbook, sheet, a1, b1, c1) = BuildChain();
        var range = new GridRange(a1, a1);

        var result = GoToSpecialService.Find(
            workbook,
            sheet,
            range,
            GoToSpecialKind.Dependents,
            activeCell: a1);

        result.Should().Equal(b1);
        result.Should().NotContain(c1);
    }
}
