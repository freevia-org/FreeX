using Free.Shared.Ribbon.Wpf;

namespace FreeP.App.Host;

/// <summary>
/// Maps FreeP's <c>freep.*</c> ribbon command ids to shared <see cref="RibbonCommandIconKind"/> glyphs, so
/// the shared WPF renderer (ribbon, BackstageFrame rail, QAT) draws a meaningful vector icon per control.
/// Ids without a dedicated mapping fall back to the generic glyph. Mirrors FreeWRibbonIcons, kept minimal for
/// the scaffold's small ribbon.
///
/// Wave 4C additions: Transitions, Animations, and Slide Show command ids.
/// Wave 5B additions: Format Painter, Insert Tables/Charts, Design tab (Themes + Slide Size).
/// </summary>
internal static class FreePRibbonIcons
{
    /// <summary>Installs the FreeP command-id → glyph resolver on the shared icon factory.</summary>
    public static void Install() => RibbonIconFactory.CommandIconKindResolver = Resolve;

    public static RibbonCommandIconKind? Resolve(string commandId) =>
        Map.TryGetValue(commandId, out var kind) ? kind : null;

    private static readonly IReadOnlyDictionary<string, RibbonCommandIconKind> Map =
        new Dictionary<string, RibbonCommandIconKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Slides
            ["freep.new-slide"] = RibbonCommandIconKind.Insert,
            ["freep.duplicate-slide"] = RibbonCommandIconKind.Copy,
            ["freep.delete-slide"] = RibbonCommandIconKind.Delete,
            ["freep.layout"] = RibbonCommandIconKind.Grid,

            // Clipboard
            ["freep.paste"] = RibbonCommandIconKind.Paste,
            ["freep.cut"] = RibbonCommandIconKind.Cut,
            ["freep.copy"] = RibbonCommandIconKind.Copy,

            // Font
            ["freep.font-family"] = RibbonCommandIconKind.Font,
            ["freep.bold"] = RibbonCommandIconKind.Bold,
            ["freep.italic"] = RibbonCommandIconKind.Italic,
            ["freep.underline"] = RibbonCommandIconKind.Underline,
            ["freep.superscript"] = RibbonCommandIconKind.Superscript,
            ["freep.subscript"] = RibbonCommandIconKind.Subscript,

            // Clipboard (Wave 5B)
            ["freep.format-painter"] = RibbonCommandIconKind.FormatPainter,

            // Insert
            ["freep.text-box"] = RibbonCommandIconKind.TextBox,
            ["freep.picture"] = RibbonCommandIconKind.Picture,
            ["freep.video"] = RibbonCommandIconKind.Picture,
            ["freep.audio"] = RibbonCommandIconKind.Picture,
            ["freep.shape-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freep.shape-ellipse"] = RibbonCommandIconKind.Ellipse,
            ["freep.shape-triangle"] = RibbonCommandIconKind.Triangle,
            ["freep.shape-diamond"] = RibbonCommandIconKind.Diamond,
            ["freep.shape-hexagon"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-right-arrow"] = RibbonCommandIconKind.ArrowRight,
            ["freep.shape-star5"] = RibbonCommandIconKind.Star,
            ["freep.insert-connector"] = RibbonCommandIconKind.Line,
            ["freep.insert-elbow-connector"] = RibbonCommandIconKind.Line,
            ["freep.insert-curved-connector"] = RibbonCommandIconKind.Line,

            // ── Wave 5B: Insert — Tables ──────────────────────────────────────────────
            ["freep.insert-table-3x3"] = RibbonCommandIconKind.Table,
            ["freep.insert-table-2x2"] = RibbonCommandIconKind.Table,
            ["freep.insert-table-4x4"] = RibbonCommandIconKind.Table,

            // ── Wave 5B: Insert — Charts ──────────────────────────────────────────────
            ["freep.insert-chart-column"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-bar"]    = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-line"]   = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-pie"]    = RibbonCommandIconKind.ChartPie,
            ["freep.insert-chart-column-stacked"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-column-stacked-100"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-bar-stacked"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-bar-stacked-100"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-line-markers"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-area"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-area-stacked"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-scatter"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-doughnut"] = RibbonCommandIconKind.ChartPie,
            ["freep.insert-chart-radar"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-bubble"] = RibbonCommandIconKind.ChartPie,
            ["freep.insert-chart-stock"] = RibbonCommandIconKind.ChartLine,
            ["freep.insert-chart-surface"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-surface-3d"] = RibbonCommandIconKind.ChartColumn,

            // ── Wave 5B: Design tab — Themes ─────────────────────────────────────────
            ["freep.theme.office"] = RibbonCommandIconKind.Color,
            ["freep.theme.berlin"] = RibbonCommandIconKind.Color,
            ["freep.theme.facet"]  = RibbonCommandIconKind.Color,
            ["freep.theme.ion"]    = RibbonCommandIconKind.Color,
            ["freep.theme.slice"]  = RibbonCommandIconKind.Color,

            // ── Wave 5B: Design tab — Slide Size ─────────────────────────────────────
            ["freep.slide-size-16x9"] = RibbonCommandIconKind.Page,
            ["freep.slide-size-4x3"]  = RibbonCommandIconKind.Page,

            // ── Wave 4C: Transition gallery ───────────────────────────────────────────
            ["freep.transition.none"]     = RibbonCommandIconKind.Clear,
            ["freep.transition.fade"]     = RibbonCommandIconKind.Effects,
            ["freep.transition.push"]     = RibbonCommandIconKind.ArrowRight,
            ["freep.transition.wipe"]     = RibbonCommandIconKind.ArrowLeft,
            ["freep.transition.split"]    = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.transition.box"]      = RibbonCommandIconKind.Rectangle,
            ["freep.transition.doors"]    = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.transition.reveal"]  = RibbonCommandIconKind.Expand,
            ["freep.transition.flash"]   = RibbonCommandIconKind.Flash,
            ["freep.transition.morph"]   = RibbonCommandIconKind.Effects,
            ["freep.transition.cut"]      = RibbonCommandIconKind.Flash,
            ["freep.transition.cover"]    = RibbonCommandIconKind.Page,
            ["freep.transition.uncover"]  = RibbonCommandIconKind.Expand,
            ["freep.transition.blinds"]   = RibbonCommandIconKind.View,
            ["freep.transition.comb"]     = RibbonCommandIconKind.Grid,
            ["freep.transition.random-bars"] = RibbonCommandIconKind.Grid,
            ["freep.transition.strips"]      = RibbonCommandIconKind.TextColumns,
            ["freep.transition.wheel-reverse"] = RibbonCommandIconKind.Rotate,
            ["freep.transition.gallery"] = RibbonCommandIconKind.Grid,
            ["freep.transition.conveyor"] = RibbonCommandIconKind.ArrowRight,
            ["freep.transition.pan"]      = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.transition.window"]   = RibbonCommandIconKind.Window,
            ["freep.transition.dissolve"] = RibbonCommandIconKind.Color,
            ["freep.transition.zoom"]     = RibbonCommandIconKind.Zoom,
            ["freep.transition.wheel"]    = RibbonCommandIconKind.Rotate,
            ["freep.transition.more"]     = RibbonCommandIconKind.Effects,

            // Wave 4C: Transition timing
            ["freep.transition.duration"]         = RibbonCommandIconKind.History,
            ["freep.transition.advance-on-click"] = RibbonCommandIconKind.Next,
            ["freep.transition.advance-after"]    = RibbonCommandIconKind.History,
            ["freep.transition.apply-all"]        = RibbonCommandIconKind.Refresh,

            // Wave 4C: Slide Show buttons
            ["freep.slideshow.from-beginning"]     = RibbonCommandIconKind.Next,
            ["freep.slideshow.from-current-slide"] = RibbonCommandIconKind.Previous,
            ["freep.slideshow.custom-shows"]        = RibbonCommandIconKind.List,

            // Wave 4C: Animation entrance effects
            ["freep.anim.entrance.appear"] = RibbonCommandIconKind.Flash,
            ["freep.anim.entrance.fade"]   = RibbonCommandIconKind.Effects,
            ["freep.anim.entrance.fly-in"] = RibbonCommandIconKind.ArrowUp,
            ["freep.anim.entrance.wipe"]   = RibbonCommandIconKind.ArrowRight,
            ["freep.anim.entrance.zoom"]   = RibbonCommandIconKind.Zoom,
            ["freep.anim.entrance.split"]  = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.anim.entrance.blinds"] = RibbonCommandIconKind.Grid,

            // Wave 4C: Animation emphasis effects
            ["freep.anim.emphasis.pulse"]       = RibbonCommandIconKind.Flash,
            ["freep.anim.emphasis.spin"]        = RibbonCommandIconKind.Rotate,
            ["freep.anim.emphasis.grow-shrink"] = RibbonCommandIconKind.Scale,

            // Wave 4C: Animation exit effects
            ["freep.anim.exit.disappear"] = RibbonCommandIconKind.Delete,
            ["freep.anim.exit.fade-out"]  = RibbonCommandIconKind.Effects,
            ["freep.anim.exit.fly-out"]   = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.exit.wipe"]       = RibbonCommandIconKind.ArrowRight,
            ["freep.anim.exit.split"]      = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.anim.exit.zoom-out"]   = RibbonCommandIconKind.Zoom,
            ["freep.anim.exit.blinds"]     = RibbonCommandIconKind.Grid,

            // Wave 4C: Animation none / timing / pane
            ["freep.anim.none"]         = RibbonCommandIconKind.Clear,
            ["freep.anim.trigger"]      = RibbonCommandIconKind.Next,
            ["freep.anim.duration"]     = RibbonCommandIconKind.History,
            ["freep.anim.delay"]        = RibbonCommandIconKind.History,
            ["freep.anim.move-earlier"] = RibbonCommandIconKind.Previous,
            ["freep.anim.move-later"]   = RibbonCommandIconKind.Next,
            ["freep.anim.pane"]         = RibbonCommandIconKind.List,
        };
}
