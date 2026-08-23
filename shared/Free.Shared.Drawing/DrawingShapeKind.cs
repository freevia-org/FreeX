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
    HomePlate = 43,
    Cylinder = 44,
    Chord = 45,
    Heart = 46,
    QuadArrow = 47
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
    Media = 7,

    /// <summary>
    /// An embedded OLE object (p:graphicFrame / p:oleObj). The embedded binary is in
    /// <c>SlideShape.OleObject</c>; the fallback preview image is in <c>SlideShape.Picture</c>.
    /// </summary>
    Ole = 8,

    /// <summary>
    /// A slide zoom / summary zoom (p:graphicFrame with zoom namespace URI).
    /// The raw frame XML + preview image are preserved verbatim.
    /// </summary>
    Zoom = 9,

    /// <summary>
    /// An ink annotation (p:contentPart referencing an InkML part).
    /// The raw contentPart XML + ink bytes + fallback image are preserved verbatim.
    /// </summary>
    Ink = 10,

    /// <summary>
    /// A 3D model (p:graphicFrame with am3d namespace URI).
    /// The raw frame XML + .glb bytes + preview image are preserved verbatim.
    /// </summary>
    Model3d = 11,

    /// <summary>
    /// A graphicFrame or contentPart with an unknown/unrecognized URI.
    /// The raw XML + any referenced part bytes are preserved verbatim so nothing is silently lost.
    /// </summary>
    PreservedObject = 12,
}
