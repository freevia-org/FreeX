using Avalonia.Controls.Documents;
using Avalonia.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds an <see cref="InlineCollection"/> from <see cref="ResolvedCellTextRun"/> entries for
/// Avalonia's <see cref="Avalonia.Controls.TextBlock"/>, enabling per-run bold/italic/color/size
/// and super/subscript baseline alignment — the Avalonia counterpart of WPF's
/// <c>ApplyRichRunFormatting</c> in <c>GridView.Rendering.CellStyles.cs</c>.
/// </summary>
/// <remarks>
/// Design decisions:
/// <list type="bullet">
///   <item>Each <see cref="ResolvedCellTextRun"/> maps to exactly one <see cref="Run"/>.</item>
///   <item>
///     Superscript uses <see cref="BaselineAlignment.Superscript"/>;
///     subscript uses <see cref="BaselineAlignment.Subscript"/>.
///     Both rely on the planner's <see cref="ResolvedCellTextRun.RenderedFontSize"/> (≈67% of
///     base size) so glyph geometry matches Excel's convention.
///   </item>
///   <item>
///     All Avalonia brush creation is deferred to the caller via a simple RGB→<see cref="IBrush"/>
///     factory delegate so this class stays testable without a running application.
///   </item>
/// </list>
/// </remarks>
internal static class CellRichTextInlinesBuilder
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="runs"/> contains at least one entry,
    /// i.e. the Inlines path should be taken rather than the plain-text path.
    /// </summary>
    public static bool HasRuns(IReadOnlyList<ResolvedCellTextRun>? runs) =>
        runs is { Count: > 0 };

    /// <summary>
    /// Builds one <see cref="Run"/> per entry in <paramref name="runs"/> and adds them to
    /// <paramref name="inlines"/>.
    /// </summary>
    /// <param name="runs">Resolved runs from <see cref="CellRichRunLayoutPlanner.Resolve"/>.</param>
    /// <param name="inlines">Target collection on the <see cref="Avalonia.Controls.TextBlock"/>.</param>
    /// <param name="brushFactory">
    /// Maps an RGB <see cref="CellColor"/> to a cached <see cref="IBrush"/>.
    /// </param>
    public static void Build(
        IReadOnlyList<ResolvedCellTextRun> runs,
        InlineCollection inlines,
        Func<CellColor, IBrush> brushFactory)
    {
        var segments = CellTextMaterializationPlanner.MaterializeRuns(
            string.Empty,
            runs,
            CellRichTextMaterializationMode.NativeRunText);
        Build(segments, inlines, brushFactory);
    }

    public static void Build(
        IReadOnlyList<CellTextRunMaterializationSegment> segments,
        InlineCollection inlines,
        Func<CellColor, IBrush> brushFactory)
    {
        foreach (var segment in segments)
        {
            var run = segment.Run;
            var inline = new Run
            {
                Text              = segment.Text,
                FontSize          = run.RenderedFontSize,
                FontWeight        = run.Bold    ? FontWeight.Bold   : FontWeight.Normal,
                FontStyle         = run.Italic  ? FontStyle.Italic  : FontStyle.Normal,
                Foreground        = brushFactory(run.FontColor),
                TextDecorations   = BuildTextDecorations(run),
                BaselineAlignment = MapVertAlign(run.VertAlign),
                FontFamily        = new FontFamily(run.FontName),
            };
            inlines.Add(inline);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static TextDecorationCollection? BuildTextDecorations(ResolvedCellTextRun run)
    {
        if (!run.Underline && !run.Strikethrough) return null;

        var list = new TextDecorationCollection();
        if (run.Underline)
            list.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (run.Strikethrough)
            list.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        return list;
    }

    private static BaselineAlignment MapVertAlign(CellTextRunVertAlign vertAlign) =>
        vertAlign switch
        {
            CellTextRunVertAlign.Superscript => BaselineAlignment.Superscript,
            CellTextRunVertAlign.Subscript   => BaselineAlignment.Subscript,
            _                                => BaselineAlignment.Baseline,
        };
}
