namespace FreeX.Core.Model;

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
    LineCallout = 41
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
    public static readonly CellColor DefaultFillColor = new(0x5B, 0x9B, 0xD5);
    public static readonly CellColor DefaultOutlineColor = new(0x2F, 0x55, 0x97);

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }
    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HasFill { get; set; } = true;
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

    public CellColor? ResolveFillColor(WorkbookTheme theme, CellColor fallback) =>
        HasFill ? GetEffectiveFillColor(theme, fallback) : null;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    public static CellColor ResolveDefaultFillColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.FillThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.FillColor ??
        DefaultFillColor;

    public static CellColor ResolveDefaultOutlineColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.OutlineThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.OutlineColor ??
        DefaultOutlineColor;

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
