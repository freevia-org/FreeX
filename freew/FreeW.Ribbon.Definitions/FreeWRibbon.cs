using System.Linq;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX, proving the ribbon library is app-neutral.
/// </summary>
public static class FreeWRibbon
{
    public static RibbonDefinition Build(FreeWRibbonCapabilities? capabilities = null)
    {
        capabilities ??= FreeWRibbonCapabilities.Wpf;
        if (capabilities.UseAvaloniaBackedSurface)
            return FreeWAvaloniaRibbonDefinition.Build(capabilities);

        var definition = new RibbonDefinitionBuilder()
            .AddHomeTab(capabilities)
            .AddInsertTab(capabilities)
            .AddReferencesTab(capabilities)
            .AddLayoutTab(capabilities)
            .AddDesignTab(capabilities)
            .AddViewTab(capabilities)
            .AddHelpTab(capabilities)
            .AddMailingsTab(capabilities)
            .AddReviewTab(capabilities)
            .AddDeveloperTab(capabilities)
            // ── Contextual tabs (Word "Tools" tabs) ───────────────────────────────────────────────────
            // Declared individually here, but shown/hidden by the shared RibbonContextualTabController only
            // while their selection context is active: "picture" when an image is selected, "table" when the
            // caret is in a table. Contextual tabs reuse the same command ids but group them by active selection,
            // exactly like Word's Picture Format / Table Design tabs.
            // ── Drawing Format contextual tab — Shape Tools (shown when a shape/text-box/WordArt is selected) ──
            .ContextualTab("drawing-format", "Drawing Format",
                new RibbonTabContext("drawing", "Drawing Tools", RibbonContextColor.Purple), tab =>
            {
                tab.Group("drawing-insert", "Insert Shapes", "I", 110, g =>
                {
                    g.Medium("freew.shape-change", "Change Shape", RibbonCommandIconKind.Shapes, menu: m =>
                    {
                        m.Item("freew.shape-change-rectangle", "Rectangle", "R");
                        m.Item("freew.shape-change-rounded", "Rounded Rectangle", "U");
                        m.Item("freew.shape-change-ellipse", "Oval", "O");
                    });
                    // Edit Shape > Edit Points (W25): converts preset to freeform custom geometry.
                    g.Medium("freew.shape-edit-shape", "Edit Shape", RibbonCommandIconKind.Generic, menu: m =>
                    {
                        m.Item("freew.shape-convert-freeform", "Convert to Freeform", "F");
                        m.Item("freew.shape-edit-points",      "Edit Points",          "E");
                    });
                });
                tab.Group("drawing-styles", "Shape Styles", "H", 100, g =>
                {
                    // Shape Styles gallery — 40 theme-coloured presets (injected as live gallery at runtime)
                    g.Medium("freew.shape-styles-gallery", "Shape Styles", RibbonCommandIconKind.Styles);

                    g.Medium("freew.shape-fill", "Shape Fill", RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill, menu: m =>
                    {
                        m.Item("freew.shape-fill-no-fill", "No Fill", "N");
                        m.Separator();
                        m.Item("freew.shape-fill-gradient-blue", "Gradient Blue", "G");
                        m.Item("freew.shape-fill-gradient-orange", "Gradient Orange", "O");
                        m.Item("freew.shape-fill-pattern-diag", "Pattern: Diagonal Cross", "D");
                    });
                    g.Medium("freew.shape-outline", "Shape Outline", RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border, menu: m =>
                    {
                        m.Item("freew.shape-outline-no-outline", "No Outline", "N");
                        m.Item("freew.shape-outline-solid", "Solid", "S");
                        m.Item("freew.shape-outline-dash", "Dash", "D");
                        m.Item("freew.shape-outline-dot", "Dot", "O");
                    });
                    // Shape Effects submenu (W24)
                    g.Medium("freew.shape-effects", "Shape Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.shape-effects-none", "No Effects", "N");
                        m.Separator();
                        m.Item("freew.shape-effect-shadow", "Shadow", "S");
                        m.Item("freew.shape-effect-glow", "Glow", "G");
                        m.Item("freew.shape-effect-soft-edge", "Soft Edges", "E");
                        m.Item("freew.shape-effect-reflection", "Reflection", "R");
                        m.Item("freew.shape-effect-bevel", "Bevel", "B");
                    });
                });
                tab.Group("drawing-text", "Text", "X", 90, g =>
                {
                    g.Medium("freew.shape-text-direction", "Text Direction", RibbonCommandIconKind.TextBox, menu: m =>
                    {
                        m.Item("freew.shape-text-horizontal", "Horizontal", "H");
                        m.Item("freew.shape-text-rotate90", "Rotate 90°", "R");
                        m.Item("freew.shape-text-rotate270", "Rotate 270°", "T");
                    });
                });
                tab.Group("drawing-wordart", "WordArt Styles", "W", 85, g =>
                {
                    g.Medium("freew.wordart-style", "WordArt Style", RibbonCommandIconKind.WordArt, menu: m =>
                    {
                        // Original four
                        m.Item("freew.wordart-style-fill-blue", "Fill: Blue", "B");
                        m.Item("freew.wordart-style-gradient", "Gradient Fill", "G");
                        m.Item("freew.wordart-style-outline", "Outline", "O");
                        m.Item("freew.wordart-style-shadow", "Shadow", "S");
                        m.Separator();
                        // Extended eleven (W24)
                        m.Item("freew.wordart-style-fill-gold", "Fill: Gold", "D");
                        m.Item("freew.wordart-style-fill-white", "Fill: White", "W");
                        m.Item("freew.wordart-style-grad-multi", "Gradient: Multicolour", "M");
                        m.Item("freew.wordart-style-chrome-one", "Outline Only", "L");
                        m.Item("freew.wordart-style-chrome-two", "White + Outline", "H");
                        m.Item("freew.wordart-style-shadow-orange", "Shadow: Orange", "A");
                        m.Item("freew.wordart-style-glow-blue", "Glow: Blue", "U");
                        m.Item("freew.wordart-style-glow-gold", "Glow: Gold", "I");
                        m.Item("freew.wordart-style-reflection", "Reflection", "F");
                        m.Item("freew.wordart-style-bevel", "Bevel", "V");
                        m.Item("freew.wordart-style-pattern", "Pattern Fill", "P");
                    });
                    // Text Effects > Transform (W24 — warp presets)
                    g.Medium("freew.wordart-transform", "Text Effects: Transform", RibbonCommandIconKind.WordArt, menu: m =>
                    {
                        m.Item("freew.wordart-warp-none", "No Transform", "N");
                        m.Separator();
                        m.Item("freew.wordart-warp-arch-up", "Arch Up", "A");
                        m.Item("freew.wordart-warp-arch-down", "Arch Down", "D");
                        m.Item("freew.wordart-warp-circle", "Circle", "C");
                        m.Item("freew.wordart-warp-wave1", "Wave 1", "W");
                        m.Item("freew.wordart-warp-wave2", "Wave 2", "V");
                        m.Item("freew.wordart-warp-inflate", "Inflate", "I");
                        m.Item("freew.wordart-warp-deflate", "Deflate", "E");
                        m.Item("freew.wordart-warp-chevron-up", "Chevron Up", "U");
                        m.Item("freew.wordart-warp-chevron-down", "Chevron Down", "H");
                        m.Item("freew.wordart-warp-fade-right", "Fade Right", "F");
                        m.Item("freew.wordart-warp-fade-left", "Fade Left", "L");
                        m.Item("freew.wordart-warp-slant-up", "Slant Up", "S");
                        m.Item("freew.wordart-warp-slant-down", "Slant Down", "T");
                    });
                });
                tab.Group("drawing-arrange", "Arrange", "A", 80, g =>
                {
                    g.Medium("freew.shape-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: m =>
                    {
                        m.Item("freew.shape-wrap-inline",     "In Line with Text", "I");
                        m.Item("freew.shape-wrap-square",     "Square",            "S");
                        m.Item("freew.shape-wrap-tight",      "Tight",             "T");
                        m.Item("freew.shape-wrap-top-bottom", "Top and Bottom",    "B");
                        m.Item("freew.shape-wrap-behind",     "Behind Text",       "H");
                        m.Item("freew.shape-wrap-front",      "In Front of Text",  "F");
                    });
                    g.Medium("freew.shape-position", "Position", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90",   "Rotate Right 90°",  "R");
                        m.Item("freew.shape-rotate-left90",    "Rotate Left 90°",   "L");
                        m.Item("freew.shape-flip-vertical",    "Flip Vertical",     "V");
                        m.Item("freew.shape-flip-horizontal",  "Flip Horizontal",   "H");
                    });
                    g.Medium("freew.shape-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.shape-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.shape-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Medium("freew.shape-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.shape-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                    // Shape-specific ids keep Drawing Format dispatch separate from Picture Format.
                    g.Medium("freew.shape-bring-to-front",  "Bring to Front",  RibbonCommandIconKind.BringToFront);
                    g.Medium("freew.shape-send-to-back",    "Send to Back",    RibbonCommandIconKind.SendToBack);
                    g.Medium("freew.shape-bring-forward",   "Bring Forward",   RibbonCommandIconKind.BringForward);
                    g.Medium("freew.shape-send-backward",   "Send Backward",   RibbonCommandIconKind.SendBackward);
                    // Group / Ungroup (Phase 4).
                    g.Medium("freew.object-group", "Group", RibbonCommandIconKind.Generic);
                    g.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                });
                tab.Group("drawing-size", "Size", "S", 70, g =>
                {
                    g.Medium("freew.shape-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.shape-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                });
            })
            .ContextualTab("picture-format", "Picture Format",
                new RibbonTabContext("picture", "Picture Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("picture-arrange", "Arrange", "A", 100, g =>
                {
                    g.Medium("freew.image-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: m =>
                    {
                        m.Item("freew.image-wrap-inline", "In Line with Text", "I");
                        m.Item("freew.image-wrap-square", "Square", "S");
                        m.Item("freew.image-wrap-tight", "Tight", "T");
                        m.Item("freew.image-wrap-top-bottom", "Top and Bottom", "B");
                        m.Item("freew.image-wrap-behind", "Behind Text", "H");
                        m.Item("freew.image-wrap-front", "In Front of Text", "F");
                    });
                    g.Medium("freew.image-position", "Position", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.image-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.image-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.image-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.image-flip-horizontal","Flip Horizontal",  "H");
                    });
                    g.Medium("freew.image-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.image-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.image-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Medium("freew.image-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.image-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                    // Z-order commands for floating images (Phase 2).
                    g.Medium("freew.image-bring-to-front",  "Bring to Front",  RibbonCommandIconKind.BringToFront);
                    g.Medium("freew.image-send-to-back",    "Send to Back",    RibbonCommandIconKind.SendToBack);
                    g.Medium("freew.image-bring-forward",   "Bring Forward",   RibbonCommandIconKind.BringForward);
                    g.Medium("freew.image-send-backward",   "Send Backward",   RibbonCommandIconKind.SendBackward);
                    // Group / Ungroup for floating images (Phase 4).
                    g.Medium("freew.object-group",   "Group",   RibbonCommandIconKind.Generic);
                    g.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                });
                // ── Picture Styles gallery group ──────────────────────────────────────────────────────────
                // Gallery injection (MainWindow.InjectGallery) keys on group id "picture-styles".
                // Each style preset command sets bundled border+effect fields.
                tab.Group("picture-styles", "Picture Styles", "Y", 98, g =>
                {
                    foreach (var preset in PictureStyleCatalog.Catalog)
                        g.Medium($"freew.image-style-{preset.Id}", preset.Name, RibbonCommandIconKind.Border);
                });
                tab.Group("picture-adjust", "Adjust", "J", 95, g =>
                {
                    g.Medium("freew.image-corrections", "Corrections", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-brightness-plus20",   "Brightness: +20%",   "1");
                        m.Item("freew.image-brightness-plus40",   "Brightness: +40%",   "2");
                        m.Item("freew.image-brightness-minus20",  "Brightness: -20%",   "3");
                        m.Item("freew.image-brightness-minus40",  "Brightness: -40%",   "4");
                        m.Item("freew.image-contrast-plus20",     "Contrast: +20%",     "5");
                        m.Item("freew.image-contrast-minus20",    "Contrast: -20%",     "6");
                        m.Item("freew.image-adjust-dialog",       "Picture Corrections…","D");
                    });
                    g.Medium("freew.image-color", "Color", RibbonCommandIconKind.Color, menu: m =>
                    {
                        m.Item("freew.image-saturation-0",        "Saturation: 0% (Greyscale)",   "G");
                        m.Item("freew.image-saturation-50",       "Saturation: 50%",              "H");
                        m.Item("freew.image-saturation-200",      "Saturation: 200%",             "J");
                        m.Item("freew.image-color-dialog",        "Color…",                       "C");
                        m.Separator();
                        m.Item("freew.image-recolor-grayscale",   "Recolor: Grayscale",           "1");
                        m.Item("freew.image-recolor-sepia",       "Recolor: Sepia",               "2");
                        m.Item("freew.image-recolor-washout",     "Recolor: Washout",             "3");
                        m.Item("freew.image-recolor-blackwhite",  "Recolor: Black and White",     "4");
                        m.Item("freew.image-recolor-none",        "Recolor: No Recolor",          "N");
                        m.Separator();
                        m.Item("freew.image-colortemp-warm",      "Color Tone: Warm (3000K)",     "W");
                        m.Item("freew.image-colortemp-cool",      "Color Tone: Cool (8000K)",     "L");
                        m.Item("freew.image-colortemp-neutral",   "Color Tone: Neutral",          "T");
                    });
                    g.Medium("freew.image-transparency", "Transparency", RibbonCommandIconKind.View, menu: m =>
                    {
                        m.Item("freew.image-transparency-25",     "Transparency: 25%",    "A");
                        m.Item("freew.image-transparency-50",     "Transparency: 50%",    "B");
                        m.Item("freew.image-transparency-75",     "Transparency: 75%",    "C");
                        m.Item("freew.image-transparency-dialog", "Transparency…",        "D");
                    });
                    // Picture Effects sub-menus: Shadow, Reflection, Glow, Soft Edges, Bevel.
                    g.Medium("freew.image-effects", "Picture Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-shadow-none",    "Shadow: No Shadow",              "N");
                        m.Item("freew.image-shadow-1",       "Shadow: Offset Diagonal",        "1");
                        m.Item("freew.image-shadow-2",       "Shadow: Offset Diagonal Medium", "2");
                        m.Item("freew.image-shadow-3",       "Shadow: Perspective",            "3");
                        m.Item("freew.image-shadow-4",       "Shadow: Offset Bottom",          "4");
                        m.Item("freew.image-shadow-5",       "Shadow: Large",                  "5");
                        m.Separator();
                        m.Item("freew.image-reflection-none","Reflection: No Reflection",      "R");
                        m.Item("freew.image-reflection-1",   "Reflection: Tight, Touching",    "A");
                        m.Item("freew.image-reflection-2",   "Reflection: Tight, 4pt",         "B");
                        m.Item("freew.image-reflection-3",   "Reflection: Tight, 8pt",         "C");
                        m.Item("freew.image-reflection-4",   "Reflection: Half, Touching",     "D");
                        m.Item("freew.image-reflection-5",   "Reflection: Half, 4pt",          "E");
                        m.Separator();
                        m.Item("freew.image-glow-none",      "Glow: No Glow",                  "G");
                        m.Item("freew.image-glow-5",         "Glow: 5 pt",                     "H");
                        m.Item("freew.image-glow-8",         "Glow: 8 pt",                     "I");
                        m.Item("freew.image-glow-11",        "Glow: 11 pt",                    "J");
                        m.Item("freew.image-glow-18",        "Glow: 18 pt",                    "K");
                        m.Separator();
                        m.Item("freew.image-softedge-none",  "Soft Edges: None",               "S");
                        m.Item("freew.image-softedge-1",     "Soft Edges: 1 pt",               "T");
                        m.Item("freew.image-softedge-2pt5",  "Soft Edges: 2.5 pt",             "U");
                        m.Item("freew.image-softedge-5",     "Soft Edges: 5 pt",               "V");
                        m.Item("freew.image-softedge-10",    "Soft Edges: 10 pt",              "X");
                        m.Separator();
                        m.Item("freew.image-bevel-none",     "Bevel: No Bevel",                "O");
                        m.Item("freew.image-bevel-1",        "Bevel: Circle",                  "P");
                        m.Item("freew.image-bevel-2",        "Bevel: Relaxed Inset",           "Q");
                        m.Item("freew.image-bevel-3",        "Bevel: Cross",                   "F");
                        m.Item("freew.image-bevel-4",        "Bevel: Cool Slant",              "M");
                    });
                    // Artistic Effects gallery (W25): named menu items, one per ImageArtisticEffect value.
                    g.Medium("freew.image-artistic", "Artistic Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-artistic-none",          "No Artistic Effect",   "N");
                        m.Item("freew.image-artistic-blur",          "Blur",                 "B");
                        m.Item("freew.image-artistic-glow-diffused", "Glow Diffused",        "G");
                        m.Item("freew.image-artistic-glow-edges",    "Glow Edges",           "E");
                        m.Item("freew.image-artistic-pencil-gray",   "Pencil Grayscale",     "A");
                        m.Item("freew.image-artistic-pencil-sketch", "Pencil Sketch",        "K");
                        m.Item("freew.image-artistic-line-drawing",  "Line Drawing",         "L");
                        m.Item("freew.image-artistic-paintbrush",    "Paint Brush",          "P");
                        m.Item("freew.image-artistic-paint-strokes", "Paint Strokes",        "T");
                        m.Item("freew.image-artistic-photocopy",     "Photocopy",            "H");
                        m.Item("freew.image-artistic-posterize",     "Posterize",            "O");
                        m.Item("freew.image-artistic-pastels",       "Pastels",              "S");
                        m.Item("freew.image-artistic-watercolor",    "Watercolor Sponge",    "W");
                        m.Item("freew.image-artistic-film-grain",    "Film Grain",           "F");
                        m.Item("freew.image-artistic-mosaic",        "Mosaic Bubbles",       "M");
                    });
                    g.Medium("freew.image-crop", "Crop", RibbonCommandIconKind.Scale);
                    g.Medium("freew.image-reset", "Reset Picture", RibbonCommandIconKind.Refresh);
                    g.Medium("freew.image-border", "Picture Border", RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border);
                });
                tab.Group("picture-size", "Size", "S", 90, g =>
                {
                    g.Medium("freew.image-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.image-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                });
            })
            // ── Chart contextual tabs — Chart Tools (shown when a chart is selected) ──────────────
            .ContextualTab("chart-design", "Chart Design",
                new RibbonTabContext("chart", "Chart Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("chart-type", "Type", "T", 100, g =>
                    g.Medium("freew.chart-type-column", "Column", RibbonCommandIconKind.ChartColumn, menu: m =>
                    {
                        m.Item("freew.chart-type-column", "Column", "C");
                        m.Item("freew.chart-type-bar", "Bar", "B");
                        m.Item("freew.chart-type-line", "Line", "L");
                        m.Item("freew.chart-type-pie", "Pie", "P");
                        m.Item("freew.chart-type-scatter", "Scatter", "X");
                        m.Item("freew.chart-type-area", "Area", "A");
                        m.Item("freew.chart-type-doughnut", "Doughnut", "D");
                    }));
                tab.Group("chart-data", "Data", "D", 90, g =>
                    g.Medium("freew.chart-edit-data", "Edit Data", RibbonCommandIconKind.Table));
                // ── Gallery groups — replaced by ChartDesignGallery live-preview controls at render time ──
                // The gallery injection (MainWindow.InjectGallery) keys on the group id: "chart-quick-layout",
                // "chart-style", "chart-colors". The placeholder Medium buttons below let the ribbon model and
                // command bus wire up backed commands; the MainWindow swaps them for gallery swatches.
                tab.Group("chart-quick-layout", "Quick Layout", "L", 85, g =>
                {
                    foreach (var layout in ChartQuickLayout.Catalog)
                        g.Medium($"freew.chart-quick-layout-{layout.Id}", layout.Name, RibbonCommandIconKind.Grid);
                });
                tab.Group("chart-style", "Chart Styles", "S", 80, g =>
                {
                    foreach (var style in ChartStyle.Catalog)
                        g.Medium($"freew.chart-style-{style.Id}", style.Name, RibbonCommandIconKind.ChartColumn);
                });
                tab.Group("chart-colors", "Change Colors", "C", 75, g =>
                {
                    foreach (var scheme in ChartColorScheme.Catalog)
                        g.Medium($"freew.chart-color-{scheme.Id}", scheme.Name, RibbonCommandIconKind.Fill);
                });
                tab.Group("chart-elements", "Chart Layouts", "E", 70, g =>
                {
                    g.Medium("freew.chart-title", "Chart Title", RibbonCommandIconKind.Header);
                    g.Medium("freew.chart-axis-titles", "Axis Titles", RibbonCommandIconKind.Ruler);
                    g.Medium("freew.chart-toggle-legend", "Legend", RibbonCommandIconKind.List);
                });
            })
            .ContextualTab("chart-format", "Chart Format",
                new RibbonTabContext("chart", "Chart Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("chart-arrange", "Arrange", "A", 100, g =>
                {
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.shape-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.shape-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.shape-flip-horizontal", "Flip Horizontal",  "H");
                    });
                });
                tab.Group("chart-size", "Size", "S", 90, g =>
                {
                    g.Medium("freew.chart-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.chart-size-dialog", "More Size Options...", RibbonCommandIconKind.Size);
                });
            })
            // ── SmartArt contextual tab — SmartArt Tools (shown when a SmartArt is selected) ─────
            .ContextualTab("smartart-design", "SmartArt Design",
                new RibbonTabContext("smartart", "SmartArt Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("smartart-create-graphic", "Create Graphic", "G", 100, g =>
                {
                    g.Medium("freew.smartart-add-shape", "Add Shape", RibbonCommandIconKind.Insert);
                    g.Medium("freew.smartart-remove-shape", "Remove Shape", RibbonCommandIconKind.Delete);
                    g.RowBreak();
                    g.Medium("freew.smartart-promote", "Promote", RibbonCommandIconKind.IndentDecrease);
                    g.Medium("freew.smartart-demote", "Demote", RibbonCommandIconKind.IndentIncrease);
                    g.RowBreak();
                    g.Medium("freew.smartart-move-up", "Move Up", RibbonCommandIconKind.ArrowUp);
                    g.Medium("freew.smartart-move-down", "Move Down", RibbonCommandIconKind.ArrowDown);
                });
                tab.Group("smartart-edit", "Edit", "E", 90, g =>
                    g.Medium("freew.smartart-edit-text", "Edit Text", RibbonCommandIconKind.TextFunction));
                // Galleries: placeholder commands — galleries are injected by MainWindow via InjectGallery.
                tab.Group("smartart-layouts", "Layouts", "L", 80, g =>
                    g.Medium("freew.smartart-change-layout", "Change Layout", RibbonCommandIconKind.SmartArt));
                tab.Group("smartart-colors", "SmartArt Styles", "C", 70, g =>
                {
                    g.Medium("freew.smartart-change-colors", "Change Colors", RibbonCommandIconKind.Fill);
                    g.Medium("freew.smartart-change-style", "Styles", RibbonCommandIconKind.Font);
                });
                tab.Group("smartart-arrange", "Arrange", "A", 60, g =>
                {
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.shape-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.shape-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.shape-flip-horizontal", "Flip Horizontal",  "H");
                    });
                });
            })
            .ContextualTab("table-design", "Table Design",
                new RibbonTabContext("table", "Table Tools", RibbonContextColor.Teal), tab =>
            {
                tab.Group("table-style-options", "Table Style Options", "O", 100, g =>
                {
                    g.Medium("freew.table-header-row", "Header Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-last-row", "Last Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-first-column", "First Column", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-last-column", "Last Column", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-banded-rows", "Banded Rows", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-banded-cols", "Banded Columns", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                });
                tab.Group("table-style", "Table Style", "Y", 80, g =>
                {
                    g.Medium("freew.cell-shading", "Shading", RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill);
                    g.Medium("freew.cell-borders", "Borders", RibbonCommandIconKind.Grid);
                });
                tab.Group("draw-borders", "Draw Borders", "D", 60, g =>
                {
                    g.Medium("freew.draw-table", "Draw Table", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Border);
                    g.Medium("freew.eraser", "Eraser", RibbonCommandIconKind.Clear);
                });
            })
            .ContextualTab("table-layout", "Table Layout",
                new RibbonTabContext("table", "Table Tools", RibbonContextColor.Teal), tab =>
            {
                tab.Group("table-table", "Table", "T", 70, g =>
                {
                    g.Medium("freew.table-select-table", "Select Table", RibbonCommandIconKind.Table);
                    g.Medium("freew.table-select-row", "Select Row", RibbonCommandIconKind.Table);
                    g.RowBreak();
                    g.Medium("freew.table-select-col", "Select Column", RibbonCommandIconKind.Table);
                    g.Medium("freew.table-select-cell", "Select Cell", RibbonCommandIconKind.Table);
                    g.RowBreak();
                    g.Medium("freew.table-view-gridlines", "View Gridlines", RibbonCommandIconKind.Grid);
                    g.Medium("freew.table-properties", "Properties", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                });
                tab.Group("table-rows-cols", "Rows & Columns", "R", 120, g =>
                {
                    g.Medium("freew.table-insert-above", "Insert Above", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-insert-row", "Insert Below", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-insert-col-left", "Insert Left", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-insert-col", "Insert Right", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-delete-row", "Delete Rows", RibbonCommandIconKind.Delete);
                    g.Medium("freew.table-delete-col", "Delete Columns", RibbonCommandIconKind.Delete);
                    g.RowBreak();
                    g.Medium("freew.table-delete", "Delete Table", RibbonCommandIconKind.Delete);
                });
                tab.Group("table-merge", "Merge", "M", 90, g =>
                {
                    g.Medium("freew.merge-cells", "Merge Cells", RibbonCommandIconKind.Merge);
                    g.Medium("freew.split-cell", "Split Cell", RibbonCommandIconKind.Grid);
                    g.RowBreak();
                    g.Medium("freew.split-table", "Split Table", RibbonCommandIconKind.Grid);
                });
                tab.Group("table-cell-size", "Cell Size", "Z", 100, g =>
                {
                    g.Medium("freew.table-row-height", "Row Height", RibbonCommandIconKind.Size);
                    g.Medium("freew.table-col-width", "Column Width", RibbonCommandIconKind.Size);
                    g.RowBreak();
                    g.Medium("freew.table-distribute-rows", "Distribute Rows", RibbonCommandIconKind.Grid);
                    g.Medium("freew.table-distribute-cols", "Distribute Columns", RibbonCommandIconKind.Grid);
                    g.RowBreak();
                    g.Medium("freew.table-autofit-contents", "AutoFit Contents", RibbonCommandIconKind.Scale);
                    g.Medium("freew.table-autofit-window", "AutoFit Window", RibbonCommandIconKind.Scale);
                    g.Medium("freew.table-autofit-fixed", "Fixed Column Width", RibbonCommandIconKind.Size);
                });
                tab.Group("table-alignment", "Alignment", "A", 110, g =>
                {
                    g.Medium("freew.cell-align-top-left", "Top Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-top-center", "Top Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-top-right", "Top Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.cell-align-middle-left", "Middle Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-middle-center", "Middle Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-middle-right", "Middle Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.cell-align-bottom-left", "Bottom Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-bottom-center", "Bottom Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-bottom-right", "Bottom Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.table-cell-margins", "Cell Margins", RibbonCommandIconKind.Margins);
                    g.RowBreak();
                    g.Medium("freew.cell-text-direction-horizontal", "Horizontal", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-text-direction-rotate90", "Rotate Text Up", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-text-direction-rotate270", "Rotate Text Down", RibbonCommandIconKind.AlignLeft);
                });
                tab.Group("table-data", "Data", "D", 70, g =>
                {
                    g.Medium("freew.table-repeat-header", "Repeat Header Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-formula", "Formula", RibbonCommandIconKind.Sum, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.sort", "Sort", RibbonCommandIconKind.Sort);
                    g.Medium("freew.table-to-text", "Convert to Text", RibbonCommandIconKind.TextFunction);
                });
            })
            // ── Header & Footer Design contextual tab — Header & Footer Tools ────────────────────────
            // Activation model: dialog approach (not an in-document edit region). FreeW's FlowDocument
            // is a single continuous stream; there is no WYSIWYG header region. Every command writes
            // directly into FinalSectionHeadersFooters / PageSettings via ApplyPageSettings and
            // round-trips through DocxWriter. The contextual key "header-footer" can be activated from
            // Insert > Header / Footer commands via the ribbon controller.
            .AddHeaderFooterDesignTab(capabilities)
            .Build();

        return definition with { Tabs = OrderVisibleTabs(definition.Tabs) };
    }

    private static IReadOnlyList<RibbonTab> OrderVisibleTabs(IReadOnlyList<RibbonTab> tabs)
    {
        string[] wordOrder =
        [
            "home",
            "insert",
            "design",
            "layout",
            "references",
            "mailings",
            "review",
            "view",
            "help",
            "developer"
        ];

        var visibleOrder = wordOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);

        var visible = tabs
            .Where(tab => !tab.IsContextual)
            .OrderBy(tab => visibleOrder.TryGetValue(tab.Id, out var index) ? index : int.MaxValue)
            .ThenBy(tab => visibleOrder.ContainsKey(tab.Id) ? 0 : 1)
            .ToArray();
        var contextual = tabs.Where(tab => tab.IsContextual).ToArray();

        return visible.Concat(contextual).ToArray();
    }
}
