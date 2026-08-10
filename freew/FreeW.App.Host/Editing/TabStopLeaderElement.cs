using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Draws a paragraph tab leader and exposes its minimal paint state to WPF XAML cloning for print and
/// pagination. Editor-only metadata remains on the containing inline and is stripped before cloning.
/// </summary>
public sealed class TabStopLeaderElement : FrameworkElement
{
    public TabStopLeaderElement()
    {
    }

    internal TabStopLeaderElement(ParagraphTabStopPlacementPlan plan, Brush brush)
    {
        Leader = plan.Leader;
        BrushToken = brush is SolidColorBrush solid ? solid.Color.ToString() : Colors.Black.ToString();
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TabLeader Leader { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string BrushToken { get; set; } = Colors.Black.ToString();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Leader == TabLeader.None || ActualWidth <= 1)
            return;

        var brush = new SolidColorBrush(ParseColor(BrushToken));
        var pen = new Pen(brush, 1);
        switch (Leader)
        {
            case TabLeader.Underline:
                drawingContext.DrawLine(pen, new Point(0, 0.5), new Point(ActualWidth, 0.5));
                break;
            case TabLeader.Dots:
                for (var x = 2.0; x < ActualWidth - 1; x += 5)
                    drawingContext.DrawEllipse(brush, null, new Point(x, 0.5), 1, 1);
                break;
            case TabLeader.Dashes:
                for (var x = 1.0; x < ActualWidth - 1; x += 7)
                    drawingContext.DrawLine(pen, new Point(x, 0.5), new Point(Math.Min(x + 4, ActualWidth), 0.5));
                break;
        }
    }

    private static Color ParseColor(string token)
    {
        try
        {
            return ColorConverter.ConvertFromString(token) is Color color ? color : Colors.Black;
        }
        catch (FormatException)
        {
            return Colors.Black;
        }
    }
}
