namespace Freexcel.Core.Model;

public sealed class TextBoxModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CellAddress Anchor { get; set; }
    public string Text { get; set; } = "";
    public string? AltText { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 80;
    public double RotationDegrees { get; set; }
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
}
