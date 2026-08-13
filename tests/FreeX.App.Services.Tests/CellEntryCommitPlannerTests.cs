using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellEntryCommitPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void BuildSingle_ReturnsPreparedCellWithoutMutatingWorkbook()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 0, 0);

        var plan = CellEntryCommitPlanner.BuildSingle("42", address, false, workbook);

        plan.Success.Should().BeTrue();
        plan.Edits.Should().ContainSingle();
        plan.Edits[0].Address.Should().Be(address);
        plan.Edits[0].NewCell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(42);
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void BuildSelection_AnchorsR1C1FormulaForEachTarget()
    {
        var addresses = new[]
        {
            new CellAddress(SheetId, 2, 1),
            new CellAddress(SheetId, 2, 2),
        };

        var plan = CellEntryCommitPlanner.BuildSelection("=R[-1]C", addresses, true);

        plan.Success.Should().BeTrue();
        plan.Edits.Select(edit => edit.NewCell.FormulaText).Should().Equal("A1", "B1");
    }

    [Fact]
    public void BuildSelection_ReturnsOneFailureAndNoPartialEditsForMalformedFormula()
    {
        var plan = CellEntryCommitPlanner.BuildSelection(
            "=SUM(A1",
            [new CellAddress(SheetId, 0, 0), new CellAddress(SheetId, 0, 1)],
            false);

        plan.Success.Should().BeFalse();
        plan.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        plan.Edits.Should().BeEmpty();
    }
}
