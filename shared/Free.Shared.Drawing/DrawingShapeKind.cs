namespace Free.Shared.Drawing;

/// <summary>
/// Preset shape kinds understood by the geometry engine. Ported from FreeX.Core.Model.DrawingShapeKind.
/// Values are preserved for cross-project compatibility.
/// </summary>
public enum DrawingShapeKind
{
    Rectangle = 0,
    Ellipse = 1,
    Line = 2,
    RoundedRectangle = 3,
    ElbowConnector = 4,
    CurvedConnector = 5,
    Triangle = 6,
    RightTriangle = 7,
    Diamond = 8,
    Parallelogram = 9,
    Trapezoid = 10,
    Pentagon = 11,
    Hexagon = 12,
    Octagon = 13,
    Cross = 14,
    RightArrow = 15,
    LeftArrow = 16,
    UpArrow = 17,
    DownArrow = 18,
    LeftRightArrow = 19,
    UpDownArrow = 20,
    PlusSign = 21,
    MinusSign = 22,
    MultiplySign = 23,
    DivideSign = 24,
    EqualSign = 25,
    NotEqualSign = 26,
    FlowchartProcess = 27,
    FlowchartDecision = 28,
    FlowchartData = 29,
    FlowchartPredefinedProcess = 30,
    FlowchartDocument = 31,
    FlowchartTerminator = 32,
    Star5 = 33,
    Star8 = 34,
    Explosion = 35,
    Ribbon = 36,
    Wave = 37,
    RectangularCallout = 38,
    RoundedRectangularCallout = 39,
    OvalCallout = 40,
    LineCallout = 41,
    Chevron = 42,
    HomePlate = 43
}

/// <summary>
/// Shape kind discriminator for <see cref="SlideShape"/> in FreeP — extends the preset set
/// with presentation-specific kinds (Picture, Group, Table, Connector).
/// </summary>
public enum SlideShapeKind
{
    /// <summary>A preset autoshape geometry defined by <see cref="DrawingShapeKind"/>.</summary>
    AutoShape = 0,

    /// <summary>A raster or vector image.</summary>
    Picture = 1,

    /// <summary>A group of child shapes.</summary>
    Group = 2,

    /// <summary>A table shape.</summary>
    Table = 3,

    /// <summary>A connector (line/arrow) between two shapes.</summary>
    Connector = 4,

    /// <summary>An embedded chart (p:graphicFrame with c:chart graphicData).</summary>
    Chart = 5,

    /// <summary>A SmartArt graphic (p:graphicFrame with dgm: graphicData). Rendered via cached dsp:drawing fallback shapes.</summary>
    SmartArt = 6,

    /// <summary>An audio or video media object. The poster image is in <c>Picture</c>; the media bytes are in <c>Media</c>.</summary>
    Media = 7
}
