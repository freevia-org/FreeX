using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Draws the renderer-neutral per-edge table border plan. The public parameterless constructor and
/// serialized plan token allow WPF's print paginator to clone the element through XAML.
/// </summary>
public sealed class TableCellBorderChrome : FrameworkElement
{
    private TableCellBorderVisualPlan _plan = new([]);
    private string _planToken = "[]";

    public TableCellBorderChrome()
    {
    }

    internal TableCellBorderChrome(TableCellBorderVisualPlan plan)
    {
        SetPlan(plan);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string PlanToken
    {
        get => _planToken;
        set
        {
            var edges = DeserializePlanToken(value);
            _plan = new TableCellBorderVisualPlan(edges);
            _planToken = JsonSerializer.Serialize(edges);
            InvalidateVisual();
        }
    }

    private static TableCellBorderEdgeVisualPlan[] DeserializePlanToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A table border plan token is required.", nameof(token));

        TableCellBorderEdgeVisualPlan?[] parsedEdges;
        try
        {
            parsedEdges = JsonSerializer.Deserialize<TableCellBorderEdgeVisualPlan?[]>(token) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The table border plan token is not valid JSON.", nameof(token), ex);
        }

        var expectedEdges = Enum.GetValues<TableCellBorderVisualEdge>();
        if (parsedEdges.Length != expectedEdges.Length || parsedEdges.Any(edge => edge is null))
            throw new ArgumentException("The table border plan token must define one valid plan per edge.", nameof(token));

        var edges = parsedEdges.Select(edge => edge!).ToArray();
        if (edges.Select(edge => edge.Edge).Distinct().Count() != expectedEdges.Length
            || expectedEdges.Any(expected => edges.All(edge => edge.Edge != expected))
            || edges.Any(edge => !Enum.IsDefined(edge.Edge)
                || !Enum.IsDefined(edge.Style)
                || !double.IsFinite(edge.WidthDip)
                || edge.WidthDip < 0
                || string.IsNullOrWhiteSpace(edge.ColorHex)))
        {
            throw new ArgumentException("The table border plan token must define one valid plan per edge.", nameof(token));
        }

        return edges;
    }

    private void SetPlan(TableCellBorderVisualPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        _planToken = JsonSerializer.Serialize(plan.Edges);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        foreach (var edge in _plan.Edges)
            DrawEdge(drawingContext, rect, edge);
    }

    private static void DrawEdge(DrawingContext drawingContext, Rect rect, TableCellBorderEdgeVisualPlan edge)
    {
        if (!edge.IsVisible)
            return;

        var (p1, p2) = CellBorderPoints(edge.Edge, rect, 0);
        var pen = CreatePen(edge);

        if (edge.Style == BorderLineStyle.Wave)
        {
            DrawWaveEdge(drawingContext, rect, edge, pen);
            return;
        }

        if (edge.Style == BorderLineStyle.Double)
        {
            var offset = Math.Max(1.0, edge.WidthDip * 1.5);
            var (outer1, outer2) = CellBorderPoints(edge.Edge, rect, -offset / 2);
            var (inner1, inner2) = CellBorderPoints(edge.Edge, rect, offset / 2);
            drawingContext.DrawLine(pen, outer1, outer2);
            drawingContext.DrawLine(pen, inner1, inner2);
            return;
        }

        drawingContext.DrawLine(pen, p1, p2);
    }

    private static void DrawWaveEdge(
        DrawingContext drawingContext,
        Rect rect,
        TableCellBorderEdgeVisualPlan edge,
        Pen pen)
    {
        const double registrationDip = 2.0;
        var length = edge.Edge is TableCellBorderVisualEdge.Top or TableCellBorderVisualEdge.Bottom
            ? rect.Width
            : rect.Height;
        var offsets = TableCellBorderVisualPlanner.BuildWaveOffsets(length);
        if (offsets.Count < 2)
            return;

        var previous = WavePoint(
            edge.Edge,
            rect,
            offsets[0].AlongDip,
            registrationDip + offsets[0].OutwardDip);
        foreach (var offset in offsets.Skip(1))
        {
            var current = WavePoint(
                edge.Edge,
                rect,
                offset.AlongDip,
                registrationDip + offset.OutwardDip);
            drawingContext.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    private static Point WavePoint(
        TableCellBorderVisualEdge edge,
        Rect rect,
        double along,
        double outward) => edge switch
        {
            TableCellBorderVisualEdge.Top => new Point(rect.Left + along, rect.Top - outward),
            TableCellBorderVisualEdge.Bottom => new Point(rect.Left + along, rect.Bottom + outward),
            TableCellBorderVisualEdge.Left => new Point(rect.Left - outward, rect.Top + along),
            TableCellBorderVisualEdge.Right => new Point(rect.Right + outward, rect.Top + along),
            _ => new Point(rect.Left + along, rect.Top - outward),
        };

    private static Pen CreatePen(TableCellBorderEdgeVisualPlan edge)
    {
        var color = ParseColor(edge.ColorHex, Colors.Black);
        if (edge.Style == BorderLineStyle.Wave)
            color = Color.FromArgb((byte)Math.Round(255 * edge.StrokeOpacity), color.R, color.G, color.B);

        var pen = new Pen(new SolidColorBrush(color), edge.WidthDip)
        {
            DashStyle = edge.Style switch
            {
                BorderLineStyle.Dashed => DashStyles.Dash,
                BorderLineStyle.Dotted => DashStyles.Dot,
                _ => null
            }
        };
        return pen;
    }

    private static Color ParseColor(string token, Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(token) is Color color ? color : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static (Point Start, Point End) CellBorderPoints(
        TableCellBorderVisualEdge edge,
        Rect rect,
        double inwardOffset) => edge switch
        {
            TableCellBorderVisualEdge.Top => (
                new Point(rect.Left, rect.Top + inwardOffset),
                new Point(rect.Right, rect.Top + inwardOffset)),
            TableCellBorderVisualEdge.Bottom => (
                new Point(rect.Left, rect.Bottom - inwardOffset),
                new Point(rect.Right, rect.Bottom - inwardOffset)),
            TableCellBorderVisualEdge.Left => (
                new Point(rect.Left + inwardOffset, rect.Top),
                new Point(rect.Left + inwardOffset, rect.Bottom)),
            TableCellBorderVisualEdge.Right => (
                new Point(rect.Right - inwardOffset, rect.Top),
                new Point(rect.Right - inwardOffset, rect.Bottom)),
            _ => (new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top)),
        };
}
