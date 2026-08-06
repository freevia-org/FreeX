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
            ["freep.table-cell-fill"] = RibbonCommandIconKind.Fill,
            ["freep.table-cell-anchor"] = RibbonCommandIconKind.Align,
            ["freep.table-cell-border"] = RibbonCommandIconKind.Border,
            ["freep.table.first-row"] = RibbonCommandIconKind.Table,
            ["freep.table.last-row"] = RibbonCommandIconKind.Table,
            ["freep.table.first-column"] = RibbonCommandIconKind.Table,
            ["freep.table.last-column"] = RibbonCommandIconKind.Table,
            ["freep.table.banded-rows"] = RibbonCommandIconKind.Table,
            ["freep.table.banded-columns"] = RibbonCommandIconKind.Table,
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
            ["freep.shape-rounded-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freep.shape-ellipse"] = RibbonCommandIconKind.Ellipse,
            ["freep.shape-triangle"] = RibbonCommandIconKind.Triangle,
            ["freep.shape-diamond"] = RibbonCommandIconKind.Diamond,
            ["freep.shape-hexagon"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-parallelogram"] = RibbonCommandIconKind.Diamond,
            ["freep.shape-trapezoid"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-left-arrow"] = RibbonCommandIconKind.ArrowLeft,
            ["freep.shape-right-arrow"] = RibbonCommandIconKind.ArrowRight,
            ["freep.shape-up-arrow"] = RibbonCommandIconKind.ArrowUp,
            ["freep.shape-down-arrow"] = RibbonCommandIconKind.ArrowDown,
            ["freep.shape-star5"] = RibbonCommandIconKind.Star,
            ["freep.shape-cross"] = RibbonCommandIconKind.Cross,
            ["freep.shape-plus-sign"] = RibbonCommandIconKind.PlusSign,
            ["freep.shape-pentagon"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-octagon"] = RibbonCommandIconKind.Octagon,
            ["freep.shape-left-right-arrow"] = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.shape-up-down-arrow"] = RibbonCommandIconKind.ArrowUpDown,
            ["freep.shape-star8"] = RibbonCommandIconKind.Star,
            ["freep.shape-chevron"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-home-plate"] = RibbonCommandIconKind.Pentagon,
            ["freep.shape-right-triangle"] = RibbonCommandIconKind.Triangle,
            ["freep.shape-minus-sign"] = RibbonCommandIconKind.MinusSign,
            ["freep.shape-multiply-sign"] = RibbonCommandIconKind.MultiplySign,
            ["freep.shape-divide-sign"] = RibbonCommandIconKind.DivideSign,
            ["freep.shape-equal-sign"] = RibbonCommandIconKind.EqualSign,
            ["freep.shape-not-equal-sign"] = RibbonCommandIconKind.NotEqualSign,
            ["freep.shape-wave"] = RibbonCommandIconKind.Wave,
            ["freep.shape-rectangular-callout"] = RibbonCommandIconKind.Callout,
            ["freep.shape-rounded-rectangular-callout"] = RibbonCommandIconKind.Callout,
            ["freep.shape-oval-callout"] = RibbonCommandIconKind.Callout,
            ["freep.shape-explosion"] = RibbonCommandIconKind.Explosion,
            ["freep.shape-ribbon"] = RibbonCommandIconKind.RibbonShape,
            ["freep.shape-flowchart-process"] = RibbonCommandIconKind.FlowchartProcess,
            ["freep.shape-flowchart-decision"] = RibbonCommandIconKind.FlowchartDecision,
            ["freep.shape-flowchart-data"] = RibbonCommandIconKind.FlowchartData,
            ["freep.shape-flowchart-predefined-process"] = RibbonCommandIconKind.FlowchartProcess,
            ["freep.shape-flowchart-document"] = RibbonCommandIconKind.FlowchartDocument,
            ["freep.shape-flowchart-terminator"] = RibbonCommandIconKind.FlowchartTerminator,
            ["freep.shape-line-callout"] = RibbonCommandIconKind.LineCallout,
            ["freep.shape-cylinder"] = RibbonCommandIconKind.Rectangle,
            ["freep.shape-chord"] = RibbonCommandIconKind.Diamond,
            ["freep.shape-heart"] = RibbonCommandIconKind.RibbonShape,
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
            ["freep.insert-chart-of-pie"] = RibbonCommandIconKind.ChartPie,
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
            ["freep.insert-chart-funnel"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-waterfall"] = RibbonCommandIconKind.ChartColumn,
            ["freep.insert-chart-combo"] = RibbonCommandIconKind.ChartColumn,

            // ── Wave 5B: Design tab — Themes ─────────────────────────────────────────
            ["freep.theme.office"] = RibbonCommandIconKind.Color,
            ["freep.theme.berlin"] = RibbonCommandIconKind.Color,
            ["freep.theme.facet"]  = RibbonCommandIconKind.Color,
            ["freep.theme.ion"]    = RibbonCommandIconKind.Color,
            ["freep.theme.slice"]  = RibbonCommandIconKind.Color,

            // ── Wave 5B: Design tab — Slide Size ─────────────────────────────────────
            ["freep.slide-size-16x9"] = RibbonCommandIconKind.Page,
            ["freep.slide-size-4x3"]  = RibbonCommandIconKind.Page,
            ["freep.background-white"] = RibbonCommandIconKind.Fill,
            ["freep.background-black"] = RibbonCommandIconKind.Fill,
            ["freep.background-blue"]  = RibbonCommandIconKind.Fill,
            ["freep.background-reset"] = RibbonCommandIconKind.Clear,

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
            ["freep.transition.sound"]             = RibbonCommandIconKind.Picture,
            ["freep.transition.sound-none"]        = RibbonCommandIconKind.Clear,
            ["freep.transition.sound-loop"]        = RibbonCommandIconKind.Refresh,

            // Wave 4C: Slide Show buttons
            ["freep.slideshow.from-beginning"]     = RibbonCommandIconKind.Next,
            ["freep.slideshow.from-current-slide"] = RibbonCommandIconKind.Previous,
            ["freep.slideshow.setup"]             = RibbonCommandIconKind.More,
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
            ["freep.anim.emphasis.teeter"]       = RibbonCommandIconKind.Rotate,
            ["freep.anim.emphasis.blink"]        = RibbonCommandIconKind.Flash,
            ["freep.anim.emphasis.flash-bulb"]    = RibbonCommandIconKind.Flash,
            ["freep.anim.emphasis.flicker"]       = RibbonCommandIconKind.Flash,
            ["freep.anim.emphasis.color-pulse"]  = RibbonCommandIconKind.Color,
            ["freep.anim.emphasis.change-color"] = RibbonCommandIconKind.Color,
            ["freep.anim.emphasis.change-fill-color"] = RibbonCommandIconKind.Color,
            ["freep.anim.emphasis.change-font-color"] = RibbonCommandIconKind.FontColor,
            ["freep.anim.emphasis.change-font-size"] = RibbonCommandIconKind.Size,
            ["freep.anim.emphasis.change-line-color"] = RibbonCommandIconKind.Color,
            ["freep.anim.emphasis.change-font-style"] = RibbonCommandIconKind.Bold,
            ["freep.anim.emphasis.grow-with-color"] = RibbonCommandIconKind.Color,
            ["freep.anim.emphasis.wave"]         = RibbonCommandIconKind.Effects,
            ["freep.anim.emphasis.shimmer"]      = RibbonCommandIconKind.Effects,
            ["freep.anim.emphasis.bold"]         = RibbonCommandIconKind.Bold,
            ["freep.anim.emphasis.underline"]   = RibbonCommandIconKind.Underline,

            // Wave 4C: Animation exit effects
            ["freep.anim.exit.disappear"] = RibbonCommandIconKind.Delete,
            ["freep.anim.exit.fade-out"]  = RibbonCommandIconKind.Effects,
            ["freep.anim.exit.fly-out"]   = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.exit.wipe"]       = RibbonCommandIconKind.ArrowRight,
            ["freep.anim.exit.split"]      = RibbonCommandIconKind.ArrowLeftRight,
            ["freep.anim.exit.zoom-out"]   = RibbonCommandIconKind.Zoom,
            ["freep.anim.exit.blinds"]     = RibbonCommandIconKind.Grid,

            // Motion-path authoring
            ["freep.anim.motion.right"]     = RibbonCommandIconKind.ArrowRight,
            ["freep.anim.motion.left"]      = RibbonCommandIconKind.ArrowLeft,
            ["freep.anim.motion.up"]        = RibbonCommandIconKind.ArrowUp,
            ["freep.anim.motion.down"]      = RibbonCommandIconKind.ArrowDown,
            ["freep.anim.motion.arc-right"] = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.arc-left"]  = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.arc-up"]    = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.arc-down"]  = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.circle"]    = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.loop"]      = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.s"]         = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.figure-eight"] = RibbonCommandIconKind.Effects,
            ["freep.anim.motion.reverse"]  = RibbonCommandIconKind.Rotate,

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
