namespace FreeX.Core.Model;

public sealed class TextBoxModel
{
    public const double DefaultWidth = 180d;
    public const double DefaultHeight = 80d;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }

    /// <summary>Horizontal sub-cell offset (in DIP pixels, EMU/9525) from the left edge of the
    /// <see cref="Anchor"/> cell to the text box's left edge, preserved from the authored anchor's
    /// <c>from/colOff</c> so the render reflects the true sub-cell position.</summary>
    public double AnchorOffsetX { get; set; }

    /// <summary>Vertical sub-cell offset (in DIP pixels, EMU/9525) from the top edge of the
    /// <see cref="Anchor"/> cell to the text box's top edge, preserved from the authored anchor's
    /// <c>from/rowOff</c>.</summary>
    public double AnchorOffsetY { get; set; }

    public string Text { get; set; } = "";
    public string? Title { get; set; }
    public string? AltText { get; set; }
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HasFill { get; set; } = true;
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }
    public bool IsSourceLoaded { get; set; }

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor? ResolveFillColor(WorkbookTheme theme, CellColor fallback) =>
        HasFill ? GetEffectiveFillColor(theme, fallback) : null;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    public static TextBoxModel? FindById(IEnumerable<TextBoxModel> textBoxes, Guid textBoxId)
    {
        ArgumentNullException.ThrowIfNull(textBoxes);

        foreach (var textBox in textBoxes)
        {
            if (textBox.Id == textBoxId)
                return textBox;
        }

        return null;
    }
}
