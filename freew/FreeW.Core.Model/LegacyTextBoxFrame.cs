namespace FreeW.Core.Model;

/// <summary>Horizontal alignment of a positioned legacy (VML) text box (<c>mso-position-horizontal</c>).</summary>
public enum LegacyFrameHorizontalAlignment
{
    /// <summary>Absolute: placed at <see cref="LegacyTextBoxFrame.LeftPt"/> from the reference frame.</summary>
    None,
    Left,
    Center,
    Right
}

/// <summary>Vertical alignment of a positioned legacy (VML) text box (<c>mso-position-vertical</c>).</summary>
public enum LegacyFrameVerticalAlignment
{
    /// <summary>Absolute: placed at <see cref="LegacyTextBoxFrame.TopPt"/> from the reference frame.</summary>
    None,
    Top,
    Center,
    Bottom
}

/// <summary>The frame a legacy text box is positioned against (<c>mso-position-*-relative</c>).</summary>
public enum LegacyFrameReference
{
    /// <summary>The anchoring paragraph / column (VML "text").</summary>
    Text,
    Margin,
    Page,
    LeftMarginArea,
    RightMarginArea,
    TopMarginArea,
    BottomMarginArea
}

/// <summary>Where the text sits vertically inside the box (<c>v-text-anchor</c>).</summary>
public enum LegacyFrameTextAnchor
{
    Top,
    Middle,
    Bottom
}

/// <summary>
/// Render-only geometry of an absolutely positioned legacy VML shape (<c>w:pict/v:oval|v:rect|v:roundrect</c>
/// with a <c>v:textbox</c>) whose <c>w:txbxContent</c> paragraphs a header/footer story flattens into its
/// own paragraph list. Word's "Page Numbers (Margins)" gallery writes the page number this way: a circle in
/// the bottom margin holding a PAGE field. The reader attaches the same instance to every paragraph of one
/// text box (see <see cref="Paragraph.LegacyTextBoxFrame"/>) so composition can lift those paragraphs out of
/// the header/footer flow and draw them inside the shape at its position. The writer never emits it.
/// </summary>
public sealed record LegacyTextBoxFrame(
    bool IsEllipse,
    double WidthPt,
    double HeightPt,
    string? FillColorHex,
    string? StrokeColorHex,
    double StrokeWidthPt,
    LegacyFrameHorizontalAlignment HorizontalAlignment,
    double LeftPt,
    LegacyFrameReference HorizontalReference,
    LegacyFrameVerticalAlignment VerticalAlignment,
    double TopPt,
    LegacyFrameReference VerticalReference,
    bool BehindText,
    LegacyFrameTextAnchor TextAnchor,
    double InsetLeftPt = 7.2,
    double InsetTopPt = 3.6,
    double InsetRightPt = 7.2,
    double InsetBottomPt = 3.6)
{
    /// <summary>
    /// Offset from the box top of the bottom rule of a layout table the text box wraps (the table's exact
    /// row heights, which can be shorter than the box); zero when the box holds no ruled table. Init-only
    /// so the positional constructor stays binary compatible.
    /// </summary>
    public double TableRuleOffsetPt { get; init; }

    /// <summary>Width of the wrapped table's bottom rule in points; zero draws none.</summary>
    public double TableRuleWidthPt { get; init; }

    /// <summary>Colour of the wrapped table's bottom rule (<c>#RRGGBB</c>); null is black.</summary>
    public string? TableRuleColorHex { get; init; }
}
