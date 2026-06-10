namespace FreeX.Core.Model;

public enum DrawingShapeKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Line,
    ElbowConnector,
    CurvedConnector,
    Triangle,
    RightTriangle,
    Diamond,
    Parallelogram,
    Trapezoid,
    Pentagon,
    Hexagon,
    Octagon,
    Cross,
    RightArrow,
    LeftArrow,
    UpArrow,
    DownArrow,
    LeftRightArrow,
    UpDownArrow,
    PlusSign,
    MinusSign,
    MultiplySign,
    DivideSign,
    EqualSign,
    NotEqualSign,
    FlowchartProcess,
    FlowchartDecision,
    FlowchartData,
    FlowchartPredefinedProcess,
    FlowchartDocument,
    FlowchartTerminator,
    Star5,
    Star8,
    Explosion,
    Ribbon,
    Wave,
    RectangularCallout,
    RoundedRectangularCallout,
    OvalCallout,
    LineCallout
}

public enum DrawingShapeEffectPreset
{
    None = 0,
    Shadow = 1,
    Glow = 2,
    SoftEdges = 3,
    InnerShadow = 4,
    Reflection = 5,
    Bevel = 6,
    ThreeDRotation = 7
}

public enum DrawingShapeGradientDirection
{
    DiagonalDown,
    Horizontal,
    Vertical,
    DiagonalUp
}

public sealed class DrawingShapeModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }
    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? Title { get; set; }
    public string? AltText { get; set; }
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
    public CellColor? GradientFillEndColor { get; set; }
    public DrawingShapeGradientDirection GradientFillDirection { get; set; } = DrawingShapeGradientDirection.DiagonalDown;
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }
    public bool HasShadowEffect { get; set; }
    public DrawingShapeEffectPreset EffectPreset { get; set; }
    public bool IsSourceLoaded { get; set; }

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    public DrawingShapeGradientDirection GetEffectiveGradientFillDirection() =>
        Enum.IsDefined(GradientFillDirection)
            ? GradientFillDirection
            : DrawingShapeGradientDirection.DiagonalDown;

    public DrawingShapeEffectPreset GetEffectiveEffectPreset()
    {
        if (Enum.IsDefined(EffectPreset) && EffectPreset != DrawingShapeEffectPreset.None)
            return EffectPreset;

        return HasShadowEffect
            ? DrawingShapeEffectPreset.Shadow
            : DrawingShapeEffectPreset.None;
    }
}
