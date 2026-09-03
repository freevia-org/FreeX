using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r267: MoveRange, the last command on the no-op debt. r225 recorded it as "reachable by dragging
/// something and dropping it where it started" and declined to fix it, because a decision over its
/// twenty-six snapshots was a round of its own.
///
/// <para>The changed direction carries most of the weight here: this command writes cells, formulas
/// elsewhere in the workbook, comments, hyperlinks, merged regions, tables, named ranges, charts and
/// sparklines. A decision that wrongly reported a no-op would drop all of that from the undo stack at
/// once, which is the worst failure available in this whole program.</para>
/// </summary>
public sealed class R267_MoveRangeNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Sheet Sheet, TestCommandContext Ctx) SetUp()
    {
        var wb = new Workbook("R267");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("one"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("three"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(4));
        return (sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void DroppingTheRangeWhereItStartedIsANoOp()
    {
        var (sheet, ctx) = SetUp();

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "B2"), Addr(sheet, "A1")).Apply(ctx)
            .IsNoOp.Should().BeTrue("the destination is the range's own top-left, so every cell is written back to itself");
        sheet.GetValue(1, 1).Should().Be(new TextValue("one"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void MovingTheRangeElsewhereIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "B2"), Addr(sheet, "D1")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the cells land somewhere else");
        sheet.GetValue(1, 4).Should().Be(new TextValue("one"));
        sheet.GetValue(1, 1).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void MovingByOneColumnIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "A2"), Addr(sheet, "B1")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the source column is vacated and the destination overwritten");
    }

    /// <summary>
    /// The workbook-wide half: a formula OUTSIDE the moved range is rewritten to follow it. Nothing
    /// inside the range changes value, so only the formula snapshot can see this move.
    /// </summary>
    [Fact]
    public void AMoveThatRewritesAFormulaElsewhereIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.SetCell(Addr(sheet, "D5"), Cell.FromFormula("A1&\"x\""));

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "A1"), Addr(sheet, "C1")).Apply(ctx)
            .IsNoOp.Should().BeFalse("D5's reference follows the moved cell");
        sheet.GetCell(Addr(sheet, "D5"))!.FormulaText.Should().Contain("C1");
    }

    /// <summary>
    /// A same-destination move on a sheet that also carries a formula referring INTO the range: the
    /// formula must not be rewritten either, so the decision stays a no-op. This is the case that
    /// would break if the formula clause compared counts rather than values.
    /// </summary>
    [Fact]
    public void DroppingWhereItStartedWithFormulasPointingIntoTheRangeIsStillANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.SetCell(Addr(sheet, "D5"), Cell.FromFormula("A1&\"x\""));
        var formulaBefore = sheet.GetCell(Addr(sheet, "D5"))!.FormulaText;

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "B2"), Addr(sheet, "A1")).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.GetCell(Addr(sheet, "D5"))!.FormulaText.Should().Be(formulaBefore);
    }

    /// <summary>
    /// The companion half: the cells keep their values on a same-destination move, and so must their
    /// comments and hyperlinks -- so this stays a no-op with them present, which proves the companion
    /// comparisons do not report a spurious change for state that merely round-tripped.
    /// </summary>
    [Fact]
    public void DroppingWhereItStartedWithCommentsAndHyperlinksIsStillANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.Comments[Addr(sheet, "A1")] = "note";
        sheet.Hyperlinks[Addr(sheet, "B2")] = "https://example.com";

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "B2"), Addr(sheet, "A1")).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.Comments[Addr(sheet, "A1")].Should().Be("note");
        sheet.Hyperlinks[Addr(sheet, "B2")].Should().Be("https://example.com");
    }

    [Fact]
    public void AMoveThatCarriesACommentToANewCellIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.Comments[Addr(sheet, "A1")] = "note";

        new MoveRangeCommand(sheet.Id, Range(sheet, "A1", "A1"), Addr(sheet, "C1")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the note moves with the cell");
        sheet.Comments.ContainsKey(Addr(sheet, "C1")).Should().BeTrue();
    }

    /// <summary>
    /// The case the WIDE decision exists for, as opposed to the same-destination early return:
    /// moving an EMPTY range onto blank cells takes the full apply path -- snapshots, formula
    /// rewrites, the lot -- and still writes nothing anyone can observe. Only the comparison over
    /// all twenty-six snapshots can report that, because the early return never sees it.
    /// </summary>
    [Fact]
    public void MovingAnEmptyRangeOntoBlankCellsIsANoOp()
    {
        var (sheet, ctx) = SetUp();

        new MoveRangeCommand(sheet.Id, Range(sheet, "F1", "G2"), Addr(sheet, "H5")).Apply(ctx)
            .IsNoOp.Should().BeTrue("blank cells moved onto blank cells leave the sheet exactly as it was");
    }
}
