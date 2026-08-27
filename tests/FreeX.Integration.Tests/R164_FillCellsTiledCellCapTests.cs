using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// r164 remediation, destination-sized tiling with no ceiling. Home &gt; Fill &gt; Down/Right/Up/Left
/// (Ctrl+D/Ctrl+R) is a distinct path from the fill handle (<see cref="AutofillCommand"/>, capped in
/// r163) and from Fill &gt; Series (capped earlier this round): <see cref="FillCellsCommand"/>
/// materialises one <c>CellAddress</c> per destination cell plus five per-cell undo snapshot lists,
/// all sized from the user's selection with no source range to bound them. Selecting a whole column
/// and pressing Ctrl+D therefore asked for ~1.05 billion entries on the synchronous UI thread.
///
/// Reachability: MainWindow.HomeEditing.cs's ExecuteFillCells is gated only by
/// <c>WorkbookSession.CanFillSelectedRange</c>, which merely requires two cells in the fill
/// direction -- no size cap -- so a full-column selection reaches this command directly.
/// </summary>
public class R164_FillCellsTiledCellCapTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("R164FillCellsCap");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Theory]
    [InlineData(FillCellsDirection.Down)]
    [InlineData(FillCellsDirection.Right)]
    [InlineData(FillCellsDirection.Up)]
    [InlineData(FillCellsDirection.Left)]
    public void FillCells_JustOverCapRange_IsRejectedInsteadOfAllocatingMillionsOfSnapshots(FillCellsDirection direction)
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        // 2,001 x 2,001 = 4,004,001 cells, one over the 4,000,000 limit -- large enough to exercise
        // the pre-fix allocation path, small enough not to risk the OOM the real gesture causes.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2001, 2001));

        var outcome = new FillCellsCommand(sheet.Id, range, direction).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void FillCells_AnOrdinaryFillDownStillFills()
    {
        // Sibling/no-regression: the cap must not disturb the gesture users actually make.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(7));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.Value.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void FillCells_AnOrdinaryFillRightStillFills()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(9));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));

        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Right).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 1, 3))!.Value.Should().Be(new NumberValue(9));
    }

    [Fact]
    public void FillCells_AnOrdinaryFillUpStillFills()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Up).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.Value.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void FillCells_AnOrdinaryFillLeftStillFills()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(5));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));

        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Left).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.Value.Should().Be(new NumberValue(5));
    }
}
