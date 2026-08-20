using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// shared-large-paste-F1: pasting a single copied cell into a destination selection much larger
/// than the copied block (e.g. several dozen full columns, or a whole-sheet Ctrl+A selection)
/// used to route into CreateTiledInternalPasteCommand, which preallocated a
/// List&lt;(CellAddress, Cell)&gt; sized to the ENTIRE destination rectangle and then walked every
/// cell in it synchronously on the caller's thread -- no size cap, no progress, no cancellation.
/// At whole-sheet scale (~17.2 billion cells) the preallocation alone throws
/// OutOfMemoryException, which the WPF host's crash handler never marks Handled, so the whole
/// process terminates. Below that threshold the same code path is still a multi-second,
/// multi-gigabyte UI freeze that scales with destination size (measured against production DLLs:
/// 10.5M cells took ~3.1s / grew the process ~1.1GB; 104.8M cells took ~35.7s / ~10.7GB).
/// PasteCommandFactory now rejects a tiled paste whose destination rectangle exceeds a fixed
/// cell-count cap up front, before any allocation or per-cell iteration happens.
/// </summary>
public sealed class PasteCommandFactoryLargeTiledPasteCapTests
{
    [Fact]
    public void CreateInternalPasteCommand_RejectsTiledPasteWhoseDestinationExceedsTheSizeCap_InsteadOfAllocatingTheFullRectangle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("x")));

        // Mirrors the finding's "select several dozen full columns and paste" repro: a single
        // copied cell tiled across a destination that would previously have caused a multi-second,
        // multi-gigabyte freeze (1,048,576 rows x 4 cols = 4,194,304 cells -- just over the
        // production cap of 4,000,000).
        var destinationStart = new CellAddress(sheet.Id, 1, 2);
        var destinationEnd = new CellAddress(sheet.Id, CellAddress.MaxRow, 5);
        var sentinel = new CellAddress(sheet.Id, CellAddress.MaxRow, 5);
        sheet.SetCell(sentinel, Cell.FromValue(new TextValue("keep")));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large");
        outcome.ErrorMessage.Should().Contain("4,194,304");
        // A rejected command must not have mutated anything before returning failure.
        sheet.GetValue(sentinel).Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void CreateInternalPasteCommand_StillTilesAnOrdinaryLargerDestinationRange_WhenWellUnderTheSizeCap()
    {
        // Sibling / no-regression case: an ordinary tiled paste (single cell filled across a
        // modest destination block, nowhere near the new size cap) must still work exactly as it
        // did before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("x")));

        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var destinationEnd = new CellAddress(sheet.Id, 5, 5);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        for (var row = 3u; row <= 5u; row++)
        {
            for (var col = 3u; col <= 5u; col++)
            {
                sheet.GetValue(new CellAddress(sheet.Id, row, col)).Should().Be(new TextValue("x"));
            }
        }
    }
}
