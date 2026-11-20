using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Avalonia;

/// <summary>Selects text across the positioned text runs on a preview page.</summary>
internal sealed class PrintPreviewTextCanvas : Canvas
{
    private static readonly IBrush SelectionHighlight = new SolidColorBrush(Color.FromRgb(173, 214, 255));
    private readonly List<(int InstructionIndex, SelectableTextBlock Block)> _runs = [];
    private readonly List<SelectableTextBlock> _selectedRuns = [];
    private bool _runsOrdered;
    private TextPosition? _anchor;
    private TextPosition? _selectionEnd;

    public event EventHandler? SelectionChanged;

    public bool HasSelection => _selectedRuns.Count > 0;

    public PrintPreviewTextCanvas()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    public void RegisterText(int instructionIndex, SelectableTextBlock block)
    {
        block.IsHitTestVisible = false;
        block.Focusable = false;
        block.SelectionBrush = SelectionHighlight;
        _runs.Add((instructionIndex, block));
        _runsOrdered = false;
    }

    public string GetSelectedText()
    {
        var text = new StringBuilder();
        double? lastTop = null;
        foreach (var run in _selectedRuns)
        {
            var top = GetTop(run);
            if (lastTop is { } previousTop)
                text.Append(Math.Abs(previousTop - top) < 2 ? "\t" : Environment.NewLine);
            text.Append(run.SelectedText);
            lastTop = top;
        }
        return text.ToString();
    }

    public SelectableTextBlock? SelectMatch(PrintPreviewTextMatch match)
    {
        EnsureReadingOrder();
        var run = _runs.FindIndex(entry => entry.InstructionIndex == match.InstructionIndex);
        if (run < 0)
            return null;

        SetSelection(new TextPosition(run, match.Start), new TextPosition(run, match.Start + match.Length));
        return _runs[run].Block;
    }

    public void ClearSelection() => SetSelection(null, null);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        var hit = HitText(e.GetPosition(this), exact: true);
        if (hit is null)
        {
            ClearSelection();
            return;
        }

        var anchor = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _anchor ?? hit : hit;
        SetSelection(anchor, hit);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            SetSelection(_anchor, HitText(e.GetPosition(this), exact: false));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured != this || e.InitialPressMouseButton != MouseButton.Left)
            return;

        SetSelection(_anchor, HitText(e.GetPosition(this), exact: false));
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private TextPosition? HitText(Point point, bool exact)
    {
        EnsureReadingOrder();
        var bestRun = -1;
        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < _runs.Count; index++)
        {
            var bounds = _runs[index].Block.Bounds;
            var dx = Math.Max(0, Math.Max(bounds.X - point.X, point.X - bounds.Right));
            var dy = Math.Max(0, Math.Max(bounds.Y - point.Y, point.Y - bounds.Bottom));
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance)
                continue;
            bestRun = index;
            bestDistance = distance;
        }

        if (bestRun < 0 || (exact && bestDistance > 0))
            return null;

        var run = _runs[bestRun].Block;
        var relativePoint = new Point(point.X - run.Bounds.X, point.Y - run.Bounds.Y);
        var hit = run.TextLayout.HitTestPoint(relativePoint);
        return new TextPosition(bestRun, hit.TextPosition);
    }

    private void SetSelection(TextPosition? anchor, TextPosition? end)
    {
        if (_anchor == anchor && _selectionEnd == end)
            return;

        _anchor = anchor;
        _selectionEnd = end;
        foreach (var run in _selectedRuns)
            run.ClearSelection();
        _selectedRuns.Clear();

        if (anchor is { } first && end is { } last && first != last)
        {
            if (first.Run > last.Run || (first.Run == last.Run && first.Offset > last.Offset))
                (first, last) = (last, first);

            for (var index = first.Run; index <= last.Run; index++)
            {
                var run = _runs[index].Block;
                var start = index == first.Run ? first.Offset : 0;
                var stop = index == last.Run ? last.Offset : run.Text!.Length;
                if (start >= stop)
                    continue;
                run.SelectionStart = start;
                run.SelectionEnd = stop;
                _selectedRuns.Add(run);
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureReadingOrder()
    {
        if (_runsOrdered)
            return;

        _runs.Sort((left, right) =>
        {
            var top = GetTop(left.Block).CompareTo(GetTop(right.Block));
            if (top != 0)
                return top;
            var column = GetLeft(left.Block).CompareTo(GetLeft(right.Block));
            return column != 0 ? column : left.InstructionIndex.CompareTo(right.InstructionIndex);
        });
        _runsOrdered = true;
    }

    private readonly record struct TextPosition(int Run, int Offset);
}
