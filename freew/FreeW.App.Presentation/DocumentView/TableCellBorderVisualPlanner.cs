using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum TableCellBorderVisualEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public sealed record TableCellBorderWavePoint(double AlongDip, double OutwardDip);

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
    public bool IsWave => Style == BorderLineStyle.Wave;
    public double StrokeOpacity => IsWave ? TableCellBorderVisualPlanner.WaveStrokeOpacity : 1.0;
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
    public const double WaveLengthDip = 8.0;
    public const double WaveAmplitudeDip = 4.0;
    public const double WaveStrokeOpacity = 86.0 / 255.0;

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

    public static IReadOnlyList<TableCellBorderWavePoint> BuildWaveOffsets(double lengthDip)
    {
        var length = Math.Max(0, lengthDip);
        if (length <= 0)
            return [];

        var points = new List<TableCellBorderWavePoint>((int)Math.Ceiling(length) + 1)
        {
            new(0, 0)
        };
        for (var along = 1.0; along < length; along += 1.0)
        {
            var phase = (along % WaveLengthDip) / WaveLengthDip;
            var outward = WaveAmplitudeDip * (1 - Math.Cos(phase * Math.PI * 2)) / 2;
            points.Add(new TableCellBorderWavePoint(along, outward));
        }

        points.Add(new TableCellBorderWavePoint(length, 0));
        return points;
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
            null);
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
