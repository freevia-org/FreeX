using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-40 finding R40-commands-autofill-flashfill-3-2: dragging the
/// fill handle over 2+ IDENTICAL built-in-list (or custom-list) values used to always advance the
/// list instead of copying. Excel's fill-handle rule treats a flat (zero-step) series -- whether
/// numeric, date, or list-based -- as "no trend detected", so it copies the value instead of
/// advancing.
/// </summary>
public sealed class R40_AutofillIdenticalListSeriesCopyTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void FillBuiltInWeekdayList_Down_TwoIdenticalSamples_CopiesInsteadOfAdvancing()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Monday"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Monday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Monday"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Monday"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Monday"));
    }

    [Fact]
    public void FillCustomList_Down_TwoIdenticalSamples_CopiesInsteadOfAdvancing()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(
            sheet.Id,
            sourceRange,
            fillRange,
            customLists: [["North", "South", "East", "West"]]).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("North"));
    }

    // ── No-regression sibling: a genuine (non-identical) list trend still advances. ────────

    [Fact]
    public void FillBuiltInWeekdayList_Down_DistinctSamples_StillAdvancesAndWraps()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Friday"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Saturday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Sunday"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Monday"));
    }
}
