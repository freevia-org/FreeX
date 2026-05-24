namespace Freexcel.Core.Model;

public enum DrawingShapeKind
{
    Rectangle,
    Ellipse,
    Line
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
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }
    public bool HasShadowEffect { get; set; }
    public bool IsSourceLoaded { get; set; }

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;
}
