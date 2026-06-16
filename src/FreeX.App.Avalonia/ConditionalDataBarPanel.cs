using Avalonia;
using Avalonia.Controls;

namespace FreeX.App.Avalonia;

/// <summary>
/// A single-child panel that positions a data-bar rectangle horizontally by fraction of the
/// panel's measured content width. The fractions come from the portable conditional-format
/// evaluator (<see cref="ConditionalFormatCellRenderPlanner"/>) and are resolved to pixels here,
/// at arrange time, so the bar tracks the cell's actual rendered width without data binding.
/// </summary>
internal sealed class ConditionalDataBarPanel : Panel
{
    private readonly Control _bar;
    private readonly double _startFraction;
    private readonly double _widthFraction;
    private readonly double _horizontalInset;

    public ConditionalDataBarPanel(Control bar, double startFraction, double widthFraction, double horizontalInset)
    {
        _bar = bar;
        _startFraction = startFraction;
        _widthFraction = widthFraction;
        _horizontalInset = horizontalInset;
        Children.Add(bar);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _bar.Measure(availableSize);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The drawable content area excludes the symmetric horizontal insets the bar layer applies.
        var drawableWidth = Math.Max(0, finalSize.Width - _horizontalInset * 2);
        var barWidth = drawableWidth * _widthFraction;
        var left = _horizontalInset + drawableWidth * _startFraction;
        _bar.Arrange(new Rect(left, 0, Math.Max(0, barWidth), finalSize.Height));
        return finalSize;
    }
}
