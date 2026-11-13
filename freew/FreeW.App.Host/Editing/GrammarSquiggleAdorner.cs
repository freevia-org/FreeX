using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word's grammar indicator: a blue double underline. Pure chrome on the adorner layer, so it never
/// becomes part of the FlowDocument and can never round-trip into the saved file.
/// </summary>
internal sealed class GrammarSquiggleAdorner : Adorner
{
    private static readonly Pen UnderlinePen = CreatePen();

    private readonly RichTextBox _view;
    private readonly Func<IReadOnlyList<TextRange>> _ranges;

    public GrammarSquiggleAdorner(RichTextBox view, Func<IReadOnlyList<TextRange>> ranges) : base(view)
    {
        _view = view;
        _ranges = ranges;
        IsHitTestVisible = false;
        // Follow scrolling, zooming and reflow.
        _view.LayoutUpdated += (_, _) =>
        {
            if (_ranges().Count > 0)
                InvalidateVisual();
        };
    }

    private static Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xD9)), 1);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var ranges = _ranges();
        if (ranges.Count == 0)
            return;

        var bounds = new Rect(_view.RenderSize);
        drawingContext.PushClip(new RectangleGeometry(bounds));
        try
        {
            foreach (var range in ranges)
            {
                if (!range.Start.IsInSameDocument(_view.Document.ContentStart))
                    continue;

                foreach (var segment in LineSegments(range, bounds))
                {
                    // Two hairlines just under the text: Word's double underline.
                    var y = Math.Round(segment.Bottom) - 0.5;
                    drawingContext.DrawLine(UnderlinePen, new Point(segment.Left, y), new Point(segment.Right, y));
                    drawingContext.DrawLine(
                        UnderlinePen,
                        new Point(segment.Left, y + 2),
                        new Point(segment.Right, y + 2));
                }
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    // One rectangle per visual line the range covers (a flagged phrase can wrap).
    private static IEnumerable<Rect> LineSegments(TextRange range, Rect bounds)
    {
        Rect first;
        Rect last;
        try
        {
            first = range.Start.GetCharacterRect(LogicalDirection.Forward);
            last = range.End.GetCharacterRect(LogicalDirection.Backward);
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        if (first.IsEmpty || last.IsEmpty || last.Bottom < bounds.Top || first.Top > bounds.Bottom)
            yield break;

        if (Math.Abs(first.Top - last.Top) < 1)
        {
            if (last.Left > first.Left)
                yield return new Rect(first.Left, first.Top, last.Left - first.Left, first.Height);
            yield break;
        }

        // Wrapped: walk the characters and merge those that share a line.
        Rect? line = null;
        for (var position = range.Start;
             position is not null && position.CompareTo(range.End) < 0;
             position = position.GetNextInsertionPosition(LogicalDirection.Forward))
        {
            var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is null)
                break;

            var left = position.GetCharacterRect(LogicalDirection.Forward);
            var right = next.GetCharacterRect(LogicalDirection.Backward);
            if (left.IsEmpty || right.IsEmpty || Math.Abs(left.Top - right.Top) >= 1 || right.Left <= left.Left)
                continue;

            var glyph = new Rect(left.Left, left.Top, right.Left - left.Left, left.Height);
            if (line is { } current && Math.Abs(current.Top - glyph.Top) < 1)
            {
                line = Rect.Union(current, glyph);
                continue;
            }

            if (line is { } finished)
                yield return finished;
            line = glyph;
        }

        if (line is { } remaining)
            yield return remaining;
    }
}
