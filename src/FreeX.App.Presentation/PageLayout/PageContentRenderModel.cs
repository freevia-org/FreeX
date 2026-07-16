using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Per-edge border style for a printed cell block, mirroring the four cell border edges. Each edge
/// carries the model <see cref="BorderStyle"/> and a <see cref="PresentationRgb"/> color so a
/// renderer can draw the edge without consulting any model styling type.
/// </summary>
public readonly record struct PageBorderEdge(BorderStyle Style, PresentationRgb Color)
{
    /// <summary>An edge with no line.</summary>
    public static readonly PageBorderEdge None = new(BorderStyle.None, default);

    /// <summary>Whether this edge should be drawn.</summary>
    public bool IsVisible => Style != BorderStyle.None;
}

/// <summary>The four border edges resolved for one printed cell block.</summary>
public readonly record struct PageCellBorders(
    PageBorderEdge Top,
    PageBorderEdge Right,
    PageBorderEdge Bottom,
    PageBorderEdge Left)
{
    /// <summary>All edges absent.</summary>
    public static readonly PageCellBorders None = new(
        PageBorderEdge.None,
        PageBorderEdge.None,
        PageBorderEdge.None,
        PageBorderEdge.None);

    /// <summary>Whether any edge should be drawn.</summary>
    public bool HasAny => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;
}

/// <summary>
/// The resolved font for a printed run: family name plus weight/slant/size and color, in
/// device-independent units, carrying no platform font type.
/// </summary>
public readonly record struct PageTextFont(
    string FontFamily,
    double FontSize,
    bool Bold,
    bool Italic,
    PresentationRgb Color);

/// <summary>Horizontal placement of a run within its block.</summary>
public enum PageTextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// One printed cell drawn on the page: its pixel rectangle, the optional fill, the resolved display
/// text + font + horizontal alignment, the four border edges, and a vertically-centered text origin
/// (top-left of the text block, measured via the supplied text measurer) the renderer can draw from.
/// A merged cell is reported once as the anchor block, sized to span the merged region clipped to the
/// page. <see cref="Fill"/> and <see cref="Font"/>'s color already carry the cell's conditional-format
/// result merged over its raw style (see <c>PageContentRenderModelBuilder.EvaluateConditionalFormat</c>);
/// <see cref="DataBar"/>/<see cref="IconSet"/> carry the resolved data-bar/icon-set conditional format
/// (when the cell's highest-priority matching rule of that kind produced one) for a renderer to paint
/// as its own overlay glyph -- no current renderer paints them yet, so they render as absent (fill/text
/// only) until a PDF renderer adds that drawing step.
/// </summary>
public sealed record PageCellBlock(
    LayoutRect Bounds,
    uint Row,
    uint Column,
    PresentationRgb? Fill,
    string Text,
    PageTextFont Font,
    PageTextAlignment Alignment,
    PageCellBorders Borders,
    LayoutPoint TextOrigin,
    DataBarLayout? DataBar = null,
    IconSetResult? IconSet = null);

/// <summary>A single straight gridline segment between two pixel-space endpoints.</summary>
public readonly record struct PageGridLine(LayoutPoint Start, LayoutPoint End);

/// <summary>
/// One row or column heading cell: its pixel rectangle, the centered label (the column name such as
/// "A"/"AB", or the 1-based row number), and a vertically-centered text origin. Headings are only
/// produced when print-headings is on.
/// </summary>
public sealed record PageHeadingCell(LayoutRect Bounds, string Label, LayoutPoint TextOrigin);

/// <summary>
/// One formatted text run within a header or footer band section.  Each run carries its own styling
/// that was decoded from the Excel format-code sequence (e.g. <c>&amp;B</c> bold, <c>&amp;I</c>
/// italic, <c>&amp;"Arial,Bold"</c> font override, <c>&amp;14</c> size, <c>&amp;Krrggbb</c> color).
/// Multiple runs are concatenated left-to-right to form the visible section text.
/// </summary>
public sealed record HeaderFooterFormattedRun(
    string Text,
    bool Bold,
    bool Italic,
    bool Underline,
    bool DoubleUnderline,
    bool Strikethrough,
    string? FontName,
    double? FontSize,
    PresentationRgb? Color);

/// <summary>
/// One header- or footer-band run: its pixel rectangle (one of the three left/center/right thirds),
/// the plain concatenated text (for sizing / PDF overlay), the formatted sub-runs that carry
/// per-run font/style overrides from Excel format codes, the horizontal alignment within the band,
/// and a vertically-centered text origin (top-left of the text block) the renderer can draw from.
/// </summary>
public sealed record PageHeaderFooterRun(
    LayoutRect Bounds,
    string Text,
    IReadOnlyList<HeaderFooterFormattedRun> FormattedRuns,
    PageTextAlignment Alignment,
    LayoutPoint TextOrigin);

/// <summary>
/// One printed worksheet text box resolved into page-space geometry: the outer rectangle, the inner
/// text rectangle, resolved fill/outline colors, and the text/font the renderer can paint without
/// consulting the workbook model.
/// </summary>
public sealed record PageTextBoxBlock(
    Guid Id,
    LayoutRect Bounds,
    LayoutRect TextBounds,
    string Text,
    PresentationRgb? Fill,
    byte FillAlpha,
    PresentationRgb Outline,
    double OutlineThickness,
    PageTextFont Font);

/// <summary>
/// One printed worksheet chart resolved into page-space geometry. This first portable object-layer
/// slice carries the chart object's page rectangle plus resolved chart-area colors and selectable text
/// overlays; platform renderers can paint a chart visual or a bounded placeholder without consulting
/// the worksheet for filtering or overlay placement.
/// </summary>
public sealed record PageChartBlock(
    Guid Id,
    LayoutRect Bounds,
    PresentationRgb Fill,
    PresentationRgb Outline,
    double OutlineThickness,
    IReadOnlyList<PrintChartTextOverlayPlan> TextOverlays);

/// <summary>
/// The complete, backend-agnostic content of one printed page: the page rectangle, the printable
/// area inset by margins, and ordered render instructions a renderer paints in list order (fills,
/// gridlines, the outer grid border, headings, cell text/borders, charts, text boxes, and the
/// header/footer bands). All geometry is in device-independent units (96 dpi) with origin top-left, y growing
/// downward.
/// </summary>
public sealed record PageContentLayout(
    int PageNumber,
    LayoutRect PageBounds,
    LayoutRect PrintableArea,
    LayoutRect GridBounds,
    IReadOnlyList<PageCellBlock> Cells,
    IReadOnlyList<PageGridLine> GridLines,
    IReadOnlyList<PageHeadingCell> ColumnHeadings,
    IReadOnlyList<PageHeadingCell> RowHeadings,
    IReadOnlyList<PageChartBlock> Charts,
    IReadOnlyList<PageTextBoxBlock> TextBoxes,
    IReadOnlyList<PageHeaderFooterRun> HeaderRuns,
    IReadOnlyList<PageHeaderFooterRun> FooterRuns)
{
    public bool PrintGridlines => GridLines.Count > 0;
    public bool PrintHeadings => ColumnHeadings.Count > 0 || RowHeadings.Count > 0;
}
