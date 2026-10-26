using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r258: the two "save this again" commands r231 named and declined to guard, because the obvious
/// <c>newValue == previous</c> could not fire -- both records carry a list member, which record
/// equality compares by reference, so against a freshly built instance it is always false.
///
/// <para>The no-op direction is what r231 predicted and could not safely assert. The changed
/// direction is the one that proves the comparison is a comparison and not just a "true": a guard
/// that always fired would take both commands off the debt list and silently swallow real saves.</para>
/// </summary>
public sealed class R258_SaveAgainNoOpTests
{
    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx) SetUp()
    {
        var wb = new Workbook("SaveAgainTest");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static SaveScenarioCommand Save(Sheet sheet, double value, string? comment = null) =>
        new("Base", [new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(value))], comment);

    [Fact]
    public void SaveScenarioCommand_SavingTheSameScenarioAgainIsANoOp()
    {
        var (wb, sheet, ctx) = SetUp();

        Save(sheet, 1).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first save adds a scenario that was not there");
        wb.Scenarios.Should().ContainSingle();

        // A freshly built scenario with identical content -- the case r231 named, where the list
        // member makes record equality answer "different" forever.
        Save(sheet, 1).Apply(ctx)
            .IsNoOp.Should().BeTrue("re-saving with nothing changed writes back an equal scenario");
        wb.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void SaveScenarioCommand_SavingAChangedValueIsNotANoOp()
    {
        var (_, sheet, ctx) = SetUp();

        Save(sheet, 1).Apply(ctx);

        Save(sheet, 2).Apply(ctx)
            .IsNoOp.Should().BeFalse("the changing cell's value differs");
    }

    [Fact]
    public void SaveScenarioCommand_SavingAChangedCommentIsNotANoOp()
    {
        var (_, sheet, ctx) = SetUp();

        Save(sheet, 1).Apply(ctx);

        Save(sheet, 1, comment: "Q3 assumptions").Apply(ctx)
            .IsNoOp.Should().BeFalse(
                "Comment is a scalar the stripped record-equality half must still catch");
    }

    [Fact]
    public void SaveCustomViewCommand_SavingTheSameViewAgainIsANoOp()
    {
        var (wb, _, ctx) = SetUp();

        new SaveCustomViewCommand("Draft").Apply(ctx)
            .IsNoOp.Should().BeFalse("the first save adds a view that was not there");
        wb.CustomViews.Should().ContainSingle();

        new SaveCustomViewCommand("Draft").Apply(ctx)
            .IsNoOp.Should().BeTrue(
                "nothing about the workbook changed, so the captured state is equal -- and every "
                + "capture builds a fresh Sheets list, which is why == could never say so");
        wb.CustomViews.Should().ContainSingle();
    }

    [Fact]
    public void SaveCustomViewCommand_SavingAfterTheViewStateChangesIsNotANoOp()
    {
        var (_, sheet, ctx) = SetUp();

        new SaveCustomViewCommand("Draft").Apply(ctx);

        sheet.ShowGridlines = !sheet.ShowGridlines;

        new SaveCustomViewCommand("Draft").Apply(ctx)
            .IsNoOp.Should().BeFalse("the captured per-sheet view state differs");
    }

    [Fact]
    public void SaveCustomViewCommand_SavingWithDifferentIncludeFlagsIsNotANoOp()
    {
        var (_, _, ctx) = SetUp();

        new SaveCustomViewCommand("Draft").Apply(ctx);

        new SaveCustomViewCommand("Draft", includePrintSettings: false).Apply(ctx)
            .IsNoOp.Should().BeFalse(
                "IncludePrintSettings is a scalar the stripped record-equality half must still catch");
    }
}
