using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum TableCellBorderVisualEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public sealed record TableCellBorderEdgeVisualPlan(
    TableCellBorderVisualEdge Edge,
    bool IsVisible,
    BorderLineStyle Style,
    string ColorHex,
    double WidthDip,
    string? FallbackNote)
{
    public bool IsDashed => Style == BorderLineStyle.Dashed;
    public bool IsDotted => Style == BorderLineStyle.Dotted;
    public bool IsDouble => Style == BorderLineStyle.Double;
}

public sealed record TableCellBorderVisualPlan(IReadOnlyList<TableCellBorderEdgeVisualPlan> Edges)
{
    public bool HasVisibleEdges => Edges.Any(edge => edge.IsVisible);

    public bool HasWordVisibleStyleEdges => Edges.Any(edge =>
        edge.IsVisible && edge.Style is BorderLineStyle.Dashed or BorderLineStyle.Dotted or BorderLineStyle.Double or BorderLineStyle.Thick or BorderLineStyle.Wave);

    public bool HasMixedVisibleColors => Edges
        .Where(edge => edge.IsVisible)
        .Select(edge => edge.ColorHex)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Skip(1)
        .Any();

    public TableCellBorderEdgeVisualPlan Edge(TableCellBorderVisualEdge edge) =>
        Edges.First(plan => plan.Edge == edge);
}

public static class TableCellBorderVisualPlanner
{
    public const double MinimumBorderWidthDip = 0.5;
    public const double MinimumThickBorderWidthDip = 1.5;

    public static TableCellBorderVisualPlan Build(CellBorders? borders, double dipPerPoint = 1.0)
    {
        var scale = Math.Max(0.01, dipPerPoint);
        return new TableCellBorderVisualPlan(
        [
            BuildEdge(TableCellBorderVisualEdge.Top, borders?.Top, scale),
            BuildEdge(TableCellBorderVisualEdge.Bottom, borders?.Bottom, scale),
            BuildEdge(TableCellBorderVisualEdge.Left, borders?.Left, scale),
            BuildEdge(TableCellBorderVisualEdge.Right, borders?.Right, scale),
        ]);
    }

    private static TableCellBorderEdgeVisualPlan BuildEdge(
        TableCellBorderVisualEdge edge,
        CellBorderEdge? modelEdge,
        double dipPerPoint)
    {
        if (modelEdge is null)
        {
            return new TableCellBorderEdgeVisualPlan(
                edge,
                IsVisible: false,
                BorderLineStyle.Single,
                "#000000",
                0,
                null);
        }

        var widthDip = Math.Max(MinimumBorderWidthDip, modelEdge.WidthPt * dipPerPoint);
        if (modelEdge.Style == BorderLineStyle.Thick)
            widthDip = Math.Max(MinimumThickBorderWidthDip, widthDip);

        return new TableCellBorderEdgeVisualPlan(
            edge,
            IsVisible: true,
            modelEdge.Style,
            NormalizeColor(modelEdge.ColorHex),
            widthDip,
            modelEdge.Style == BorderLineStyle.Wave
                ? "Wave cell borders are planned explicitly but rendered with the host solid-line fallback."
                : null);
    }

    private static string NormalizeColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return "#000000";

        var value = colorHex.Trim();
        return value.StartsWith("#", StringComparison.Ordinal)
            ? value
            : "#" + value;
    }
}
