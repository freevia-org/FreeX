namespace Freexcel.Core.Model;

public sealed class TextBoxModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }
    public string Text { get; set; } = "";
    public string? AltText { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 80;
    public double RotationDegrees { get; set; }
    public bool IsVisible { get; set; } = true;
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }
    public bool IsSourceLoaded { get; set; }

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;
}
