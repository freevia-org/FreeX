using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r213: the tier-1 group r212 ranked -- print areas, cell comment, and scenario. Each is a direct
/// compare, and each carries one wrinkle the test pins.
/// </summary>
public sealed class R213_EqualValueSetterNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingTheSheetsOwnPrintAreas_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var areas = new[]
        {
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            new GridRange(new CellAddress(sheet.Id, 9, 1), new CellAddress(sheet.Id, 12, 3)),
        };
        sheet.SetPrintAreas(areas);

        new SetPrintAreasCommand(sheet.Id, areas).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReorderingThePrintAreas_DoesNotReportNoOp()
    {
        // Order is part of the value: the comparison is sequence-equal, not set-equal.
        var (_, sheet, ctx) = Fixture();
        var first = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var second = new GridRange(new CellAddress(sheet.Id, 9, 1), new CellAddress(sheet.Id, 12, 3));
        sheet.SetPrintAreas([first, second]);

        new SetPrintAreasCommand(sheet.Id, [second, first]).Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ReApplyingACellsOwnCommentText_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[address] = "Check this figure";
        sheet.CommentAuthors[address] = "Ada";

        new SetCommentCommand(sheet.Id, address, "Check this figure", "Someone Else").Apply(ctx)
            .IsNoOp.Should().BeTrue("editing an existing note must not touch its recorded author");
        sheet.CommentAuthors[address].Should().Be("Ada");
    }

    [Fact]
    public void AddingANewCommentWithTheSameTextAsAnother_DoesNotReportNoOp()
    {
        // A brand-new note also writes the author, so it always changes something even if some other
        // cell happens to carry identical text.
        var (_, sheet, ctx) = Fixture();
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "Same words";

        var outcome = new SetCommentCommand(
                sheet.Id, new CellAddress(sheet.Id, 2, 1), "Same words", "Ada")
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.CommentAuthors[new CellAddress(sheet.Id, 2, 1)].Should().Be("Ada");
    }

    [Fact]
    public void EditingACommentsText_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[address] = "Old";

        new SetCommentCommand(sheet.Id, address, "New", "Ada").Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ShowingTheScenarioThatIsAlreadyApplied_ReportsNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(42));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best case",
            [new ScenarioCellValue(address, new NumberValue(42))]));

        new ApplyScenarioCommand("Best case").Apply(ctx)
            .IsNoOp.Should().BeTrue("the scenario list highlights the active scenario, so re-showing it is ordinary");
    }

    [Fact]
    public void ShowingAScenarioThatChangesOneCell_DoesNotReportNoOp()
    {
        // The probe pass must not write anything before the answer is known, and one differing cell
        // is enough to make the whole scenario a real change.
        var (workbook, sheet, ctx) = Fixture();
        var matching = new CellAddress(sheet.Id, 1, 1);
        var differing = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(matching, new NumberValue(42));
        sheet.SetCell(differing, new NumberValue(1));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best case",
            [
                new ScenarioCellValue(matching, new NumberValue(42)),
                new ScenarioCellValue(differing, new NumberValue(99)),
            ]));

        var outcome = new ApplyScenarioCommand("Best case").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetCell(differing)!.Value.Should().BeEquivalentTo(new NumberValue(99));
    }
}
