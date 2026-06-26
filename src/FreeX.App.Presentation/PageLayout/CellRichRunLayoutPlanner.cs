using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Resolves per-run rich-text properties against a cell's base <see cref="CellStyle"/>,
/// producing a flat list of <see cref="ResolvedCellTextRun"/> that renderers (WPF and Avalonia)
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
///     The workbook theme is NOT required here: by the time a cell is rendered, the cell
///     style already has a concrete resolved <see cref="CellColor"/> (the IO layer resolves
///     theme colors on load).  Run colors are always stored as resolved RGB values.
///   </item>
/// </list>
/// </remarks>
public static class CellRichRunLayoutPlanner
{
    private const double SuperSubSizeFactor = 0.67;

    /// <summary>
    /// Resolves the rich-text runs for a cell, coalescing null per-run properties with the
    /// cell style defaults.
    /// </summary>
    /// <param name="runs">Raw runs from <see cref="Sheet.RichTextRuns"/>.</param>
    /// <param name="cellStyle">The resolved cell style (base font, color, …).</param>
    /// <returns>
    /// A list of fully-resolved runs ready for the renderer.  Never null; an empty list means
    /// no rich runs (caller falls back to plain-text rendering).
    /// </returns>
    public static IReadOnlyList<ResolvedCellTextRun> Resolve(
        IReadOnlyList<CellTextRun>? runs,
        CellStyle cellStyle)
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
            var color        = run.FontColor ?? cellStyle.FontColor;
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
/// Consumed by WPF and Avalonia renderers (Waves 2 and 3).
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
