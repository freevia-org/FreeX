using System;
using System.Threading.Tasks;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R47-io-drawing-anchor-3-3: a twoCellAnchor whose 'to' marker precedes 'from' on only ONE axis is
/// accepted by the reader's validity check (which only rejects an anchor invalid on BOTH axes at once --
/// producible by non-Excel authoring tools or a hand-edited/corrupted file). XlsxDrawingAnchorApplier's
/// GetAnchorSize (shared by Picture/Shape/TextBox anchoring) and the duplicate inline computation in
/// ApplyToChart both computed the column/row span as a plain `to - from` subtraction on `uint` values with
/// no check that `to &gt;= from`; when inverted, that underflows to a value near <see cref="uint.MaxValue"/>
/// (~4.29 billion), which is then used as the iteration count for SumColumnPixels/SumRowPixels -- hanging
/// the app the first time that object's size is computed, i.e. immediately on load. The fix clamps the
/// span to zero whenever 'to' does not come strictly after 'from' on that axis.
/// </summary>
public sealed class R47_DrawingAnchorInvertedSpanTests
{
    private static Sheet BuildSheet()
    {
        var workbook = new Workbook("DrawingAnchorInvertedSpan");
        return workbook.AddSheet("Sheet1");
    }

    private static XlsxDrawingAnchor BuildAnchor(uint fromCol, uint? toCol, uint fromRow, uint? toRow) =>
        new(
            Kind: ChartDrawingAnchorKind.TwoCell,
            FromRowZeroBased: fromRow,
            FromColumnZeroBased: fromCol,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: toRow,
            ToColumnZeroBased: toCol,
            ToRowOffset: 0,
            ToColumnOffset: 0,
            Width: null,
            Height: null);

    [Fact]
    public async Task GetAnchorSize_ToColumnPrecedesFromColumn_ClampsSpanToZero_InsteadOfHanging()
    {
        var sheet = BuildSheet();

        // Inverted on the COLUMN axis only (toCol=2 < fromCol=5); valid on the row axis (toRow=3 >
        // fromRow=0) so the reader's `colInvalid && rowInvalid` check would have accepted this shape.
        var anchor = BuildAnchor(fromCol: 5, toCol: 2, fromRow: 0, toRow: 3);

        // Pre-fix, computing the size underflows `toCol - fromCol` (both uint) to ~4.29 billion and then
        // loops that many times inside SumColumnPixels -- effectively hanging. Bound the call with a
        // generous timeout so this test fails fast (rather than hanging the whole test run) when that
        // regression is present.
        var work = Task.Run(() => XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet));
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10))) == work;

        completed.Should().BeTrue(
            "an anchor inverted on only one axis must not underflow the unsigned column-span subtraction into a multi-billion-iteration loop that hangs file load");

        var (width, _) = await work;
        width.Should().Be(0,
            "the column span must clamp to zero when 'to' precedes 'from' on that axis, not compute a huge (underflowed) width");
    }

    [Fact]
    public async Task GetAnchorSize_ToRowPrecedesFromRow_ClampsSpanToZero_InsteadOfHanging()
    {
        var sheet = BuildSheet();

        // The mirror case: inverted on the ROW axis only, valid on the column axis.
        var anchor = BuildAnchor(fromCol: 0, toCol: 3, fromRow: 5, toRow: 2);

        var work = Task.Run(() => XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet));
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10))) == work;

        completed.Should().BeTrue(
            "an anchor inverted on only the row axis must not underflow the unsigned row-span subtraction into a multi-billion-iteration loop that hangs file load");

        var (_, height) = await work;
        height.Should().Be(0,
            "the row span must clamp to zero when 'to' precedes 'from' on that axis, not compute a huge (underflowed) height");
    }

    [Fact]
    public void GetAnchorSize_OrdinaryValidTwoCellAnchor_StillComputesCorrectNonZeroSpan()
    {
        // Sibling no-regression case: an ordinary, correctly-ordered twoCellAnchor (to >= from on both
        // axes, the overwhelming common case) must keep computing its real pixel span.
        var sheet = BuildSheet();
        var anchor = BuildAnchor(fromCol: 1, toCol: 4, fromRow: 0, toRow: 2);

        var (width, height) = XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet);

        var expectedWidth = sheet.DefaultColumnWidth * 8 * 3; // columns 2,3,4 (zero-based 1,2,3) => 3 columns
        var expectedHeight = sheet.DefaultRowHeight * 2; // rows 1,2 (zero-based 0,1) => 2 rows

        width.Should().Be(expectedWidth);
        height.Should().Be(expectedHeight);
    }
}
