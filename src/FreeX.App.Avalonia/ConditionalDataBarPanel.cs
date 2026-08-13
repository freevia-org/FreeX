using Avalonia;
using Avalonia.Controls;
using FreeX.App.Presentation.ConditionalFormatting;

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
    // P53: optional zero-crossing axis-line control, positioned at _axisFraction of the drawable
    // width (mirrors WPF's axis line in GridView.ConditionalDataBars.cs). Null when the data bar
    // has no interior axis (e.g. an all-positive or all-negative value range).
    private readonly Control? _axisLine;
    private readonly double _axisFraction;

    public ConditionalDataBarPanel(
        Control bar,
        double startFraction,
        double widthFraction,
        double horizontalInset,
        Control? axisLine = null,
        double axisFraction = 0d)
    {
        _bar = bar;
        _startFraction = startFraction;
        _widthFraction = widthFraction;
        _horizontalInset = horizontalInset;
        _axisLine = axisLine;
        _axisFraction = axisFraction;
        Children.Add(bar);
        if (axisLine is not null)
            Children.Add(axisLine);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _bar.Measure(availableSize);
        _axisLine?.Measure(availableSize);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The drawable content area excludes the symmetric horizontal insets the bar layer applies.
        var drawableWidth = Math.Max(0, finalSize.Width - _horizontalInset * 2);
        var barWidth = drawableWidth * _widthFraction;
        var left = _horizontalInset + drawableWidth * _startFraction;
        _bar.Arrange(new Rect(left, 0, Math.Max(0, barWidth), finalSize.Height));

        if (_axisLine is not null)
        {
            var axisWidth = Math.Max(1d, _axisLine.DesiredSize.Width);
            var axisLeft = _horizontalInset + drawableWidth * _axisFraction - axisWidth / 2d;
            _axisLine.Arrange(new Rect(axisLeft, 0, axisWidth, finalSize.Height));
        }

        return finalSize;
    }
}
