using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// r253: Above/Below-Average filtering is re-applicable from the Filter menu, so re-running it on a
/// column it is already applied to writes exactly what is already there. Reporting that as an edit
/// pushes an undo entry, and UndoRedoStack.Push clears the redo stack -- so a phantom entry here
/// destroys a real edit the user could have redone.
///
/// <para>Both directions are pinned: a filter that changes something must NOT report a no-op, or the
/// guard would silently swallow real edits, which is the worse of the two failures.</para>
/// </summary>
public partial class SortFilterTests
{
    private static (ICommandContext ctx, Sheet sheet, GridRange range, SheetId sid) MakeAverageFilterSheet(bool withAutoFilterModel)
    {
        var (_, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        // With the worksheet AutoFilter model present, the command also writes a filterColumn entry,
        // so the no-op decision has to compare a FRESHLY BUILT column model against the stored one --
        // the case record equality gets wrong, because their collections share no reference.
        if (withAutoFilterModel)
            sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        return (ctx, sheet, range, sid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AverageFilterCommand_FirstApplicationIsNotReportedAsANoOp(bool withAutoFilterModel)
    {
        var (ctx, sheet, range, sid) = MakeAverageFilterSheet(withAutoFilterModel);

        var outcome = new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse("the filter hid rows the sheet was showing");
        sheet.FilterHiddenRows.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AverageFilterCommand_ReapplyingTheSameFilterReportsANoOp(bool withAutoFilterModel)
    {
        var (ctx, sheet, range, sid) = MakeAverageFilterSheet(withAutoFilterModel);

        new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx)
            .IsNoOp.Should().BeFalse();
        var hiddenAfterFirst = new HashSet<uint>(sheet.FilterHiddenRows);

        var outcome = new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue(
            "re-running Above Average on a column already filtered that way writes what is already "
            + "there; pushing an undo entry for it clears the redo stack");
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hiddenAfterFirst);
    }

    [Fact]
    public void AverageFilterCommand_SwitchingBelowAverageAfterAboveIsNotANoOp()
    {
        var (ctx, sheet, range, sid) = MakeAverageFilterSheet(withAutoFilterModel: true);

        new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx);

        var outcome = new AverageFilterCommand(sid, range, filterColOffset: 0, above: false).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse(
            "Below Average hides the opposite rows and stores a different dynamicFilter type");
    }

    /// <summary>
    /// The AutoFilter-model half on its own: a column whose rows already satisfy the criterion moves
    /// no row, so only the stored filterColumn entry can differ. Without content comparison of the
    /// freshly built column model this case reports a change forever.
    /// </summary>
    [Fact]
    public void AverageFilterCommand_ReapplyingWhenNoRowMovesStillReportsANoOp()
    {
        var (ctx, sheet, range, sid) = MakeAverageFilterSheet(withAutoFilterModel: true);

        new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx);
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle(
            "the applied criterion must be stored, or this test would prove nothing about it");

        var outcome = new AverageFilterCommand(sid, range, filterColOffset: 0, above: true).Apply(ctx);

        outcome.IsNoOp.Should().BeTrue();
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
    }
}
