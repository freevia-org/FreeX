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

    // ── Text formatting (txBody) ────────────────────────────────────────────
    // Mirrors DrawingShapeModel's ShapeText* fields (same flattened, first-run-only
    // simplification -- see XlsxWorksheetDrawingParts.ReadShapeTextFormatting) so a text box's
    // rich-text formatting survives a load -> Duplicate Sheet -> save round-trip instead of being
    // silently dropped (the model previously had no fields to carry it, so DuplicateSheetCommand
    // stripped it and a real xlsx load never populated it -- backlog textbox-6-2).

    /// <summary>
    /// Font family/typeface for the text box's text, from the first run's
    /// <c>&lt;a:rPr&gt;&lt;a:latin typeface="..."/&gt;</c>. <see langword="null"/> means "no
    /// explicit font family authored" -- the renderer/Excel falls back to the theme's minor font.
    /// </summary>
    public string? TextFontFamily { get; set; }

    /// <summary>
    /// Font size for the first run's <c>&lt;a:rPr sz&gt;</c>, in points (OOXML stores hundredths
    /// of a point; divide by 100 when reading). Zero or negative means "inherit default".
    /// </summary>
    public double TextFontSizePoints { get; set; }

    /// <summary>Bold (<c>&lt;a:rPr b="1"/&gt;</c>).</summary>
    public bool TextBold { get; set; }

    /// <summary>Italic (<c>&lt;a:rPr i="1"/&gt;</c>).</summary>
    public bool TextItalic { get; set; }

    /// <summary>
    /// Explicit font color from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:srgbClr&gt;</c>.
    /// <see langword="null"/> means "no explicit color" -- renderer uses a default (e.g. black).
    /// </summary>
    public CellColor? TextColor { get; set; }

    /// <summary>
    /// Theme-based font color (from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:schemeClr&gt;</c>).
    /// Takes precedence over <see cref="TextColor"/> when non-null.
    /// </summary>
    public WorkbookThemeColorReference? TextThemeColor { get; set; }

    /// <summary>Horizontal paragraph alignment from <c>&lt;a:pPr algn="l|ctr|r"/&gt;</c>.</summary>
    public DrawingShapeTextHAlign TextHAlign { get; set; } = DrawingShapeTextHAlign.Left;

    /// <summary>
    /// Vertical text anchor from <c>&lt;a:bodyPr anchor="t|ctr|b"/&gt;</c>. Defaults to
    /// <see cref="DrawingShapeTextVAnchor.Top"/> -- unlike <c>DrawingShapeModel.ShapeTextVAnchor</c>
    /// (which defaults to Middle), a plain Excel-authored text box's bodyPr genuinely defaults to
    /// top-anchored, and this is also the value a brand-new (never-loaded) FreeX text box needs so
    /// the writer's now-unconditional explicit anchor attribute reproduces the same rendered
    /// position a fresh text box always had before this field existed.
    /// </summary>
    public DrawingShapeTextVAnchor TextVAnchor { get; set; } = DrawingShapeTextVAnchor.Top;

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor? ResolveFillColor(WorkbookTheme theme, CellColor fallback) =>
        HasFill ? GetEffectiveFillColor(theme, fallback) : null;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    /// <summary>
    /// Resolves the effective text color, preferring the theme reference when present.
    /// Returns <see langword="null"/> when neither an explicit nor a theme color is set.
    /// </summary>
    public CellColor? ResolveTextColor(WorkbookTheme theme) =>
        TextThemeColor?.Resolve(theme) ?? TextColor;

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
