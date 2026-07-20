using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for round-55 findings:
///  - R55-commands-fill-series-5-2: AutofillCommand's built-in weekday/month list series always
///    emitted the canonical Title-Case list entry, losing the seed's ALL-CAPS/all-lowercase case
///    style.
///  - R55-commands-fill-series-5-3: FillCellsCommand (Ctrl+D/Ctrl+R) unconditionally refused any
///    selection overlapping a merged region, even the uniform-merge-tile shape its sibling
///    AutofillCommand explicitly supports.
/// </summary>
public class R55_FillSeriesCommandsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // --- R55-commands-fill-series-5-2 -------------------------------------------------------

    [Fact]
    public void Autofill_BuiltInWeekdayList_AllCapsSeed_ContinuesInAllCaps()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("MONDAY"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("TUESDAY"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Excel reproduces the seed's own ALL-CAPS style, not the list's canonical "Wednesday".
        sheet.GetValue(3, 1).Should().Be(new TextValue("WEDNESDAY"));
    }

    [Fact]
    public void Autofill_BuiltInWeekdayList_AllLowerSeed_ContinuesInAllLower()
    {
        // Sibling case: an all-lowercase seed should continue in all-lowercase, not Title Case.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("monday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("tuesday"));
    }

    [Fact]
    public void Autofill_BuiltInWeekdayList_TitleCaseSeed_StaysTitleCase()
    {
        // No-regression sibling: the original Title-Case behavior must be unchanged.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Friday"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Saturday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Sunday"));
    }

    [Fact]
    public void Autofill_BuiltInWeekdayList_MixedCaseSeeds_FallsBackToCanonicalCase()
    {
        // Sibling: seeds that disagree on case style have no single style to reproduce, so
        // FreeX falls back to the list's canonical Title-Case entry.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("MONDAY"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Tuesday"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Wednesday"));
    }

    // --- R55-commands-fill-series-5-3 -------------------------------------------------------

    [Fact]
    public void FillDown_StackedUniformMerges_FillsTheMergeTile()
    {
        var (_, sheet, ctx) = Setup();

        // A1:B1 merged, holding "Q1"; A2:B2 separately merged, currently blank -- two
        // identically-sized, pre-existing stacked merges.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Q1")));
        sheet.AddMergedRegion(new GridRange(a1, b1));
        sheet.AddMergedRegion(new GridRange(a2, b2));

        var range = new GridRange(a1, b2); // A1:B2
        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Q1"));
        // The merge's non-anchor cell must remain independent-content-free.
        sheet.GetCell(b2).Should().BeNull();
        sheet.MergedRegions.Should().Contain(r => r == new GridRange(a1, b1));
        sheet.MergedRegions.Should().Contain(r => r == new GridRange(a2, b2));
    }

    [Fact]
    public void FillDown_PartialMergeOverlap_StillRejected()
    {
        // No-regression sibling (matches the existing R27 guard test): a merge only partially
        // covered by the fill range must still be refused outright.
        var (_, sheet, ctx) = Setup();

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b1, Cell.FromValue(new TextValue("X")));
        sheet.AddMergedRegion(new GridRange(b1, b2));

        var range = new GridRange(b1, new CellAddress(sheet.Id, 4, 2)); // B1:B4, B3:B4 unmerged
        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetCell(b2).Should().BeNull();
        sheet.MergedRegions.Should().ContainSingle(r => r == new GridRange(b1, b2));
    }
}
