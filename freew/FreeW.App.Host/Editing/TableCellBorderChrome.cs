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

        var pen = CreatePen(edge);
        foreach (var segment in TableCellBorderVisualPlanner.BuildStrokeSegments(
                     edge,
                     rect.Left,
                     rect.Top,
                     rect.Right,
                     rect.Bottom,
                     waveRegistrationDip: 2.0))
        {
            drawingContext.DrawLine(
                pen,
                new Point(segment.X1Dip, segment.Y1Dip),
                new Point(segment.X2Dip, segment.Y2Dip));
        }
    }

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

}
