using Free.Shared.Drawing;

namespace FreeP.App.Compositor.Tests;

public sealed class TableCellBorderRenderSequenceTests
{
    [Fact]
    public void Dispatch_PreservesSideOrderAndDiagonalOrientation()
    {
        ResolvedOutline[] outlines =
        [
            Outline(1),
            Outline(2),
            Outline(3),
            Outline(4),
            Outline(5),
            Outline(6),
        ];
        var cell = new TableCellOp
        {
            BoundsDip = new LayoutRect(10, 20, 30, 40),
            BorderTop = outlines[0],
            BorderBottom = outlines[1],
            BorderLeft = outlines[2],
            BorderRight = outlines[3],
            BorderDiagonalDown = outlines[4],
            BorderDiagonalUp = outlines[5],
        };
        var entries = new List<Entry>();
        var sink = new RecordingSink(entries);

        TableCellBorderRenderSequence.Dispatch(cell, ref sink);

        entries.Select(entry => entry.Outline).Should().Equal(outlines);
        entries.Select(entry => (entry.Start, entry.End)).Should().Equal(
            (new LayoutPoint(10, 20), new LayoutPoint(40, 20)),
            (new LayoutPoint(10, 60), new LayoutPoint(40, 60)),
            (new LayoutPoint(10, 20), new LayoutPoint(10, 60)),
            (new LayoutPoint(40, 20), new LayoutPoint(40, 60)),
            (new LayoutPoint(10, 20), new LayoutPoint(40, 60)),
            (new LayoutPoint(10, 60), new LayoutPoint(40, 20)));
    }

    [Fact]
    public void Dispatch_RejectsNullCell()
    {
        var sink = new RecordingSink([]);

        var act = () => TableCellBorderRenderSequence.Dispatch(null!, ref sink);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ResolvedOutline Outline(byte red) =>
        new ResolvedOutline.Visible(new SrgbColor(red, 0, 0), 1, OutlineDash.Solid);

    private readonly record struct Entry(
        ResolvedOutline Outline,
        LayoutPoint Start,
        LayoutPoint End);

    private readonly struct RecordingSink(List<Entry> entries) : ITableCellBorderRenderSink
    {
        public void Render(ResolvedOutline outline, LayoutPoint start, LayoutPoint end) =>
            entries.Add(new Entry(outline, start, end));
    }
}
