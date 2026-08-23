namespace Free.Shared.Drawing;

/// <summary>
/// Maps OOXML DrawingML preset geometry names to shared shape kinds and back.
/// </summary>
public static class DrawingMlPresetGeometryMap
{
    private const string RectanglePreset = "rect";

    private static readonly IReadOnlyDictionary<DrawingShapeKind, string> CanonicalPresetsByKind =
        new Dictionary<DrawingShapeKind, string>
        {
            [DrawingShapeKind.Rectangle] = RectanglePreset,
            [DrawingShapeKind.RoundedRectangle] = "roundRect",
            [DrawingShapeKind.Ellipse] = "ellipse",
            [DrawingShapeKind.Line] = "line",
            [DrawingShapeKind.ElbowConnector] = "bentConnector2",
            [DrawingShapeKind.CurvedConnector] = "curvedConnector2",
            [DrawingShapeKind.Triangle] = "triangle",
            [DrawingShapeKind.RightTriangle] = "rtTriangle",
            [DrawingShapeKind.Diamond] = "diamond",
            [DrawingShapeKind.Parallelogram] = "parallelogram",
            [DrawingShapeKind.Trapezoid] = "trapezoid",
            [DrawingShapeKind.Pentagon] = "pentagon",
            [DrawingShapeKind.Hexagon] = "hexagon",
            [DrawingShapeKind.Octagon] = "octagon",
            [DrawingShapeKind.Cross] = "cross",
            [DrawingShapeKind.RightArrow] = "rightArrow",
            [DrawingShapeKind.LeftArrow] = "leftArrow",
            [DrawingShapeKind.UpArrow] = "upArrow",
            [DrawingShapeKind.DownArrow] = "downArrow",
            [DrawingShapeKind.LeftRightArrow] = "leftRightArrow",
            [DrawingShapeKind.UpDownArrow] = "upDownArrow",
            [DrawingShapeKind.PlusSign] = "mathPlus",
            [DrawingShapeKind.MinusSign] = "mathMinus",
            [DrawingShapeKind.MultiplySign] = "mathMultiply",
            [DrawingShapeKind.DivideSign] = "mathDivide",
            [DrawingShapeKind.EqualSign] = "mathEqual",
            [DrawingShapeKind.NotEqualSign] = "mathNotEqual",
            [DrawingShapeKind.FlowchartProcess] = "flowChartProcess",
            [DrawingShapeKind.FlowchartDecision] = "flowChartDecision",
            [DrawingShapeKind.FlowchartData] = "flowChartInputOutput",
            [DrawingShapeKind.FlowchartPredefinedProcess] = "flowChartPredefinedProcess",
            [DrawingShapeKind.FlowchartDocument] = "flowChartDocument",
            [DrawingShapeKind.FlowchartTerminator] = "flowChartTerminator",
            [DrawingShapeKind.Star5] = "star5",
            [DrawingShapeKind.Star8] = "star8",
            [DrawingShapeKind.Explosion] = "irregularSeal1",
            [DrawingShapeKind.Ribbon] = "ribbon",
            [DrawingShapeKind.Wave] = "wave",
            [DrawingShapeKind.RectangularCallout] = "wedgeRectCallout",
            [DrawingShapeKind.RoundedRectangularCallout] = "wedgeRoundRectCallout",
            [DrawingShapeKind.OvalCallout] = "wedgeEllipseCallout",
            [DrawingShapeKind.LineCallout] = "lineCallout1",
            [DrawingShapeKind.Chevron] = "chevron",
            [DrawingShapeKind.HomePlate] = "homePlate",
            [DrawingShapeKind.Cylinder] = "can",
            [DrawingShapeKind.Chord] = "chord",
            [DrawingShapeKind.Heart] = "heart",
            [DrawingShapeKind.QuadArrow] = "quadArrow"
        };

    private static readonly IReadOnlyDictionary<string, DrawingShapeKind> ShapeKindsByPreset = BuildPresetMap();

    /// <summary>Canonical OOXML preset names emitted for each shared shape kind.</summary>
    public static IReadOnlyDictionary<DrawingShapeKind, string> CanonicalPresets => CanonicalPresetsByKind;

    /// <summary>Attempts to map an OOXML preset geometry name to a shared shape kind.</summary>
    public static bool TryGetShapeKind(string? preset, out DrawingShapeKind kind)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            kind = default;
            return false;
        }

        return ShapeKindsByPreset.TryGetValue(preset, out kind);
    }

    /// <summary>Maps an OOXML preset geometry name to a shared shape kind or an app-supplied fallback.</summary>
    public static DrawingShapeKind GetShapeKindOrDefault(
        string? preset,
        DrawingShapeKind defaultKind = DrawingShapeKind.Rectangle) =>
        TryGetShapeKind(preset, out var kind) ? kind : defaultKind;

    /// <summary>Maps a shared shape kind to its canonical OOXML preset geometry name.</summary>
    public static string GetPreset(DrawingShapeKind kind) =>
        CanonicalPresetsByKind.TryGetValue(kind, out var preset) ? preset : RectanglePreset;

    private static IReadOnlyDictionary<string, DrawingShapeKind> BuildPresetMap()
    {
        var map = new Dictionary<string, DrawingShapeKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CanonicalPresetsByKind)
            map[pair.Value] = pair.Key;

        AddAlias(map, "straightConnector1", DrawingShapeKind.Line);

        AddAlias(map, "bentConnector3", DrawingShapeKind.ElbowConnector);
        AddAlias(map, "bentConnector4", DrawingShapeKind.ElbowConnector);
        AddAlias(map, "bentConnector5", DrawingShapeKind.ElbowConnector);

        AddAlias(map, "curvedConnector3", DrawingShapeKind.CurvedConnector);
        AddAlias(map, "curvedConnector4", DrawingShapeKind.CurvedConnector);
        AddAlias(map, "curvedConnector5", DrawingShapeKind.CurvedConnector);

        AddAlias(map, "irregularSeal2", DrawingShapeKind.Explosion);
        AddAlias(map, "ribbon2", DrawingShapeKind.Ribbon);

        AddAlias(map, "lineCallout2", DrawingShapeKind.LineCallout);
        AddAlias(map, "lineCallout3", DrawingShapeKind.LineCallout);
        AddAlias(map, "lineCallout4", DrawingShapeKind.LineCallout);
        AddAlias(map, "borderCallout1", DrawingShapeKind.LineCallout);
        AddAlias(map, "borderCallout2", DrawingShapeKind.LineCallout);
        AddAlias(map, "borderCallout3", DrawingShapeKind.LineCallout);
        AddAlias(map, "borderCallout4", DrawingShapeKind.LineCallout);

        return map;
    }

    private static void AddAlias(
        IDictionary<string, DrawingShapeKind> map,
        string preset,
        DrawingShapeKind kind) =>
        map[preset] = kind;
}
