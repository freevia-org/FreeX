namespace FreeP.App.Host;

/// <summary>
/// FreeP's minimal PowerPoint-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX and FreeW, proving the ribbon library is app-neutral.
///
/// Tabs: Home, Insert (Wave 3 + 5B), Design (Wave 5B), Transitions, Animations, Slide Show (Wave 4C).
/// Wave 12A: Arrange group added to the Home tab (Group/Ungroup, z-order, Align).
/// </summary>
internal static class FreePRibbon
{
    public static RibbonDefinition Build()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("slides", "Slides", "S", 100, g =>
                {
                    // New Slide is the hero; the rest are compact stubs, mirroring PowerPoint's Slides group.
                    g.Large("freep.new-slide", "New Slide", RibbonCommandIconKind.Insert, "N");
                    g.Medium("freep.duplicate-slide", "Duplicate Slide", RibbonCommandIconKind.Copy, "D");
                    g.Medium("freep.delete-slide", "Delete Slide", RibbonCommandIconKind.Delete, "X");
                    g.Medium("freep.layout", "Layout", RibbonCommandIconKind.Grid, "L");
                });
                tab.Group("clipboard", "Clipboard", "C", 90, g =>
                {
                    g.Large("freep.paste", "Paste", RibbonCommandIconKind.Paste, "V");
                    g.Medium("freep.cut", "Cut", RibbonCommandIconKind.Cut, "T");
                    g.Medium("freep.copy", "Copy", RibbonCommandIconKind.Copy, "C");
                    // Wave 5B: Format Painter — copies formatting from first selected shape to rest of selection.
                    g.Medium("freep.format-painter", "Format Painter", RibbonCommandIconKind.FormatPainter, "F");
                });
                tab.Group("font", "Font", "F", 80, g =>
                {
                    g.ComboBox("freep.font-family", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Segoe UI", "Georgia", "Verdana" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.IconToggle("freep.bold", "Bold", RibbonCommandIconKind.Bold, "1");
                    g.IconToggle("freep.italic", "Italic", RibbonCommandIconKind.Italic, "2");
                    g.IconToggle("freep.underline", "Underline", RibbonCommandIconKind.Underline, "3");
                });
                // ── Wave 12A: Arrange group ───────────────────────────────────────────────
                tab.Group("arrange", "Arrange", "R", 70, g =>
                {
                    // Group / Ungroup
                    g.Large("freep.arrange.group",   "Group",   RibbonCommandIconKind.Group,   "G");
                    g.Medium("freep.arrange.ungroup", "Ungroup", RibbonCommandIconKind.Ungroup, "U");
                    g.Separator();
                    // Z-order
                    g.Medium("freep.arrange.bring-to-front",  "Bring to Front",  RibbonCommandIconKind.ArrowUp,   "F");
                    g.Medium("freep.arrange.bring-forward",   "Bring Forward",   RibbonCommandIconKind.ArrowUp,   "O");
                    g.Medium("freep.arrange.send-backward",   "Send Backward",   RibbonCommandIconKind.ArrowDown, "K");
                    g.Medium("freep.arrange.send-to-back",    "Send to Back",    RibbonCommandIconKind.ArrowDown, "B");
                    g.Separator();
                    // Align (six buttons — vertical reuse arrow/effects icons as fallback)
                    g.Medium("freep.arrange.align-left",      "Align Left",     RibbonCommandIconKind.AlignLeft,    "L");
                    g.Medium("freep.arrange.align-center-h",  "Center Horiz.",  RibbonCommandIconKind.AlignCenter,  "H");
                    g.Medium("freep.arrange.align-right",     "Align Right",    RibbonCommandIconKind.AlignRight,   "R");
                    g.Medium("freep.arrange.align-top",       "Align Top",      RibbonCommandIconKind.ArrowUp,      "T");
                    g.Medium("freep.arrange.align-middle",    "Center Vert.",   RibbonCommandIconKind.Align,        "M");
                    g.Medium("freep.arrange.align-bottom",    "Align Bottom",   RibbonCommandIconKind.ArrowDown,    "E");
                    g.Separator();
                    // Distribute
                    g.Medium("freep.arrange.distribute-h",    "Distribute Horiz.", RibbonCommandIconKind.AlignCenter, "D");
                    g.Medium("freep.arrange.distribute-v",    "Distribute Vert.",  RibbonCommandIconKind.Align,       "V");
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("text", "Text", "T", 100, g =>
                {
                    g.Large("freep.text-box", "Text Box", RibbonCommandIconKind.TextBox, "X");
                });
                // Wave 5B: Tables group — default 3×3; picker deferral noted.
                tab.Group("tables", "Tables", "A", 95, g =>
                {
                    g.Large("freep.insert-table-3x3", "Table", RibbonCommandIconKind.Table, "T");
                    g.Medium("freep.insert-table-2x2", "2×2", RibbonCommandIconKind.Table, "2");
                    g.Medium("freep.insert-table-4x4", "4×4", RibbonCommandIconKind.Table, "4");
                    // NOTE: interactive row/col picker (hover-grid) is deferred to a later wave.
                });
                // Wave 5B: Charts group (9B: "Edit Data" button added).
                tab.Group("charts", "Charts", "H", 93, g =>
                {
                    g.Medium("freep.insert-chart-column", "Column",    RibbonCommandIconKind.ChartColumn, "C");
                    g.Medium("freep.insert-chart-bar",    "Bar",       RibbonCommandIconKind.ChartColumn, "B");
                    g.Medium("freep.insert-chart-line",   "Line",      RibbonCommandIconKind.ChartLine,   "L");
                    g.Medium("freep.insert-chart-pie",    "Pie",       RibbonCommandIconKind.ChartPie,    "P");
                    // Wave 9B: Edit selected chart's data via grid dialog.
                    g.Medium("freep.chart.edit-data",     "Edit Data", RibbonCommandIconKind.ChartTitle,  "E");
                });
                // Wave 11A: Links group — Insert / Remove hyperlink.
                tab.Group("links", "Links", "L", 92, g =>
                {
                    g.Large("freep.insert-link", "Hyperlink", RibbonCommandIconKind.Link, "K");
                    g.Medium("freep.remove-link", "Remove Link", RibbonCommandIconKind.Delete, "R");
                });
                tab.Group("illustrations", "Illustrations", "I", 90, g =>
                {
                    g.Large("freep.picture", "Picture", RibbonCommandIconKind.Picture, "P");
                    g.Medium("freep.shape-rectangle", "Rectangle", RibbonCommandIconKind.Rectangle, "R");
                    g.Medium("freep.shape-ellipse", "Ellipse", RibbonCommandIconKind.Ellipse, "E");
                });
            })
            // ── Wave 5B: Design tab ───────────────────────────────────────────────────
            .Tab("design", "Design", "G", tab =>
            {
                // Themes group — one button per built-in theme.
                tab.Group("themes", "Themes", "T", 100, g =>
                {
                    g.Large("freep.theme.office",  "Office",  RibbonCommandIconKind.Color, "O");
                    g.Medium("freep.theme.berlin",  "Berlin",  RibbonCommandIconKind.Color, "B");
                    g.Medium("freep.theme.facet",   "Facet",   RibbonCommandIconKind.Color, "F");
                    g.Medium("freep.theme.ion",     "Ion",     RibbonCommandIconKind.Color, "I");
                    g.Medium("freep.theme.slice",   "Slice",   RibbonCommandIconKind.Color, "S");
                });
                // Customize group — slide size options.
                // Wave 10B: "Slide Size…" button opens the custom-size dialog.
                tab.Group("customize", "Customize", "Z", 90, g =>
                {
                    g.Large("freep.slide-size-16x9",  "Widescreen (16:9)", RibbonCommandIconKind.Page, "W");
                    g.Large("freep.slide-size-4x3",   "Standard (4:3)",   RibbonCommandIconKind.Page, "S");
                    g.Medium("freep.slide-size-custom", "Slide Size…",     RibbonCommandIconKind.Page, "C");
                });
            })
            // ── Wave 4C: Transitions tab ───────────────────────────────────────────────
            .Tab("transitions", "Transitions", "K", tab =>
            {
                // "Transition to This Slide" group — gallery of transition kinds via Medium buttons.
                tab.Group("transition-gallery", "Transition to This Slide", "T", 100, g =>
                {
                    g.Medium("freep.transition.none",     "None",     RibbonCommandIconKind.Clear,   "0");
                    g.Medium("freep.transition.fade",     "Fade",     RibbonCommandIconKind.Effects, "F");
                    g.Medium("freep.transition.push",     "Push",     RibbonCommandIconKind.ArrowRight, "U");
                    g.Medium("freep.transition.wipe",     "Wipe",     RibbonCommandIconKind.ArrowLeft,  "W");
                    g.Medium("freep.transition.split",    "Split",    RibbonCommandIconKind.ArrowLeftRight, "S");
                    g.Medium("freep.transition.cut",      "Cut",      RibbonCommandIconKind.Flash,   "C");
                    g.Medium("freep.transition.cover",    "Cover",    RibbonCommandIconKind.Page,    "V");
                    g.Medium("freep.transition.uncover",  "Uncover",  RibbonCommandIconKind.Expand,  "N");
                    g.Medium("freep.transition.blinds",   "Blinds",   RibbonCommandIconKind.View,    "B");
                    g.Medium("freep.transition.dissolve", "Dissolve", RibbonCommandIconKind.Color,   "D");
                    g.Medium("freep.transition.zoom",     "Zoom",     RibbonCommandIconKind.Zoom,    "Z");
                    g.Medium("freep.transition.wheel",    "Wheel",    RibbonCommandIconKind.Rotate,  "H");
                });

                // Timing group — duration, advance options, apply to all.
                tab.Group("transition-timing", "Timing", "I", 90, g =>
                {
                    g.ComboBox("freep.transition.duration", "Duration", c => c with
                    {
                        Items = new[] { "0.50s", "0.75s", "1.00s", "1.50s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.MediumToggle("freep.transition.advance-on-click", "On Mouse Click",
                        RibbonCommandIconKind.Next, "M");
                    g.ComboBox("freep.transition.advance-after", "After", c => c with
                    {
                        Items = new[] { "(none)", "1s", "2s", "3s", "5s", "10s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.transition.apply-all", "Apply To All",
                        RibbonCommandIconKind.Refresh, "A");
                });

                // Slide Show buttons live here for quick access from the Transitions tab.
                tab.Group("slideshow-from-transitions", "Slide Show", "L", 80, g =>
                {
                    g.Large("freep.slideshow.from-beginning",     "From Beginning",     RibbonCommandIconKind.Next,     "B");
                    g.Large("freep.slideshow.from-current-slide", "From Current Slide", RibbonCommandIconKind.Previous, "C");
                });
            })
            // ── Wave 4C: Animations tab ───────────────────────────────────────────────
            .Tab("animations", "Animations", "A", tab =>
            {
                // "Animation" group — Entrance, Emphasis, Exit effect buttons for selected shape.
                tab.Group("animation-effects", "Animation", "N", 100, g =>
                {
                    // Entrance effects
                    g.Medium("freep.anim.entrance.appear",    "Appear",     RibbonCommandIconKind.Flash,   "A");
                    g.Medium("freep.anim.entrance.fade",      "Fade In",    RibbonCommandIconKind.Effects, "F");
                    g.Medium("freep.anim.entrance.fly-in",    "Fly In",     RibbonCommandIconKind.ArrowUp, "Y");
                    g.Medium("freep.anim.entrance.wipe",      "Wipe",       RibbonCommandIconKind.ArrowRight, "W");
                    g.Medium("freep.anim.entrance.zoom",      "Zoom In",    RibbonCommandIconKind.Zoom,    "Z");
                    g.Medium("freep.anim.entrance.split",     "Split",      RibbonCommandIconKind.ArrowLeftRight, "S");
                    g.Separator();
                    // Emphasis effects
                    g.Medium("freep.anim.emphasis.pulse",      "Pulse",      RibbonCommandIconKind.Flash,   "P");
                    g.Medium("freep.anim.emphasis.spin",       "Spin",       RibbonCommandIconKind.Rotate,  "I");
                    g.Medium("freep.anim.emphasis.grow-shrink","Grow/Shrink", RibbonCommandIconKind.Scale,   "G");
                    g.Separator();
                    // Exit effects
                    g.Medium("freep.anim.exit.disappear",  "Disappear", RibbonCommandIconKind.Delete,    "D");
                    g.Medium("freep.anim.exit.fade-out",   "Fade Out",  RibbonCommandIconKind.Effects,   "O");
                    g.Medium("freep.anim.exit.fly-out",    "Fly Out",   RibbonCommandIconKind.ArrowDown, "X");
                    g.Separator();
                    // Remove all animations from selected shape
                    g.Medium("freep.anim.none", "No Animation", RibbonCommandIconKind.Clear, "E");
                });

                // Timing group — trigger, duration, delay, reorder.
                tab.Group("animation-timing", "Timing", "T", 90, g =>
                {
                    g.ComboBox("freep.anim.trigger", "Start", c => c with
                    {
                        Items = new[] { "On Click", "With Previous", "After Previous" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                        Width = 130
                    });
                    g.ComboBox("freep.anim.duration", "Duration", c => c with
                    {
                        Items = new[] { "0.25s", "0.50s", "1.00s", "1.50s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.ComboBox("freep.anim.delay", "Delay", c => c with
                    {
                        Items = new[] { "0s", "0.25s", "0.50s", "1.00s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.anim.move-earlier", "Move Earlier", RibbonCommandIconKind.Previous, "U");
                    g.Medium("freep.anim.move-later",   "Move Later",   RibbonCommandIconKind.Next,     "L");
                });

                // Animation Pane toggle stub.
                tab.Group("animation-pane", "Advanced Animation", "V", 80, g =>
                {
                    g.MediumToggle("freep.anim.pane", "Animation Pane", RibbonCommandIconKind.List, "P");
                });
            })
            .Build();
    }
}
