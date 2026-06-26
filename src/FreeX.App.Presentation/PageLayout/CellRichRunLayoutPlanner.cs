using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Resolves per-run rich-text properties against a cell's base <see cref="CellStyle"/>,
/// producing a flat list of <see cref="ResolvedCellTextRun"/> that the desktop renderers
/// can consume directly without re-reading the model.
/// </summary>
/// <remarks>
/// Design contract:
/// <list type="bullet">
///   <item>
///     A null property on a <see cref="CellTextRun"/> means "inherit from the cell style".
///     This planner fills those gaps using the cell's <see cref="CellStyle"/>.
///   </item>
///   <item>
///     Super/subscript sizing follows Excel's convention: ±33% of the resolved font size.
///   </item>
///   <item>
///     Run colors are stored as <see cref="CellRunColor"/> which may reference the workbook
///     theme.  Pass the theme and indexed-color palette to <see cref="Resolve(IReadOnlyList{CellTextRun}?,CellStyle,WorkbookTheme,WorkbookIndexedColorPalette)"/>
///     for accurate resolution; the two-argument overload falls back to the cell-style color.
///   </item>
/// </list>
/// </remarks>
public static class CellRichRunLayoutPlanner
{
    private const double SuperSubSizeFactor = 0.67;

    /// <summary>
    /// Resolves the rich-text runs for a cell, coalescing null per-run properties with the
    /// cell style defaults.  Pass a <see cref="WorkbookTheme"/> via the four-argument overload
    /// for accurate theme-color resolution.
    /// </summary>
    public static IReadOnlyList<ResolvedCellTextRun> Resolve(
        IReadOnlyList<CellTextRun>? runs,
        CellStyle cellStyle) =>
        Resolve(runs, cellStyle, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

    /// <summary>
    /// Resolves the rich-text runs for a cell, coalescing null per-run properties with the
    /// cell style defaults.
    /// </summary>
    /// <param name="runs">Raw runs from <see cref="Sheet.RichTextRuns"/>.</param>
    /// <param name="cellStyle">The resolved cell style (base font, color, …).</param>
    /// <param name="theme">Workbook theme used to resolve theme-color references on runs.</param>
    /// <param name="indexedColors">Indexed-color palette used to resolve legacy indexed colors on runs.</param>
    /// <returns>
    /// A list of fully-resolved runs ready for the renderer.  Never null; an empty list means
    /// no rich runs (caller falls back to plain-text rendering).
    /// </returns>
    public static IReadOnlyList<ResolvedCellTextRun> Resolve(
        IReadOnlyList<CellTextRun>? runs,
        CellStyle cellStyle,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        if (runs is null or { Count: 0 })
            return [];

        var result = new List<ResolvedCellTextRun>(runs.Count);
        foreach (var run in runs)
        {
            var bold         = run.Bold         ?? cellStyle.Bold;
            var italic       = run.Italic       ?? cellStyle.Italic;
            var underline    = run.Underline     ?? cellStyle.Underline;
            var strikethrough = run.Strikethrough ?? cellStyle.Strikethrough;
            var fontName     = run.FontName ?? cellStyle.FontName;
            var baseSize     = run.FontSize ?? cellStyle.FontSize;
            var color        = run.FontColor is { } runColor
                                   ? runColor.Resolve(theme, indexedColors)
                                   : cellStyle.FontColor;
            var vertAlign    = run.VertAlign;

            // Apply super/subscript size scaling (Excel convention: 67% of base size).
            var renderedSize = vertAlign != CellTextRunVertAlign.None
                ? baseSize * SuperSubSizeFactor
                : baseSize;

            result.Add(new ResolvedCellTextRun(
                run.Text,
                bold,
                italic,
                underline,
                strikethrough,
                fontName,
                renderedSize,
                baseSize,
                color,
                vertAlign));
        }

        return result;
    }
}

/// <summary>
/// A fully-resolved rich-text run with all properties coalesced against the cell style.
/// Consumed by the desktop renderers (Waves 2 and 3).
/// </summary>
/// <param name="Text">The run text.</param>
/// <param name="Bold">Whether bold is applied.</param>
/// <param name="Italic">Whether italic is applied.</param>
/// <param name="Underline">Whether underline is applied.</param>
/// <param name="Strikethrough">Whether strikethrough is applied.</param>
/// <param name="FontName">The resolved font family name.</param>
/// <param name="RenderedFontSize">
/// The font size to use for glyph layout, accounting for super/subscript scaling.
/// </param>
/// <param name="BaseFontSize">
/// The unscaled font size (i.e. before super/subscript factor), for baseline positioning.
/// </param>
/// <param name="FontColor">The resolved font color.</param>
/// <param name="VertAlign">Super, subscript, or none.</param>
public sealed record ResolvedCellTextRun(
    string Text,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    string FontName,
    double RenderedFontSize,
    double BaseFontSize,
    CellColor FontColor,
    CellTextRunVertAlign VertAlign);
