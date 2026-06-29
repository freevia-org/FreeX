using Free.Shared.Ribbon;

namespace FreeP.Ribbon.Definitions;

/// <summary>
/// FreeP's minimal PowerPoint-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX and FreeW, proving the ribbon library is app-neutral.
///
/// Tabs: Home, Insert (Wave 3 + 5B), Design (Wave 5B), Transitions, Animations, Slide Show (Wave 4C).
/// Wave 12A: Arrange group added to the Home tab (Group/Ungroup, z-order, Align).
/// </summary>
public static class FreePRibbon
{
    public static RibbonDefinition Build(FreePRibbonCapabilities? capabilities = null)
    {
        capabilities ??= FreePRibbonCapabilities.Wpf;
        if (capabilities.UseAvaloniaBackedSurface)
            return FreePAvaloniaRibbonDefinition.Build();

        return new RibbonDefinitionBuilder()
            .Tab("home", FreePRibbonText.HomeTabLabel, FreePRibbonText.HomeTabKeyTip, tab =>
            {
                tab.Group("slides", FreePRibbonText.SlidesGroupLabel, FreePRibbonText.SlidesGroupKeyTip, 100, g =>
                {
                    // New Slide is the hero; the rest are compact stubs, mirroring PowerPoint's Slides group.
                    g.Large("freep.new-slide", FreePRibbonText.NewSlideLabel, RibbonCommandIconKind.Insert, FreePRibbonText.NewSlideKeyTip);
                    g.Medium("freep.duplicate-slide", FreePRibbonText.DuplicateSlideLabel, RibbonCommandIconKind.Copy, FreePRibbonText.DuplicateSlideKeyTip);
                    g.Medium("freep.delete-slide", FreePRibbonText.DeleteSlideLabel, RibbonCommandIconKind.Delete, FreePRibbonText.DeleteSlideKeyTip);
                    g.Medium("freep.layout", FreePRibbonText.LayoutLabel, RibbonCommandIconKind.Grid, FreePRibbonText.LayoutKeyTip);
                });
                tab.Group("clipboard", FreePRibbonText.ClipboardGroupLabel, FreePRibbonText.ClipboardGroupKeyTip, 90, g =>
                {
                    g.Large("freep.paste", FreePRibbonText.PasteLabel, RibbonCommandIconKind.Paste, FreePRibbonText.PasteKeyTip);
                    g.Medium("freep.cut", FreePRibbonText.CutLabel, RibbonCommandIconKind.Cut, FreePRibbonText.CutKeyTip);
                    g.Medium("freep.copy", FreePRibbonText.CopyLabel, RibbonCommandIconKind.Copy, FreePRibbonText.CopyKeyTip);
                    // Wave 5B: Format Painter — copies formatting from first selected shape to rest of selection.
                    g.Medium("freep.format-painter", FreePRibbonText.FormatPainterLabel, RibbonCommandIconKind.FormatPainter, FreePRibbonText.FormatPainterKeyTip);
                });
                tab.Group("font", FreePRibbonText.FontGroupLabel, FreePRibbonText.FontGroupKeyTip, 80, g =>
                {
                    g.ComboBox("freep.font-family", FreePRibbonText.FontFamilyLabel, c => c with
                    {
                        Items = FreePRibbonDefinitionData.FontFamilies,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.IconToggle("freep.bold", FreePRibbonText.BoldLabel, RibbonCommandIconKind.Bold, FreePRibbonText.BoldKeyTip);
                    g.IconToggle("freep.italic", FreePRibbonText.ItalicLabel, RibbonCommandIconKind.Italic, FreePRibbonText.ItalicKeyTip);
                    g.IconToggle("freep.underline", FreePRibbonText.UnderlineLabel, RibbonCommandIconKind.Underline, FreePRibbonText.UnderlineKeyTip);
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
                // Wave 12B: Editing group — Find & Replace.
                tab.Group("editing", FreePRibbonText.EditingGroupLabel, FreePRibbonText.EditingGroupKeyTip, 70, g =>
                {
                    g.Large("freep.find", FreePRibbonText.FindLabel, RibbonCommandIconKind.Search, FreePRibbonText.FindKeyTip);
                    g.Medium("freep.replace", FreePRibbonText.ReplaceLabel, RibbonCommandIconKind.Refresh, FreePRibbonText.ReplaceKeyTip);
                });
            })
            .Tab("insert", FreePRibbonText.InsertTabLabel, FreePRibbonText.InsertTabKeyTip, tab =>
            {
                tab.Group("text", FreePRibbonText.TextGroupLabel, FreePRibbonText.TextGroupKeyTip, 100, g =>
                {
                    g.Large("freep.text-box", FreePRibbonText.TextBoxLabel, RibbonCommandIconKind.TextBox, FreePRibbonText.TextBoxKeyTip);
                });
                // Wave 5B: Tables group — default 3×3; picker deferral noted.
                tab.Group("tables", FreePRibbonText.TablesGroupLabel, FreePRibbonText.TablesGroupKeyTip, 95, g =>
                {
                    g.Large("freep.insert-table-3x3", FreePRibbonText.InsertTable3x3Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable3x3KeyTip);
                    g.Medium("freep.insert-table-2x2", FreePRibbonText.InsertTable2x2Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable2x2KeyTip);
                    g.Medium("freep.insert-table-4x4", FreePRibbonText.InsertTable4x4Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable4x4KeyTip);
                    // NOTE: interactive row/col picker (hover-grid) is deferred to a later wave.
                });
                // Wave 5B: Charts group (9B: chart data editing button added).
                tab.Group("charts", FreePRibbonText.ChartsGroupLabel, FreePRibbonText.ChartsGroupKeyTip, 93, g =>
                {
                    g.Medium("freep.insert-chart-column", FreePRibbonText.InsertChartColumnLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartColumnKeyTip);
                    g.Medium("freep.insert-chart-bar",    FreePRibbonText.InsertChartBarLabel,    RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartBarKeyTip);
                    g.Medium("freep.insert-chart-line",   FreePRibbonText.InsertChartLineLabel,   RibbonCommandIconKind.ChartLine,   FreePRibbonText.InsertChartLineKeyTip);
                    g.Medium("freep.insert-chart-pie",    FreePRibbonText.InsertChartPieLabel,    RibbonCommandIconKind.ChartPie,    FreePRibbonText.InsertChartPieKeyTip);
                    // Wave 9B: Edit selected chart's data via grid dialog.
                    g.Medium("freep.chart.edit-data",     FreePRibbonText.ChartEditDataLabel,     RibbonCommandIconKind.ChartTitle,  FreePRibbonText.ChartEditDataKeyTip);
                });
                // Wave 11A: Links group — Insert / Remove hyperlink.
                tab.Group("links", FreePRibbonText.LinksGroupLabel, FreePRibbonText.LinksGroupKeyTip, 92, g =>
                {
                    g.Large("freep.insert-link", FreePRibbonText.InsertLinkLabel, RibbonCommandIconKind.Link, FreePRibbonText.InsertLinkKeyTip);
                    g.Medium("freep.remove-link", FreePRibbonText.RemoveLinkLabel, RibbonCommandIconKind.Delete, FreePRibbonText.RemoveLinkKeyTip);
                });
                tab.Group("illustrations", FreePRibbonText.IllustrationsGroupLabel, FreePRibbonText.IllustrationsGroupKeyTip, 90, g =>
                {
                    g.Large("freep.picture", FreePRibbonText.PictureLabel, RibbonCommandIconKind.Picture, FreePRibbonText.PictureKeyTip);
                    g.Medium("freep.shape-rectangle", FreePRibbonText.ShapeRectangleLabel, RibbonCommandIconKind.Rectangle, FreePRibbonText.ShapeRectangleKeyTip);
                    g.Medium("freep.shape-ellipse", FreePRibbonText.ShapeEllipseLabel, RibbonCommandIconKind.Ellipse, FreePRibbonText.ShapeEllipseKeyTip);
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
                        Items = FreePRibbonDefinitionData.TransitionDurations,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.MediumToggle("freep.transition.advance-on-click", "On Mouse Click",
                        RibbonCommandIconKind.Next, "M");
                    g.ComboBox("freep.transition.advance-after", "After", c => c with
                    {
                        Items = FreePRibbonDefinitionData.TransitionAdvanceAfterOptions,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.transition.apply-all", "Apply To All",
                        RibbonCommandIconKind.Refresh, "A");
                });

                // Slide Show buttons live here for quick access from the Transitions tab.
                tab.Group("slideshow-from-transitions", FreePRibbonText.SlideShowGroupLabel, FreePRibbonText.SlideShowGroupWpfKeyTip, 80, g =>
                {
                    g.Large("freep.slideshow.from-beginning",     FreePRibbonText.SlideShowFromBeginningLabel,     RibbonCommandIconKind.Next,     FreePRibbonText.SlideShowFromBeginningKeyTip);
                    g.Large("freep.slideshow.from-current-slide", FreePRibbonText.SlideShowFromCurrentSlideLabel, RibbonCommandIconKind.Previous, FreePRibbonText.SlideShowFromCurrentSlideKeyTip);
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
                        Items = FreePRibbonDefinitionData.AnimationTriggers,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                        Width = 130
                    });
                    g.ComboBox("freep.anim.duration", "Duration", c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDurations,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.ComboBox("freep.anim.delay", "Delay", c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDelays,
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
