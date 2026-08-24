using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Canonical contextual-tab topology shared by the WPF and Avalonia FreeW renderers.
/// Portable overrides preserve only the native control representations that genuinely differ.
/// </summary>
internal static partial class FreeWCanonicalRibbonTabs
{
    internal static RibbonDefinitionBuilder AddPictureContextualTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("picture-format", "Picture Format",
            new RibbonTabContext(capabilities.PictureContextKey, "Picture Tools", RibbonContextColor.Orange), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "picture.arrange",
                    tab => tab.Group("picture-arrange", "Arrange", "A", 100, group =>
                        {
                            group.Medium("freew.image-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: menu =>
                            {
                                menu.Item("freew.image-wrap-inline", "In Line with Text", "I");
                                menu.Item("freew.image-wrap-square", "Square", "S");
                                menu.Item("freew.image-wrap-tight", "Tight", "T");
                                menu.Item("freew.image-wrap-top-bottom", "Top and Bottom", "B");
                                menu.Item("freew.image-wrap-behind", "Behind Text", "H");
                                menu.Item("freew.image-wrap-front", "In Front of Text", "F");
                            });
                            group.Medium("freew.image-position", "Position", RibbonCommandIconKind.Margins);
                            group.Medium("freew.image-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: menu =>
                            {
                                menu.Item("freew.image-rotate-right90", "Rotate Right 90\u00B0", "R");
                                menu.Item("freew.image-rotate-left90", "Rotate Left 90\u00B0", "L");
                                menu.Item("freew.image-flip-vertical", "Flip Vertical", "V");
                                menu.Item("freew.image-flip-horizontal", "Flip Horizontal", "H");
                            });
                            group.Medium("freew.image-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.image-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.image-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                            group.Medium("freew.image-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                            group.Medium("freew.image-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                            group.Medium("freew.image-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.image-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.image-bring-to-front", "Bring to Front", RibbonCommandIconKind.BringToFront);
                            group.Medium("freew.image-send-to-back", "Send to Back", RibbonCommandIconKind.SendToBack);
                            group.Medium("freew.image-bring-forward", "Bring Forward", RibbonCommandIconKind.BringForward);
                            group.Medium("freew.image-send-backward", "Send Backward", RibbonCommandIconKind.SendBackward);
                            group.Medium("freew.object-group", "Group", RibbonCommandIconKind.Generic);
                            group.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                        }),
                    tab => tab.Group("picture-arrange", "Arrange", null, 100, group =>
                        {
                            group.Dropdown("freew.image-position", "Position", BuildFloatingPositionMenu("image"));
                            group.Dropdown("freew.image-wrap", "Wrap Text", BuildWrapMenu("image"));
                            group.Dropdown("freew.image-rotate", "Rotate", BuildRotateMenu("image"));
                            group.Button("freew.shape-bring-to-front", "Bring to Front");
                            group.Button("freew.shape-send-to-back", "Send to Back");
                            group.Button("freew.shape-bring-forward", "Bring Forward");
                            group.Button("freew.shape-send-backward", "Send Backward");
                            group.Button("freew.image-align-left", "Align Left");
                            group.Button("freew.image-align-center", "Align Center");
                            group.Button("freew.image-align-right", "Align Right");
                            group.Button("freew.image-align-to-page", "Align to Page");
                            group.Button("freew.image-align-to-margin", "Align to Margin");
                            group.Button("freew.image-distribute-h", "Distribute Horizontally");
                            group.Button("freew.image-distribute-v", "Distribute Vertically");
                            group.Button("freew.object-group", "Group");
                            group.Button("freew.object-ungroup", "Ungroup");
                        }));

                topology.Section(
                    "picture.styles",
                    tab => tab.Group("picture-styles", "Picture Styles", "Y", 98, group =>
                        {
                            foreach (var preset in PictureStyleCatalog.Catalog)
                                group.Medium($"freew.image-style-{preset.Id}", preset.Name, RibbonCommandIconKind.Border);
                        }),
                    tab => tab.Group("picture-styles", "Picture Styles", null, 98, group =>
                        {
                            foreach (var preset in PictureStyleCatalog.Catalog)
                            {
                                group.Button($"freew.image-style-{preset.Id}", preset.Name, button => button with
                                {
                                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                                });
                            }
                        }));

                topology.Section(
                    "picture.adjust",
                    tab => tab.Group("picture-adjust", "Adjust", "J", 95, group =>
                        {
                            group.Medium("freew.image-corrections", "Corrections", RibbonCommandIconKind.Effects, menu: menu =>
                            {
                                menu.Item("freew.image-brightness-plus20", "Brightness: +20%", "1");
                                menu.Item("freew.image-brightness-plus40", "Brightness: +40%", "2");
                                menu.Item("freew.image-brightness-minus20", "Brightness: -20%", "3");
                                menu.Item("freew.image-brightness-minus40", "Brightness: -40%", "4");
                                menu.Item("freew.image-contrast-plus20", "Contrast: +20%", "5");
                                menu.Item("freew.image-contrast-minus20", "Contrast: -20%", "6");
                                menu.Item("freew.image-adjust-dialog", "Picture Corrections\u2026", "D");
                            });
                            group.Medium("freew.image-color", "Color", RibbonCommandIconKind.Color, menu: menu =>
                            {
                                menu.Item("freew.image-saturation-0", "Saturation: 0% (Greyscale)", "G");
                                menu.Item("freew.image-saturation-50", "Saturation: 50%", "H");
                                menu.Item("freew.image-saturation-200", "Saturation: 200%", "J");
                                menu.Item("freew.image-color-dialog", "Color\u2026", "C");
                                menu.Separator();
                                menu.Item("freew.image-recolor-grayscale", "Recolor: Grayscale", "1");
                                menu.Item("freew.image-recolor-sepia", "Recolor: Sepia", "2");
                                menu.Item("freew.image-recolor-washout", "Recolor: Washout", "3");
                                menu.Item("freew.image-recolor-blackwhite", "Recolor: Black and White", "4");
                                menu.Item("freew.image-recolor-none", "Recolor: No Recolor", "N");
                                menu.Separator();
                                menu.Item("freew.image-colortemp-warm", "Color Tone: Warm (3000K)", "W");
                                menu.Item("freew.image-colortemp-cool", "Color Tone: Cool (8000K)", "L");
                                menu.Item("freew.image-colortemp-neutral", "Color Tone: Neutral", "T");
                            });
                            group.Medium("freew.image-transparency", "Transparency", RibbonCommandIconKind.View, menu: menu =>
                            {
                                menu.Item("freew.image-transparency-25", "Transparency: 25%", "A");
                                menu.Item("freew.image-transparency-50", "Transparency: 50%", "B");
                                menu.Item("freew.image-transparency-75", "Transparency: 75%", "C");
                                menu.Item("freew.image-transparency-dialog", "Transparency\u2026", "D");
                            });
                            group.Medium("freew.image-effects", "Picture Effects", RibbonCommandIconKind.Effects, menu: menu =>
                            {
                                menu.Item("freew.image-shadow-none", "Shadow: No Shadow", "N");
                                menu.Item("freew.image-shadow-1", "Shadow: Offset Diagonal", "1");
                                menu.Item("freew.image-shadow-2", "Shadow: Offset Diagonal Medium", "2");
                                menu.Item("freew.image-shadow-3", "Shadow: Perspective", "3");
                                menu.Item("freew.image-shadow-4", "Shadow: Offset Bottom", "4");
                                menu.Item("freew.image-shadow-5", "Shadow: Large", "5");
                                menu.Separator();
                                menu.Item("freew.image-reflection-none", "Reflection: No Reflection", "R");
                                menu.Item("freew.image-reflection-1", "Reflection: Tight, Touching", "A");
                                menu.Item("freew.image-reflection-2", "Reflection: Tight, 4pt", "B");
                                menu.Item("freew.image-reflection-3", "Reflection: Tight, 8pt", "C");
                                menu.Item("freew.image-reflection-4", "Reflection: Half, Touching", "D");
                                menu.Item("freew.image-reflection-5", "Reflection: Half, 4pt", "E");
                                menu.Separator();
                                menu.Item("freew.image-glow-none", "Glow: No Glow", "G");
                                menu.Item("freew.image-glow-5", "Glow: 5 pt", "H");
                                menu.Item("freew.image-glow-8", "Glow: 8 pt", "I");
                                menu.Item("freew.image-glow-11", "Glow: 11 pt", "J");
                                menu.Item("freew.image-glow-18", "Glow: 18 pt", "K");
                                menu.Separator();
                                menu.Item("freew.image-softedge-none", "Soft Edges: None", "S");
                                menu.Item("freew.image-softedge-1", "Soft Edges: 1 pt", "T");
                                menu.Item("freew.image-softedge-2pt5", "Soft Edges: 2.5 pt", "U");
                                menu.Item("freew.image-softedge-5", "Soft Edges: 5 pt", "V");
                                menu.Item("freew.image-softedge-10", "Soft Edges: 10 pt", "X");
                                menu.Separator();
                                menu.Item("freew.image-bevel-none", "Bevel: No Bevel", "O");
                                menu.Item("freew.image-bevel-1", "Bevel: Circle", "P");
                                menu.Item("freew.image-bevel-2", "Bevel: Relaxed Inset", "Q");
                                menu.Item("freew.image-bevel-3", "Bevel: Cross", "F");
                                menu.Item("freew.image-bevel-4", "Bevel: Cool Slant", "M");
                            });
                            group.Medium("freew.image-artistic", "Artistic Effects", RibbonCommandIconKind.Effects, menu: menu =>
                            {
                                menu.Item("freew.image-artistic-none", "No Artistic Effect", "N");
                                menu.Item("freew.image-artistic-blur", "Blur", "B");
                                menu.Item("freew.image-artistic-glow-diffused", "Glow Diffused", "G");
                                menu.Item("freew.image-artistic-glow-edges", "Glow Edges", "E");
                                menu.Item("freew.image-artistic-pencil-gray", "Pencil Grayscale", "A");
                                menu.Item("freew.image-artistic-pencil-sketch", "Pencil Sketch", "K");
                                menu.Item("freew.image-artistic-line-drawing", "Line Drawing", "L");
                                menu.Item("freew.image-artistic-paintbrush", "Paint Brush", "P");
                                menu.Item("freew.image-artistic-paint-strokes", "Paint Strokes", "T");
                                menu.Item("freew.image-artistic-photocopy", "Photocopy", "H");
                                menu.Item("freew.image-artistic-posterize", "Posterize", "O");
                                menu.Item("freew.image-artistic-pastels", "Pastels", "S");
                                menu.Item("freew.image-artistic-watercolor", "Watercolor Sponge", "W");
                                menu.Item("freew.image-artistic-film-grain", "Film Grain", "F");
                                menu.Item("freew.image-artistic-mosaic", "Mosaic Bubbles", "M");
                            });
                            group.Medium("freew.image-crop", "Crop", RibbonCommandIconKind.Scale);
                            group.Medium("freew.image-reset", "Reset Picture", RibbonCommandIconKind.Refresh);
                            group.Medium("freew.image-border", "Picture Border", RibbonCommandIconKind.Border,
                                accent: RibbonCommandIconAccent.Border);
                        }),
                    tab => tab.Group("picture-adjust", "Adjust", null, 90, group =>
                        {
                            group.Dropdown("freew.image-corrections", "Corrections", BuildPictureCorrectionsMenu());
                            group.Dropdown("freew.image-color", "Color", BuildPictureColorMenu());
                            group.Dropdown("freew.image-transparency", "Transparency", BuildPictureTransparencyMenu());
                            group.Dropdown("freew.image-effects", "Picture Effects", BuildPictureEffectsMenu());
                            group.Dropdown("freew.image-artistic", "Artistic Effects", BuildPictureArtisticEffectsMenu());
                            group.Button("freew.image-reset", "Reset Picture", button => button with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh)
                            });
                            group.Button("freew.image-border", "Picture Border", button => button with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border)
                            });
                            group.Button("freew.image-crop", "Crop", button => button with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale)
                            });
                        }));

                topology.Section(
                    "picture.size",
                    tab => tab.Group("picture-size", "Size", "S", 90, group =>
                        {
                            group.Medium("freew.image-size", "Size", RibbonCommandIconKind.Size);
                            group.Medium("freew.image-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                        }),
                    tab => tab.Group("picture-size", "Size", null, 90, group =>
                        {
                            group.ComboBox("freew.image-width", "Width", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.ComboBox("freew.image-height", "Height", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.Button("freew.image-size", "Size", button => button with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size)
                            });
                            group.Button("freew.image-alt-text", "Alt Text", button => button with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Info)
                            });
                        }));

                topology.Build();
            });

    internal static RibbonDefinitionBuilder AddDrawingContextualTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("drawing-format", "Drawing Format",
            new RibbonTabContext(capabilities.DrawingContextKey, "Drawing Tools", RibbonContextColor.Purple), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "drawing.insert",
                    tab => tab.Group("drawing-insert", "Insert Shapes", "I", 110, group =>
                        {
                            group.Medium("freew.shape-change", "Change Shape", RibbonCommandIconKind.Shapes, menu: menu =>
                            {
                                menu.Item("freew.shape-change-rectangle", "Rectangle", "R");
                                menu.Item("freew.shape-change-rounded", "Rounded Rectangle", "U");
                                menu.Item("freew.shape-change-ellipse", "Oval", "O");
                            });
                            group.Medium("freew.shape-edit-shape", "Edit Shape", RibbonCommandIconKind.Generic, menu: menu =>
                            {
                                menu.Item("freew.shape-convert-freeform", "Convert to Freeform", "F");
                                menu.Item("freew.shape-edit-points", "Edit Points", "E");
                            });
                        }));

                topology.Section(
                    "drawing.styles",
                    tab => tab.Group("drawing-styles", "Shape Styles", "H", 100, group =>
                        {
                            group.Medium("freew.shape-styles-gallery", "Shape Styles", RibbonCommandIconKind.Styles);
                            group.Medium("freew.shape-fill", "Shape Fill", RibbonCommandIconKind.Fill,
                                accent: RibbonCommandIconAccent.Fill, menu: menu =>
                            {
                                menu.Item("freew.shape-fill-no-fill", "No Fill", "N");
                                menu.Separator();
                                menu.Item("freew.shape-fill-gradient-blue", "Gradient Blue", "G");
                                menu.Item("freew.shape-fill-gradient-orange", "Gradient Orange", "O");
                                menu.Item("freew.shape-fill-pattern-diag", "Pattern: Diagonal Cross", "D");
                            });
                            group.Medium("freew.shape-outline", "Shape Outline", RibbonCommandIconKind.Border,
                                accent: RibbonCommandIconAccent.Border, menu: menu =>
                            {
                                menu.Item("freew.shape-outline-no-outline", "No Outline", "N");
                                menu.Item("freew.shape-outline-solid", "Solid", "S");
                                menu.Item("freew.shape-outline-dash", "Dash", "D");
                                menu.Item("freew.shape-outline-dot", "Dot", "O");
                            });
                            group.Medium("freew.shape-effects", "Shape Effects", RibbonCommandIconKind.Effects, menu: menu =>
                            {
                                menu.Item("freew.shape-effects-none", "No Effects", "N");
                                menu.Separator();
                                menu.Item("freew.shape-effect-shadow", "Shadow", "S");
                                menu.Item("freew.shape-effect-glow", "Glow", "G");
                                menu.Item("freew.shape-effect-soft-edge", "Soft Edges", "E");
                                menu.Item("freew.shape-effect-reflection", "Reflection", "R");
                                menu.Item("freew.shape-effect-bevel", "Bevel", "B");
                            });
                        }),
                    tab => tab.Group("drawing-styles", "Shape Styles", null, 100, group =>
                        {
                            group.Dropdown("freew.shape-styles-gallery", "Shape Styles", BuildShapeStylesMenu());
                            group.Dropdown("freew.shape-fill", "Shape Fill", BuildShapeFillMenu());
                            group.Dropdown("freew.shape-outline", "Shape Outline", BuildShapeOutlineMenu());
                            group.Dropdown("freew.shape-effects", "Shape Effects", BuildShapeEffectsMenu());
                            group.Dropdown("freew.shape-change", "Change Shape", BuildShapeChangeMenu());
                            group.Dropdown("freew.shape-edit-shape", "Edit Shape", BuildShapeEditMenu());
                            group.Dropdown("freew.shape-text-direction", "Text Direction", BuildShapeTextDirectionMenu());
                        }));

                topology.Section(
                    "drawing.text",
                    tab => tab.Group("drawing-text", "Text", "X", 90, group =>
                        {
                            group.Medium("freew.shape-text-direction", "Text Direction", RibbonCommandIconKind.TextBox, menu: menu =>
                            {
                                menu.Item("freew.shape-text-horizontal", "Horizontal", "H");
                                menu.Item("freew.shape-text-rotate90", "Rotate 90\u00B0", "R");
                                menu.Item("freew.shape-text-rotate270", "Rotate 270\u00B0", "T");
                            });
                        }));

                topology.Section(
                    "drawing.wordart",
                    tab => tab.Group("drawing-wordart", "WordArt Styles", "W", 85, group =>
                        {
                            group.Medium(
                                WordArtRibbonWorkflow.StyleMenuCommandId.Value,
                                "WordArt Style",
                                RibbonCommandIconKind.WordArt,
                                menu: BuildWordArtStyleMenu);
                            group.Medium(
                                WordArtRibbonWorkflow.WarpMenuCommandId.Value,
                                "Text Effects: Transform",
                                RibbonCommandIconKind.WordArt,
                                menu: BuildWordArtWarpMenu);
                        }));

                topology.Section(
                    "drawing.arrange",
                    tab => tab.Group("drawing-arrange", "Arrange", "A", 80, group =>
                        {
                            group.Medium("freew.shape-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: menu =>
                            {
                                menu.Item("freew.shape-wrap-inline", "In Line with Text", "I");
                                menu.Item("freew.shape-wrap-square", "Square", "S");
                                menu.Item("freew.shape-wrap-tight", "Tight", "T");
                                menu.Item("freew.shape-wrap-top-bottom", "Top and Bottom", "B");
                                menu.Item("freew.shape-wrap-behind", "Behind Text", "H");
                                menu.Item("freew.shape-wrap-front", "In Front of Text", "F");
                            });
                            group.Medium("freew.shape-position", "Position", RibbonCommandIconKind.Margins);
                            group.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: menu =>
                            {
                                menu.Item("freew.shape-rotate-right90", "Rotate Right 90\u00B0", "R");
                                menu.Item("freew.shape-rotate-left90", "Rotate Left 90\u00B0", "L");
                                menu.Item("freew.shape-flip-vertical", "Flip Vertical", "V");
                                menu.Item("freew.shape-flip-horizontal", "Flip Horizontal", "H");
                            });
                            group.Medium("freew.shape-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.shape-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.shape-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                            group.Medium("freew.shape-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                            group.Medium("freew.shape-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                            group.Medium("freew.shape-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.shape-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.shape-bring-to-front", "Bring to Front", RibbonCommandIconKind.BringToFront);
                            group.Medium("freew.shape-send-to-back", "Send to Back", RibbonCommandIconKind.SendToBack);
                            group.Medium("freew.shape-bring-forward", "Bring Forward", RibbonCommandIconKind.BringForward);
                            group.Medium("freew.shape-send-backward", "Send Backward", RibbonCommandIconKind.SendBackward);
                            group.Medium("freew.object-group", "Group", RibbonCommandIconKind.Generic);
                            group.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                        }),
                    tab => tab.Group("drawing-arrange", "Arrange", null, 90, group =>
                        {
                            group.Dropdown("freew.shape-position", "Position", BuildFloatingPositionMenu("shape"));
                            group.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                            group.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                            group.Button("freew.image-bring-to-front", "Bring to Front");
                            group.Button("freew.image-send-to-back", "Send to Back");
                            group.Button("freew.image-bring-forward", "Bring Forward");
                            group.Button("freew.image-send-backward", "Send Backward");
                            group.Button("freew.shape-align-left", "Align Left");
                            group.Button("freew.shape-align-center", "Align Center");
                            group.Button("freew.shape-align-right", "Align Right");
                            group.Button("freew.shape-align-to-page", "Align to Page");
                            group.Button("freew.shape-align-to-margin", "Align to Margin");
                            group.Button("freew.shape-distribute-h", "Distribute Horizontally");
                            group.Button("freew.shape-distribute-v", "Distribute Vertically");
                            group.Button("freew.object-group", "Group");
                            group.Button("freew.object-ungroup", "Ungroup");
                        }));

                topology.Section(
                    "drawing.size",
                    tab => tab.Group("drawing-size", "Size", "S", 70, group =>
                        {
                            group.Medium("freew.shape-size", "Size", RibbonCommandIconKind.Size);
                            group.Medium("freew.shape-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                        }),
                    tab => tab.Group("drawing-size", "Size", null, 80, group =>
                        {
                            group.ComboBox("freew.shape-width", "Width", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.ComboBox("freew.shape-height", "Height", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.Dropdown("freew.shape-size", "Size", BuildShapeSizeMenu());
                            group.Dropdown("freew.shape-alt-text", "Alt Text", BuildShapeAltTextMenu());
                        }));

                topology.Build();
            });

    internal static RibbonDefinitionBuilder AddChartContextualTabs(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        AddChartDesignTopology(builder, capabilities);
        return AddChartFormatTopology(builder, capabilities);
    }

    private static RibbonDefinitionBuilder AddChartDesignTopology(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("chart-design", "Chart Design",
            new RibbonTabContext(capabilities.ChartContextKey, "Chart Tools", RibbonContextColor.Orange), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "chart.type",
                    tab => tab.Group("chart-type", "Type", "T", 100, group =>
                        group.Medium("freew.chart-type-column", "Column", RibbonCommandIconKind.ChartColumn, menu: menu =>
                        {
                            menu.Item("freew.chart-type-column", "Column", "C");
                            menu.Item("freew.chart-type-bar", "Bar", "B");
                            menu.Item("freew.chart-type-line", "Line", "L");
                            menu.Item("freew.chart-type-pie", "Pie", "P");
                            menu.Item("freew.chart-type-scatter", "Scatter", "X");
                            menu.Item("freew.chart-type-area", "Area", "A");
                            menu.Item("freew.chart-type-doughnut", "Doughnut", "D");
                        })),
                    tab => tab.Group("chart-type", "Type", null, 100, group =>
                        group.Dropdown("freew.chart-type", "Change Chart Type", BuildChartTypeMenu())));

                topology.Section(
                    "chart.data",
                    tab => tab.Group("chart-data", "Data", "D", 90, group =>
                        group.Medium("freew.chart-edit-data", "Edit Data", RibbonCommandIconKind.Table)),
                    tab => tab.Group("chart-data", "Data", null, 80, group =>
                        {
                            group.ComboBox("freew.chart-edit-data", "Edit Data", control => control with
                            {
                                Items = new[] { "Quarterly Sales", "Monthly Revenue" },
                                Width = 132
                            });
                        }));

                topology.Section(
                    "chart.quick-layout",
                    tab => tab.Group("chart-quick-layout", "Quick Layout", "L", 85, group =>
                        {
                            foreach (var layout in ChartQuickLayout.Catalog)
                                group.Medium($"freew.chart-quick-layout-{layout.Id}", layout.Name, RibbonCommandIconKind.Grid);
                        }),
                    tab => tab.Group("chart-quick-layout", "Quick Layout", null, 85, group =>
                        {
                            foreach (var layout in ChartQuickLayout.Catalog)
                            {
                                group.Button($"freew.chart-quick-layout-{layout.Id}", layout.Name, button => button with
                                {
                                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid)
                                });
                            }
                        }));

                topology.Section(
                    "chart.styles",
                    tab =>
                    {
                        tab.Group("chart-style", "Chart Styles", "S", 80, group =>
                        {
                            foreach (var style in ChartStyle.Catalog)
                                group.Medium($"freew.chart-style-{style.Id}", style.Name, RibbonCommandIconKind.ChartColumn);
                        });

                        tab.Group("chart-colors", "Change Colors", "C", 75, group =>
                        {
                            foreach (var scheme in ChartColorScheme.Catalog)
                                group.Medium(ChartColorRibbonCommandCatalog.CommandId(scheme), scheme.Name, RibbonCommandIconKind.Fill);
                        });
                    },
                    tab => tab.Group("chart-styles", "Chart Styles", null, 90, group =>
                        {
                            group.Dropdown("freew.chart-style", "Chart Styles", BuildChartStyleMenu());
                            group.Dropdown("freew.chart-colors", "Change Colors", BuildChartColorsMenu());
                        }));

                topology.Section(
                    "chart.elements",
                    tab => tab.Group("chart-elements", "Chart Layouts", "E", 70, group =>
                        {
                            group.Medium("freew.chart-title", "Chart Title", RibbonCommandIconKind.Header);
                            group.Medium("freew.chart-axis-titles", "Axis Titles", RibbonCommandIconKind.Ruler);
                            group.Medium("freew.chart-toggle-legend", "Legend", RibbonCommandIconKind.List);
                        }),
                    tab => tab.Group("chart-elements", "Chart Elements", null, 80, group =>
                        {
                            group.Toggle("freew.chart-toggle-legend", "Legend");
                            group.Button("freew.chart-title", "Chart Title");
                            group.Button("freew.chart-axis-titles", "Axis Titles");
                        }));

                topology.Build();
            });

    private static RibbonDefinitionBuilder AddChartFormatTopology(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("chart-format", "Chart Format",
            new RibbonTabContext(
                capabilities.ChartContextKey,
                "Chart Tools",
                RibbonContextColor.Orange), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "chart.arrange",
                    tab => tab.Group("chart-arrange", "Arrange", "A", 100, group =>
                        {
                            group.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: menu =>
                            {
                                menu.Item("freew.shape-rotate-right90", "Rotate Right 90\u00B0", "R");
                                menu.Item("freew.shape-rotate-left90", "Rotate Left 90\u00B0", "L");
                                menu.Item("freew.shape-flip-vertical", "Flip Vertical", "V");
                                menu.Item("freew.shape-flip-horizontal", "Flip Horizontal", "H");
                            });
                        }),
                    tab => tab.Group("chart-arrange", "Arrange", null, 100, group =>
                        {
                            group.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                            group.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                            group.Button("freew.image-bring-to-front", "Bring to Front");
                            group.Button("freew.image-send-to-back", "Send to Back");
                            group.Button("freew.image-bring-forward", "Bring Forward");
                            group.Button("freew.image-send-backward", "Send Backward");
                        }));

                topology.Section(
                    "chart.size",
                    tab => tab.Group("chart-size", "Size", "S", 90, group =>
                        {
                            group.Medium("freew.chart-size", "Size", RibbonCommandIconKind.Size);
                            group.Medium("freew.chart-size-dialog", "More Size Options...", RibbonCommandIconKind.Size);
                        }),
                    tab => tab.Group("chart-size", "Size", null, 90, group =>
                        {
                            group.ComboBox("freew.chart-size", "Size", control => control with
                            {
                                Items = new[] { "360 x 216", "400 x 300", "468 x 288" },
                                Width = 90
                            });
                            group.ComboBox("freew.shape-width", "Width", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.ComboBox("freew.shape-height", "Height", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.Button("freew.chart-size-dialog", "More Size Options...");
                        }));

                topology.Build();
            });

    internal static RibbonDefinitionBuilder AddSmartArtContextualTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("smartart-design", "SmartArt Design",
            new RibbonTabContext(capabilities.SmartArtContextKey, "SmartArt Tools", RibbonContextColor.Orange), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "smartart.create",
                    tab => tab.Group("smartart-create-graphic", "Create Graphic", "G", 100, group =>
                        {
                            group.Medium("freew.smartart-add-shape", "Add Shape", RibbonCommandIconKind.Insert);
                            group.Medium("freew.smartart-remove-shape", "Remove Shape", RibbonCommandIconKind.Delete);
                            group.RowBreak();
                            group.Medium("freew.smartart-promote", "Promote", RibbonCommandIconKind.IndentDecrease);
                            group.Medium("freew.smartart-demote", "Demote", RibbonCommandIconKind.IndentIncrease);
                            group.RowBreak();
                            group.Medium("freew.smartart-move-up", "Move Up", RibbonCommandIconKind.ArrowUp);
                            group.Medium("freew.smartart-move-down", "Move Down", RibbonCommandIconKind.ArrowDown);
                        }),
                    tab => tab.Group("smartart-create-graphic", "Create Graphic", null, 120, group =>
                        {
                            group.Button("freew.smartart-add-shape", "Add Shape");
                            group.Button("freew.smartart-remove-shape", "Remove Shape");
                            group.RowBreak();
                            group.Button("freew.smartart-promote", "Promote");
                            group.Button("freew.smartart-demote", "Demote");
                            group.RowBreak();
                            group.Button("freew.smartart-move-up", "Move Up");
                            group.Button("freew.smartart-move-down", "Move Down");
                        }));

                topology.Section(
                    "smartart.edit",
                    tab => tab.Group("smartart-edit", "Edit", "E", 90, group =>
                        group.Medium("freew.smartart-edit-text", "Edit Text", RibbonCommandIconKind.TextFunction)),
                    tab => tab.Group("smartart-edit", "Edit", null, 90, group =>
                        group.Button("freew.smartart-edit-text", "Edit Text")));

                topology.Section(
                    "smartart.layouts",
                    tab => tab.Group("smartart-layouts", "Layouts", "L", 80, group =>
                        group.Dropdown("freew.smartart-layout", "Layouts", BuildSmartArtLayoutMenu())));

                topology.Section(
                    "smartart.styles",
                    tab => tab.Group("smartart-styles", "SmartArt Styles", "C", 90, group =>
                        {
                            group.Dropdown("freew.smartart-colors", "Change Colors", BuildSmartArtColorsMenu());
                            group.Dropdown("freew.smartart-change-style", "Styles", BuildSmartArtStylesMenu());
                        }));

                topology.Section(
                    "smartart.arrange",
                    tab => tab.Group("smartart-arrange", "Arrange", "A", 60, group =>
                        {
                            group.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: menu =>
                            {
                                menu.Item("freew.shape-rotate-right90", "Rotate Right 90\u00B0", "R");
                                menu.Item("freew.shape-rotate-left90", "Rotate Left 90\u00B0", "L");
                                menu.Item("freew.shape-flip-vertical", "Flip Vertical", "V");
                                menu.Item("freew.shape-flip-horizontal", "Flip Horizontal", "H");
                            });
                        }),
                    tab => tab.Group("smartart-arrange", "Arrange", null, 80, group =>
                        {
                            group.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                            group.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                            group.Button("freew.image-bring-to-front", "Bring to Front");
                            group.Button("freew.image-send-to-back", "Send to Back");
                        }));

                topology.Section(
                    "smartart.size",
                    tab => tab.Group("smartart-size", "Size", null, 70, group =>
                        {
                            group.ComboBox("freew.shape-width", "Width", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                            group.ComboBox("freew.shape-height", "Height", control => control with
                            {
                                Items = FloatSizes,
                                Width = 72
                            });
                        }));

                topology.Build();
            });

    internal static RibbonDefinitionBuilder AddTableContextualTabs(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        AddTableDesignTopology(builder, capabilities);
        return AddTableLayoutTopology(builder, capabilities);
    }

    private static RibbonDefinitionBuilder AddTableDesignTopology(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("table-design", "Table Design",
            new RibbonTabContext(capabilities.TableContextKey, "Table Tools", RibbonContextColor.Teal), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "table.style-options",
                    tab => tab.Group("table-style-options", "Table Style Options", "O", 100, group =>
                        {
                            group.Medium("freew.table-header-row", "Header Row", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-last-row", "Last Row", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.RowBreak();
                            group.Medium("freew.table-first-column", "First Column", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-last-column", "Last Column", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.RowBreak();
                            group.Medium("freew.table-banded-rows", "Banded Rows", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-banded-cols", "Banded Columns", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                        }),
                    tab => tab.Group("table-style-options", "Table Style Options", null, 100, group =>
                        {
                            group.Toggle("freew.table-header-row", "Header Row");
                            group.Toggle("freew.table-last-row", "Last Row");
                            group.Toggle("freew.table-first-column", "First Column");
                            group.Toggle("freew.table-last-column", "Last Column");
                            group.Toggle("freew.table-banded-rows", "Banded Rows");
                            group.Toggle("freew.table-banded-cols", "Banded Columns");
                        }));

                topology.Section(
                    "table.styles",
                    tab => tab.Group("table-style", "Table Style", "Y", 80, group =>
                        {
                            group.Medium("freew.table-shading", "Shading", RibbonCommandIconKind.Fill,
                                accent: RibbonCommandIconAccent.Fill);
                            group.Medium("freew.table-borders", "Borders", RibbonCommandIconKind.Grid);
                        }),
                    tab =>
                    {
                        // Keep the gallery as its own adaptive group. Avalonia hosts a thumbnail
                        // picker here; Shading and Borders remain independently reachable beside it.
                        tab.Group("table-styles", "Table Styles", null, 90, group =>
                            group.Dropdown("freew.table-styles", "Table Styles", BuildTableStylesMenu()));
                        tab.Group("table-style", "Table Style", null, 70, group =>
                        {
                            group.Button("freew.table-shading", "Shading");
                            group.Dropdown("freew.table-borders", "Borders", BuildTableBordersMenu());
                        });
                    });

                topology.Section(
                    "table.borders",
                    tab => tab.Group("draw-borders", "Draw Borders", "D", 60, group =>
                        {
                            group.Medium("freew.draw-table", "Draw Table", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Border);
                            group.Medium("freew.eraser", "Eraser", RibbonCommandIconKind.Clear);
                        }),
                    tab => tab.Group("draw-borders", "Draw Borders", null, 80, group =>
                        {
                            group.Button("freew.draw-table", "Draw Table", button => button with
                            {
                                PreferredLayout = RibbonCommandLayoutKind.Medium,
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Border)
                            });
                            group.Button("freew.eraser", "Eraser", button => button with
                            {
                                PreferredLayout = RibbonCommandLayoutKind.Medium,
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear)
                            });
                        }));

                topology.Build();
            });

    private static RibbonDefinitionBuilder AddTableLayoutTopology(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab("table-layout", "Table Layout",
            new RibbonTabContext(capabilities.TableContextKey, "Table Tools", RibbonContextColor.Teal), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "table.select",
                    tab => tab.Group("table-table", "Table", "T", 70, group =>
                        {
                            group.Medium("freew.table-select-table", "Select Table", RibbonCommandIconKind.Table);
                            group.Medium("freew.table-select-row", "Select Row", RibbonCommandIconKind.Table);
                            group.RowBreak();
                            group.Medium("freew.table-select-col", "Select Column", RibbonCommandIconKind.Table);
                            group.Medium("freew.table-select-cell", "Select Cell", RibbonCommandIconKind.Table);
                            group.RowBreak();
                            group.Medium("freew.table-view-gridlines", "View Gridlines", RibbonCommandIconKind.Grid);
                            group.Medium("freew.table-properties", "Properties", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                        }),
                    tab => tab.Group("table-select", "Table", null, 110, group =>
                        {
                            group.Button("freew.table-select-table", "Select Table");
                            group.Button("freew.table-select-row", "Select Row");
                            group.Button("freew.table-select-col", "Select Column");
                            group.Button("freew.table-select-cell", "Select Cell");
                            group.Toggle("freew.table-view-gridlines", "View Gridlines");
                            group.Button("freew.table-properties", "Properties");
                        }));

                topology.Section(
                    "table.rows-columns",
                    tab => tab.Group("table-rows-cols", "Rows & Columns", "R", 120, group =>
                        {
                            group.Medium("freew.table-insert-above", "Insert Above", RibbonCommandIconKind.Insert,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-insert-below", "Insert Below", RibbonCommandIconKind.Insert,
                                accent: RibbonCommandIconAccent.Green);
                            group.RowBreak();
                            group.Medium("freew.table-insert-col-left", "Insert Left", RibbonCommandIconKind.Insert,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-insert-col-right", "Insert Right", RibbonCommandIconKind.Insert,
                                accent: RibbonCommandIconAccent.Green);
                            group.RowBreak();
                            group.Medium("freew.table-delete-row", "Delete Rows", RibbonCommandIconKind.Delete);
                            group.Medium("freew.table-delete-col", "Delete Columns", RibbonCommandIconKind.Delete);
                            group.RowBreak();
                            group.Medium("freew.table-delete", "Delete Table", RibbonCommandIconKind.Delete);
                        }),
                    tab => tab.Group("table-rows-cols", "Rows & Columns", null, 100, group =>
                        {
                            group.Button("freew.table-insert-above", "Insert Above");
                            group.Button("freew.table-insert-below", "Insert Below");
                            group.Button("freew.table-insert-col-left", "Insert Left");
                            group.Button("freew.table-insert-col-right", "Insert Right");
                            group.Button("freew.table-delete-row", "Delete Row");
                            group.Button("freew.table-delete-col", "Delete Column");
                            group.Button("freew.table-delete", "Delete Table");
                        }));

                topology.Section(
                    "table.merge",
                    tab => tab.Group("table-merge", "Merge", "M", 90, group =>
                        {
                            group.Medium("freew.table-merge-cells", "Merge Cells", RibbonCommandIconKind.Merge);
                            group.Medium("freew.table-split-cell", "Split Cell", RibbonCommandIconKind.Grid);
                            group.RowBreak();
                            group.Medium("freew.split-table", "Split Table", RibbonCommandIconKind.Grid);
                        }),
                    tab => tab.Group("table-merge", "Merge", null, 90, group =>
                        {
                            group.Button("freew.table-merge-cells", "Merge Cells");
                            group.Button("freew.table-split-cell", "Split Cell");
                            group.Button("freew.split-table", "Split Table");
                        }));

                topology.Section(
                    "table.cell-size",
                    tab => tab.Group("table-cell-size", "Cell Size", "Z", 100, group =>
                        {
                            group.Medium("freew.table-row-height", "Row Height", RibbonCommandIconKind.Size);
                            group.Medium("freew.table-col-width", "Column Width", RibbonCommandIconKind.Size);
                            group.RowBreak();
                            group.Medium("freew.table-distribute-rows", "Distribute Rows", RibbonCommandIconKind.Grid);
                            group.Medium("freew.table-distribute-cols", "Distribute Columns", RibbonCommandIconKind.Grid);
                            group.RowBreak();
                            group.Medium("freew.table-autofit-contents", "AutoFit Contents", RibbonCommandIconKind.Scale);
                            group.Medium("freew.table-autofit-window", "AutoFit Window", RibbonCommandIconKind.Scale);
                            group.Medium("freew.table-autofit-fixed", "Fixed Column Width", RibbonCommandIconKind.Size);
                        }),
                    tab => tab.Group("table-cell-size", "Cell Size", null, 95, group =>
                        {
                            group.Button("freew.table-row-height", "Row Height");
                            group.Button("freew.table-col-width", "Column Width");
                            group.Button("freew.table-distribute-rows", "Distribute Rows");
                            group.Button("freew.table-distribute-cols", "Distribute Columns");
                            group.Button("freew.table-autofit-contents", "AutoFit Contents");
                            group.Button("freew.table-autofit-window", "AutoFit Window");
                            group.Button("freew.table-autofit-fixed", "Fixed Column Width");
                        }));

                topology.Section(
                    "table.alignment",
                    tab => tab.Group("table-alignment", "Alignment", "A", 110, group =>
                        {
                            group.Medium("freew.cell-align-top-left", "Top Left", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.cell-align-top-center", "Top Center", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.cell-align-top-right", "Top Right", RibbonCommandIconKind.AlignRight);
                            group.RowBreak();
                            group.Medium("freew.cell-align-middle-left", "Middle Left", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.cell-align-middle-center", "Middle Center", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.cell-align-middle-right", "Middle Right", RibbonCommandIconKind.AlignRight);
                            group.RowBreak();
                            group.Medium("freew.cell-align-bottom-left", "Bottom Left", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.cell-align-bottom-center", "Bottom Center", RibbonCommandIconKind.AlignCenter);
                            group.Medium("freew.cell-align-bottom-right", "Bottom Right", RibbonCommandIconKind.AlignRight);
                            group.RowBreak();
                            group.Medium("freew.table-cell-margins", "Cell Margins", RibbonCommandIconKind.Margins);
                            group.RowBreak();
                            group.Medium("freew.cell-text-direction-horizontal", "Horizontal", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.cell-text-direction-rotate90", "Rotate Text Up", RibbonCommandIconKind.AlignLeft);
                            group.Medium("freew.cell-text-direction-rotate270", "Rotate Text Down", RibbonCommandIconKind.AlignLeft);
                        }),
                    tab => tab.Group("table-alignment", "Alignment", null, 110, group =>
                        {
                            group.Button("freew.cell-align-top-left", "Top Left");
                            group.Button("freew.cell-align-top-center", "Top Center");
                            group.Button("freew.cell-align-top-right", "Top Right");
                            group.Button("freew.cell-align-middle-left", "Middle Left");
                            group.Button("freew.cell-align-middle-center", "Middle Center");
                            group.Button("freew.cell-align-middle-right", "Middle Right");
                            group.Button("freew.cell-align-bottom-left", "Bottom Left");
                            group.Button("freew.cell-align-bottom-center", "Bottom Center");
                            group.Button("freew.cell-align-bottom-right", "Bottom Right");
                            group.Button("freew.table-cell-margins", "Cell Margins");
                            group.Button("freew.cell-text-direction-horizontal", "Horizontal");
                            group.Button("freew.cell-text-direction-rotate90", "Rotate Text Up");
                            group.Button("freew.cell-text-direction-rotate270", "Rotate Text Down");
                        }));

                topology.Section(
                    "table.data",
                    tab => tab.Group("table-data", "Data", "D", 70, group =>
                        {
                            group.Medium("freew.table-repeat-header", "Repeat Header Row", RibbonCommandIconKind.Table,
                                accent: RibbonCommandIconAccent.Green);
                            group.Medium("freew.table-formula", "Formula", RibbonCommandIconKind.Sum,
                                accent: RibbonCommandIconAccent.Green);
                            group.RowBreak();
                            group.Medium("freew.sort", "Sort", RibbonCommandIconKind.Sort);
                            group.Medium("freew.table-to-text", "Convert to Text", RibbonCommandIconKind.TextFunction);
                        }),
                    tab => tab.Group("table-data", "Data", null, 80, group =>
                        {
                            group.Toggle("freew.table-repeat-header", "Repeat Header Row");
                            group.Button("freew.table-formula", "Formula");
                            group.Button("freew.sort", "Sort");
                            group.Button("freew.table-to-text", "Convert to Text");
                        }));

                topology.Build();
            });

    private static readonly string[] FloatSizes = FreeWRibbonDefinitionData.FloatSizes;

    private static void BuildWordArtStyleMenu(RibbonMenuBuilder menu)
    {
        for (var index = 0; index < WordArtRibbonWorkflow.StylePresets.Count; index++)
        {
            if (index == 4)
                menu.Separator();
            var preset = WordArtRibbonWorkflow.StylePresets[index];
            menu.Item(preset.CommandId.Value, preset.Label, preset.KeyTip);
        }
    }

    private static void BuildWordArtWarpMenu(RibbonMenuBuilder menu)
    {
        for (var index = 0; index < WordArtRibbonWorkflow.WarpPresets.Count; index++)
        {
            if (index == 1)
                menu.Separator();
            var preset = WordArtRibbonWorkflow.WarpPresets[index];
            menu.Item(preset.CommandId.Value, preset.Label, preset.KeyTip);
        }
    }

    private static RibbonMenu BuildWrapMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("In Line with Text", new RibbonCommandId($"freew.{prefix}-wrap-inline")),
            new("Square", new RibbonCommandId($"freew.{prefix}-wrap-square")),
            new("Tight", new RibbonCommandId($"freew.{prefix}-wrap-tight")),
            new("Top and Bottom", new RibbonCommandId($"freew.{prefix}-wrap-top-bottom")),
            new("Behind Text", new RibbonCommandId($"freew.{prefix}-wrap-behind")),
            new("In Front of Text", new RibbonCommandId($"freew.{prefix}-wrap-front")),
        });

    private static RibbonMenu BuildRotateMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("Rotate Right 90\u00B0", new RibbonCommandId($"freew.{prefix}-rotate-right90")),
            new("Rotate Left 90\u00B0", new RibbonCommandId($"freew.{prefix}-rotate-left90")),
            RibbonMenuItem.Separator(),
            new("Flip Vertical", new RibbonCommandId($"freew.{prefix}-flip-vertical")),
            new("Flip Horizontal", new RibbonCommandId($"freew.{prefix}-flip-horizontal")),
        });

    private static RibbonMenu BuildFloatingPositionMenu(string prefix) =>
        new(FreeWRibbonDefinitionData.FloatingPositionPresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.{prefix}-position-{preset.Suffix}")))
            .Concat(prefix == "image"
                ? [RibbonMenuItem.Separator(), new RibbonMenuItem("More Layout Options...", new RibbonCommandId($"freew.{prefix}-position"))]
                : [])
            .ToArray());

    private static RibbonMenu BuildPictureCorrectionsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Brightness: +20%", new RibbonCommandId("freew.image-brightness-plus20"), "1"),
            new("Brightness: +40%", new RibbonCommandId("freew.image-brightness-plus40"), "2"),
            new("Brightness: -20%", new RibbonCommandId("freew.image-brightness-minus20"), "3"),
            new("Brightness: -40%", new RibbonCommandId("freew.image-brightness-minus40"), "4"),
            new("Contrast: +20%", new RibbonCommandId("freew.image-contrast-plus20"), "5"),
            new("Contrast: -20%", new RibbonCommandId("freew.image-contrast-minus20"), "6"),
            new("Picture Corrections\u2026", new RibbonCommandId("freew.image-adjust-dialog"), "D"),
        });

    private static RibbonMenu BuildPictureColorMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Saturation: 0% (Greyscale)", new RibbonCommandId("freew.image-saturation-0"), "G"),
            new("Saturation: 50%", new RibbonCommandId("freew.image-saturation-50"), "H"),
            new("Saturation: 200%", new RibbonCommandId("freew.image-saturation-200"), "J"),
            new("Color\u2026", new RibbonCommandId("freew.image-color-dialog"), "C"),
            RibbonMenuItem.Separator(),
            new("Recolor: Grayscale", new RibbonCommandId("freew.image-recolor-grayscale"), "1"),
            new("Recolor: Sepia", new RibbonCommandId("freew.image-recolor-sepia"), "2"),
            new("Recolor: Washout", new RibbonCommandId("freew.image-recolor-washout"), "3"),
            new("Recolor: Black and White", new RibbonCommandId("freew.image-recolor-blackwhite"), "4"),
            new("Recolor: No Recolor", new RibbonCommandId("freew.image-recolor-none"), "N"),
            RibbonMenuItem.Separator(),
            new("Color Tone: Warm (3000K)", new RibbonCommandId("freew.image-colortemp-warm"), "W"),
            new("Color Tone: Cool (8000K)", new RibbonCommandId("freew.image-colortemp-cool"), "L"),
            new("Color Tone: Neutral", new RibbonCommandId("freew.image-colortemp-neutral"), "T"),
        });

    private static RibbonMenu BuildPictureTransparencyMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Transparency: 25%", new RibbonCommandId("freew.image-transparency-25"), "A"),
            new("Transparency: 50%", new RibbonCommandId("freew.image-transparency-50"), "B"),
            new("Transparency: 75%", new RibbonCommandId("freew.image-transparency-75"), "C"),
            new("Transparency\u2026", new RibbonCommandId("freew.image-transparency-dialog"), "D"),
        });

    private static RibbonMenu BuildPictureEffectsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Shadow: No Shadow", new RibbonCommandId("freew.image-shadow-none"), "N"),
            new("Shadow: Offset Diagonal", new RibbonCommandId("freew.image-shadow-1"), "1"),
            new("Shadow: Offset Diagonal Medium", new RibbonCommandId("freew.image-shadow-2"), "2"),
            new("Shadow: Perspective", new RibbonCommandId("freew.image-shadow-3"), "3"),
            new("Shadow: Offset Bottom", new RibbonCommandId("freew.image-shadow-4"), "4"),
            new("Shadow: Large", new RibbonCommandId("freew.image-shadow-5"), "5"),
            RibbonMenuItem.Separator(),
            new("Reflection: No Reflection", new RibbonCommandId("freew.image-reflection-none"), "R"),
            new("Reflection: Tight, Touching", new RibbonCommandId("freew.image-reflection-1"), "A"),
            new("Reflection: Tight, 4pt", new RibbonCommandId("freew.image-reflection-2"), "B"),
            new("Reflection: Tight, 8pt", new RibbonCommandId("freew.image-reflection-3"), "C"),
            new("Reflection: Half, Touching", new RibbonCommandId("freew.image-reflection-4"), "D"),
            new("Reflection: Half, 4pt", new RibbonCommandId("freew.image-reflection-5"), "E"),
            RibbonMenuItem.Separator(),
            new("Glow: No Glow", new RibbonCommandId("freew.image-glow-none"), "G"),
            new("Glow: 5 pt", new RibbonCommandId("freew.image-glow-5"), "H"),
            new("Glow: 8 pt", new RibbonCommandId("freew.image-glow-8"), "I"),
            new("Glow: 11 pt", new RibbonCommandId("freew.image-glow-11"), "J"),
            new("Glow: 18 pt", new RibbonCommandId("freew.image-glow-18"), "K"),
            RibbonMenuItem.Separator(),
            new("Soft Edges: None", new RibbonCommandId("freew.image-softedge-none"), "S"),
            new("Soft Edges: 1 pt", new RibbonCommandId("freew.image-softedge-1"), "T"),
            new("Soft Edges: 2.5 pt", new RibbonCommandId("freew.image-softedge-2pt5"), "U"),
            new("Soft Edges: 5 pt", new RibbonCommandId("freew.image-softedge-5"), "V"),
            new("Soft Edges: 10 pt", new RibbonCommandId("freew.image-softedge-10"), "X"),
            RibbonMenuItem.Separator(),
            new("Bevel: No Bevel", new RibbonCommandId("freew.image-bevel-none"), "O"),
            new("Bevel: Circle", new RibbonCommandId("freew.image-bevel-1"), "P"),
            new("Bevel: Relaxed Inset", new RibbonCommandId("freew.image-bevel-2"), "Q"),
            new("Bevel: Cross", new RibbonCommandId("freew.image-bevel-3"), "F"),
            new("Bevel: Cool Slant", new RibbonCommandId("freew.image-bevel-4"), "M"),
        });

    private static RibbonMenu BuildPictureArtisticEffectsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("No Artistic Effect", new RibbonCommandId("freew.image-artistic-none"), "N"),
            new("Blur", new RibbonCommandId("freew.image-artistic-blur"), "B"),
            new("Glow Diffused", new RibbonCommandId("freew.image-artistic-glow-diffused"), "G"),
            new("Glow Edges", new RibbonCommandId("freew.image-artistic-glow-edges"), "E"),
            new("Pencil Grayscale", new RibbonCommandId("freew.image-artistic-pencil-gray"), "A"),
            new("Pencil Sketch", new RibbonCommandId("freew.image-artistic-pencil-sketch"), "K"),
            new("Line Drawing", new RibbonCommandId("freew.image-artistic-line-drawing"), "L"),
            new("Paint Brush", new RibbonCommandId("freew.image-artistic-paintbrush"), "P"),
            new("Paint Strokes", new RibbonCommandId("freew.image-artistic-paint-strokes"), "T"),
            new("Photocopy", new RibbonCommandId("freew.image-artistic-photocopy"), "H"),
            new("Posterize", new RibbonCommandId("freew.image-artistic-posterize"), "O"),
            new("Pastels", new RibbonCommandId("freew.image-artistic-pastels"), "S"),
            new("Watercolor Sponge", new RibbonCommandId("freew.image-artistic-watercolor"), "W"),
            new("Film Grain", new RibbonCommandId("freew.image-artistic-film-grain"), "F"),
            new("Mosaic Bubbles", new RibbonCommandId("freew.image-artistic-mosaic"), "M"),
        });

    private static RibbonMenu BuildShapeSizeMenu() =>
        new(FreeWRibbonDefinitionData.FloatingSizePresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.shape-size-{preset.Suffix}")))
            .ToArray());

    private static RibbonMenu BuildShapeAltTextMenu() =>
        new(FreeWRibbonDefinitionData.ShapeAltTextPresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.shape-alt-text-{preset.Suffix}")))
            .ToArray());

    private static RibbonMenu BuildShapeStylesMenu() =>
        new(ShapeStylePreset.Catalog
            .Select(preset => new RibbonMenuItem(
                preset.Name,
                new RibbonCommandId($"freew.{preset.Id}")))
            .ToArray());

    private static RibbonMenu BuildShapeChangeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Rectangle", new RibbonCommandId("freew.shape-change-rectangle")),
            new("Rounded Rectangle", new RibbonCommandId("freew.shape-change-rounded")),
            new("Ellipse", new RibbonCommandId("freew.shape-change-ellipse")),
        });

    private static RibbonMenu BuildShapeEditMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Convert to Freeform", new RibbonCommandId("freew.shape-convert-freeform")),
            new("Edit Points", new RibbonCommandId("freew.shape-edit-points")),
        });

    private static RibbonMenu BuildShapeFillMenu() =>
        new(new RibbonMenuItem[]
        {
            new("No Fill", new RibbonCommandId("freew.shape-fill-no-fill")),
            RibbonMenuItem.Separator(),
            new("Gradient Blue", new RibbonCommandId("freew.shape-fill-gradient-blue")),
            new("Gradient Orange", new RibbonCommandId("freew.shape-fill-gradient-orange")),
            new("Pattern Diagonal", new RibbonCommandId("freew.shape-fill-pattern-diag")),
        });

    private static RibbonMenu BuildShapeOutlineMenu() =>
        new(new RibbonMenuItem[]
        {
            new("No Outline", new RibbonCommandId("freew.shape-outline-no-outline")),
            new("Solid", new RibbonCommandId("freew.shape-outline-solid")),
            new("Dash", new RibbonCommandId("freew.shape-outline-dash")),
            new("Dot", new RibbonCommandId("freew.shape-outline-dot")),
        });

    private static RibbonMenu BuildShapeEffectsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("None", new RibbonCommandId("freew.shape-effects-none")),
            RibbonMenuItem.Separator(),
            new("Shadow", new RibbonCommandId("freew.shape-effect-shadow")),
            new("Glow", new RibbonCommandId("freew.shape-effect-glow")),
            new("Soft Edges", new RibbonCommandId("freew.shape-effect-soft-edge")),
            new("Reflection", new RibbonCommandId("freew.shape-effect-reflection")),
            new("Bevel", new RibbonCommandId("freew.shape-effect-bevel")),
        });

    private static RibbonMenu BuildShapeTextDirectionMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Horizontal", new RibbonCommandId("freew.shape-text-horizontal")),
            new("Rotate 90\u00B0", new RibbonCommandId("freew.shape-text-rotate90")),
            new("Rotate 270\u00B0", new RibbonCommandId("freew.shape-text-rotate270")),
        });

    private static RibbonMenu BuildChartTypeMenu() =>
        new(Enum.GetValues<ChartKind>()
            .Select(kind => new RibbonMenuItem(
                kind.ToString(),
                new RibbonCommandId($"freew.chart-type-{kind.ToString().ToLowerInvariant()}")))
            .ToArray());

    private static RibbonMenu BuildChartStyleMenu() =>
        new(ChartStyle.Catalog
            .Select(style => new RibbonMenuItem(
                style.Name,
                new RibbonCommandId($"freew.chart-style-{style.Id}")))
            .ToArray());

    private static RibbonMenu BuildChartColorsMenu() =>
        new(ChartColorScheme.Catalog
            .Select(scheme => new RibbonMenuItem(
                scheme.Name,
                new RibbonCommandId(ChartColorRibbonCommandCatalog.CommandId(scheme))))
            .ToArray());

    private static RibbonMenu BuildSmartArtLayoutMenu() =>
        new(SmartArtLayoutPreset.Catalog
            .Select(preset => new RibbonMenuItem(
                preset.Name,
                new RibbonCommandId($"freew.smartart-layout-{preset.Id}")))
            .ToArray());

    private static RibbonMenu BuildSmartArtColorsMenu() =>
        new(SmartArtColorScheme.Catalog
            .Select(scheme => new RibbonMenuItem(
                scheme.Name,
                new RibbonCommandId($"freew.smartart-colors-{scheme.Id}")))
            .ToArray());

    private static RibbonMenu BuildSmartArtStylesMenu() =>
        new(SmartArtStyle.Catalog
            .Select(style => new RibbonMenuItem(
                style.Name,
                SmartArtCommandPlanner.StyleCommandId(style)))
            .ToArray());

    private static RibbonMenu BuildTableBordersMenu() =>
        new(new RibbonMenuItem[]
        {
            new("All Borders", new RibbonCommandId("freew.table-borders.all")),
            new("Outside Borders", new RibbonCommandId("freew.table-borders.outside")),
            new("Inside Borders", new RibbonCommandId("freew.table-borders.inside")),
            new("No Border", new RibbonCommandId("freew.table-borders.none")),
            RibbonMenuItem.Separator(),
            new("Top Border", new RibbonCommandId("freew.table-borders.top")),
            new("Bottom Border", new RibbonCommandId("freew.table-borders.bottom")),
            new("Left Border", new RibbonCommandId("freew.table-borders.left")),
            new("Right Border", new RibbonCommandId("freew.table-borders.right")),
        });

    private static RibbonMenu BuildTableStylesMenu() => FreeWContextMenuPlanner.BuildTableStyles();
}
