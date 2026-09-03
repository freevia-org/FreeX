using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r235: closing the limit r221 wrote down. Its Paste guards caught "there was nothing to paste"
/// and said, in the source and the notes, that they did NOT catch "the pasted values equalled what
/// was already there". r234 built the missing half, so these two now report a paste over an
/// identical range for what it is.
/// <para>
/// Copying a block and pasting it back over itself is the gesture -- and it is one people perform by
/// accident constantly, by pasting twice.
/// </para>
/// </summary>
public sealed class R235_PasteOverIdenticalNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"r{row}"));
        return (sheet, new TestCommandContext(workbook));
    }

    private static List<(CellAddress Address, Cell Cell)> CellsOf(Sheet sheet) =>
    [
        (new CellAddress(sheet.Id, 1, 1), sheet.GetCell(1, 1)!.Clone()),
        (new CellAddress(sheet.Id, 2, 1), sheet.GetCell(2, 1)!.Clone()),
        (new CellAddress(sheet.Id, 3, 1), sheet.GetCell(3, 1)!.Clone()),
    ];

    private static List<(CellAddress Address, Cell Cell)> MovedCellsOf(Sheet sheet) =>
    [
        (new CellAddress(sheet.Id, 10, 1), sheet.GetCell(1, 1)!.Clone()),
        (new CellAddress(sheet.Id, 11, 1), sheet.GetCell(2, 1)!.Clone()),
        (new CellAddress(sheet.Id, 12, 1), sheet.GetCell(3, 1)!.Clone()),
    ];

    [Fact]
    public void PastingARangeBackOverItself_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new PasteCellsCommand(sheet.Id, CellsOf(sheet))
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingARangeSomewhereElse_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        var outcome = new PasteCellsCommand(sheet.Id, MovedCellsOf(sheet))
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(10, 1).Should().Be(new TextValue("r1"));
    }

    [Fact]
    public void PastingOverARangeThatDiffersInOneCell_IsARealEdit()
    {
        // The clause that keeps the batch honest, same as r234's: one differing cell in the block
        // makes the whole paste a real edit, even though the other two match.
        var (sheet, ctx) = Fixture();
        var cells = CellsOf(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("changed"));

        var outcome = new PasteCellsCommand(sheet.Id, cells)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(2, 1).Should().Be(new TextValue("r2"));
    }
}
