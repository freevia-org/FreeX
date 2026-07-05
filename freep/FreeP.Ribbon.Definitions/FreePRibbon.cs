using Free.Shared.Ribbon;
using FreeP.App.Compositor;

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
                    g.ComboBox("freep.font-size", FreePRibbonText.FontSizeLabel, c => c with
                    {
                        Items = FreePRibbonDefinitionData.FontSizes,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size),
                        Width = 64
                    });
                    g.ComboBox("freep.font-color", FreePRibbonText.FontColorLabel, c => c with
                    {
                        Items = FreePRibbonDefinitionData.FontColors,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.FontColor, RibbonCommandIconAccent.Color),
                        Width = 96
                    });
                    g.IconToggle("freep.bold", FreePRibbonText.BoldLabel, RibbonCommandIconKind.Bold, FreePRibbonText.BoldKeyTip);
                    g.IconToggle("freep.italic", FreePRibbonText.ItalicLabel, RibbonCommandIconKind.Italic, FreePRibbonText.ItalicKeyTip);
                    g.IconToggle("freep.underline", FreePRibbonText.UnderlineLabel, RibbonCommandIconKind.Underline, FreePRibbonText.UnderlineKeyTip);
                });
                // ── Wave 12A: Arrange group ───────────────────────────────────────────────
                tab.Group("paragraph", FreePRibbonText.ParagraphGroup.Label, FreePRibbonText.ParagraphGroup.KeyTip, 78, AddParagraphControls);
                tab.Group("arrange", FreePRibbonText.ArrangeGroup.Label, FreePRibbonText.ArrangeGroup.KeyTip, 70, g =>
                {
                    // Group / Ungroup
                    g.Large("freep.arrange.group", FreePRibbonText.ArrangeGroupCommand.Label, RibbonCommandIconKind.Group, FreePRibbonText.ArrangeGroupCommand.KeyTip);
                    g.Medium("freep.arrange.ungroup", FreePRibbonText.ArrangeUngroupCommand.Label, RibbonCommandIconKind.Ungroup, FreePRibbonText.ArrangeUngroupCommand.KeyTip);
                    g.Separator();
                    // Z-order
                    g.Medium("freep.arrange.bring-to-front", FreePRibbonText.ArrangeBringToFrontCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringToFrontCommand.KeyTip);
                    g.Medium("freep.arrange.bring-forward", FreePRibbonText.ArrangeBringForwardCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringForwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-backward", FreePRibbonText.ArrangeSendBackwardCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendBackwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-to-back", FreePRibbonText.ArrangeSendToBackCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendToBackCommand.KeyTip);
                    g.Separator();
                    // Align (six buttons — vertical reuse arrow/effects icons as fallback)
                    g.Medium("freep.arrange.align-left", FreePRibbonText.ArrangeAlignLeftCommand.Label, RibbonCommandIconKind.AlignLeft, FreePRibbonText.ArrangeAlignLeftCommand.KeyTip);
                    g.Medium("freep.arrange.align-center-h", FreePRibbonText.ArrangeAlignCenterHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeAlignCenterHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.align-right", FreePRibbonText.ArrangeAlignRightCommand.Label, RibbonCommandIconKind.AlignRight, FreePRibbonText.ArrangeAlignRightCommand.KeyTip);
                    g.Medium("freep.arrange.align-top", FreePRibbonText.ArrangeAlignTopCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeAlignTopCommand.KeyTip);
                    g.Medium("freep.arrange.align-middle", FreePRibbonText.ArrangeAlignMiddleCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeAlignMiddleCommand.KeyTip);
                    g.Medium("freep.arrange.align-bottom", FreePRibbonText.ArrangeAlignBottomCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeAlignBottomCommand.KeyTip);
                    g.Separator();
                    // Distribute
                    g.Medium("freep.arrange.distribute-h", FreePRibbonText.ArrangeDistributeHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeDistributeHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.distribute-v", FreePRibbonText.ArrangeDistributeVerticalCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeDistributeVerticalCommand.KeyTip);
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
                    g.Medium("freep.header-footer", FreePRibbonText.HeaderFooterLabel, RibbonCommandIconKind.HeaderFooter, FreePRibbonText.HeaderFooterKeyTip);
                    g.Medium("freep.date-time", FreePRibbonText.DateTimeLabel, RibbonCommandIconKind.Date, FreePRibbonText.DateTimeKeyTip);
                    g.Medium("freep.slide-number", FreePRibbonText.SlideNumberLabel, RibbonCommandIconKind.PageNumber, FreePRibbonText.SlideNumberKeyTip);
                });
                // Tables group: the large Table command opens the host picker; compact fixed-size shortcuts remain direct.
                tab.Group("tables", FreePRibbonText.TablesGroupLabel, FreePRibbonText.TablesGroupKeyTip, 95, g =>
                {
                    g.Large("freep.insert-table-3x3", FreePRibbonText.InsertTable3x3Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable3x3KeyTip);
                    g.Medium("freep.insert-table-2x2", FreePRibbonText.InsertTable2x2Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable2x2KeyTip);
                    g.Medium("freep.insert-table-4x4", FreePRibbonText.InsertTable4x4Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable4x4KeyTip);
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
            .Tab("design", FreePRibbonText.DesignTab.Label, FreePRibbonText.DesignTab.KeyTip, tab =>
            {
                // Themes group — one button per built-in theme.
                tab.Group("themes", FreePRibbonText.ThemesGroup.Label, FreePRibbonText.ThemesGroup.KeyTip, 100, g =>
                {
                    g.Large("freep.theme.office", FreePRibbonText.ThemeOfficeCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeOfficeCommand.KeyTip);
                    g.Medium("freep.theme.berlin", FreePRibbonText.ThemeBerlinCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeBerlinCommand.KeyTip);
                    g.Medium("freep.theme.facet", FreePRibbonText.ThemeFacetCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeFacetCommand.KeyTip);
                    g.Medium("freep.theme.ion", FreePRibbonText.ThemeIonCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeIonCommand.KeyTip);
                    g.Medium("freep.theme.slice", FreePRibbonText.ThemeSliceCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeSliceCommand.KeyTip);
                });
                // Customize group — slide size options.
                // Wave 10B: "Slide Size…" button opens the custom-size dialog.
                tab.Group("customize", FreePRibbonText.CustomizeGroup.Label, FreePRibbonText.CustomizeGroup.KeyTip, 90, g =>
                {
                    g.Large("freep.slide-size-16x9", FreePRibbonText.SlideSizeWidescreenCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeWidescreenCommand.KeyTip);
                    g.Large("freep.slide-size-4x3", FreePRibbonText.SlideSizeStandardCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeStandardCommand.KeyTip);
                    g.Medium("freep.slide-size-custom", FreePRibbonText.SlideSizeCustomCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeCustomCommand.KeyTip);
                });
            })
            // ── Wave 4C: Transitions tab ───────────────────────────────────────────────
            .Tab("transitions", FreePRibbonText.TransitionsTab.Label, FreePRibbonText.TransitionsTab.KeyTip, tab =>
            {
                // "Transition to This Slide" group — gallery of transition kinds via Medium buttons.
                tab.Group("transition-gallery", FreePRibbonText.TransitionGalleryGroup.Label, FreePRibbonText.TransitionGalleryGroup.KeyTip, 100, g =>
                {
                    g.Medium("freep.transition.none", FreePRibbonText.TransitionNoneCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.TransitionNoneCommand.KeyTip);
                    g.Medium("freep.transition.fade", FreePRibbonText.TransitionFadeCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.TransitionFadeCommand.KeyTip);
                    g.Medium("freep.transition.push", FreePRibbonText.TransitionPushCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.TransitionPushCommand.KeyTip);
                    g.Medium("freep.transition.wipe", FreePRibbonText.TransitionWipeCommand.Label, RibbonCommandIconKind.ArrowLeft, FreePRibbonText.TransitionWipeCommand.KeyTip);
                    g.Medium("freep.transition.split", FreePRibbonText.TransitionSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.TransitionSplitCommand.KeyTip);
                    g.Medium("freep.transition.cut", FreePRibbonText.TransitionCutCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.TransitionCutCommand.KeyTip);
                    g.Medium("freep.transition.cover", FreePRibbonText.TransitionCoverCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.TransitionCoverCommand.KeyTip);
                    g.Medium("freep.transition.uncover", FreePRibbonText.TransitionUncoverCommand.Label, RibbonCommandIconKind.Expand, FreePRibbonText.TransitionUncoverCommand.KeyTip);
                    g.Medium("freep.transition.blinds", FreePRibbonText.TransitionBlindsCommand.Label, RibbonCommandIconKind.View, FreePRibbonText.TransitionBlindsCommand.KeyTip);
                    g.Medium("freep.transition.dissolve", FreePRibbonText.TransitionDissolveCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.TransitionDissolveCommand.KeyTip);
                    g.Medium("freep.transition.zoom", FreePRibbonText.TransitionZoomCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.TransitionZoomCommand.KeyTip);
                    g.Medium("freep.transition.wheel", FreePRibbonText.TransitionWheelCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.TransitionWheelCommand.KeyTip);
                });

                // Timing group — duration, advance options, apply to all.
                tab.Group("transition-timing", FreePRibbonText.TransitionTimingGroup.Label, FreePRibbonText.TransitionTimingGroup.KeyTip, 90, g =>
                {
                    g.ComboBox("freep.transition.duration", FreePRibbonText.TransitionDurationCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.TransitionDurations,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.MediumToggle("freep.transition.advance-on-click", FreePRibbonText.TransitionAdvanceOnClickCommand.Label,
                        RibbonCommandIconKind.Next, FreePRibbonText.TransitionAdvanceOnClickCommand.KeyTip);
                    g.ComboBox("freep.transition.advance-after", FreePRibbonText.TransitionAdvanceAfterCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.TransitionAdvanceAfterOptions,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.transition.apply-all", FreePRibbonText.TransitionApplyAllCommand.Label,
                        RibbonCommandIconKind.Refresh, FreePRibbonText.TransitionApplyAllCommand.KeyTip);
                });

                // Slide Show buttons live here for quick access from the Transitions tab.
                tab.Group("slideshow-from-transitions", FreePRibbonText.SlideShowGroupLabel, FreePRibbonText.SlideShowGroupWpfKeyTip, 80, g =>
                {
                    g.Large("freep.slideshow.from-beginning",     FreePRibbonText.SlideShowFromBeginningLabel,     RibbonCommandIconKind.Next,     FreePRibbonText.SlideShowFromBeginningKeyTip);
                    g.Large("freep.slideshow.from-current-slide", FreePRibbonText.SlideShowFromCurrentSlideLabel, RibbonCommandIconKind.Previous, FreePRibbonText.SlideShowFromCurrentSlideKeyTip);
                    g.Medium("freep.slideshow.custom-shows", FreePRibbonText.SlideShowCustomShowsLabel, RibbonCommandIconKind.List, FreePRibbonText.SlideShowCustomShowsKeyTip);
                });
            })
            // ── Wave 4C: Animations tab ───────────────────────────────────────────────
            .Tab("animations", FreePRibbonText.AnimationsTab.Label, FreePRibbonText.AnimationsTab.KeyTip, tab =>
            {
                // "Animation" group — Entrance, Emphasis, Exit effect buttons for selected shape.
                tab.Group("animation-effects", FreePRibbonText.AnimationEffectsGroup.Label, FreePRibbonText.AnimationEffectsGroup.KeyTip, 100, g =>
                {
                    // Entrance effects
                    g.Medium("freep.anim.entrance.appear", FreePRibbonText.AnimationEntranceAppearCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEntranceAppearCommand.KeyTip);
                    g.Medium("freep.anim.entrance.fade", FreePRibbonText.AnimationEntranceFadeCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationEntranceFadeCommand.KeyTip);
                    g.Medium("freep.anim.entrance.fly-in", FreePRibbonText.AnimationEntranceFlyInCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.AnimationEntranceFlyInCommand.KeyTip);
                    g.Medium("freep.anim.entrance.wipe", FreePRibbonText.AnimationEntranceWipeCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.AnimationEntranceWipeCommand.KeyTip);
                    g.Medium("freep.anim.entrance.zoom", FreePRibbonText.AnimationEntranceZoomCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.AnimationEntranceZoomCommand.KeyTip);
                    g.Medium("freep.anim.entrance.split", FreePRibbonText.AnimationEntranceSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.AnimationEntranceSplitCommand.KeyTip);
                    g.Separator();
                    // Emphasis effects
                    g.Medium("freep.anim.emphasis.pulse", FreePRibbonText.AnimationEmphasisPulseCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEmphasisPulseCommand.KeyTip);
                    g.Medium("freep.anim.emphasis.spin", FreePRibbonText.AnimationEmphasisSpinCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.AnimationEmphasisSpinCommand.KeyTip);
                    g.Medium("freep.anim.emphasis.grow-shrink", FreePRibbonText.AnimationEmphasisGrowShrinkCommand.Label, RibbonCommandIconKind.Scale, FreePRibbonText.AnimationEmphasisGrowShrinkCommand.KeyTip);
                    g.Separator();
                    // Exit effects
                    g.Medium("freep.anim.exit.disappear", FreePRibbonText.AnimationExitDisappearCommand.Label, RibbonCommandIconKind.Delete, FreePRibbonText.AnimationExitDisappearCommand.KeyTip);
                    g.Medium("freep.anim.exit.fade-out", FreePRibbonText.AnimationExitFadeOutCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationExitFadeOutCommand.KeyTip);
                    g.Medium("freep.anim.exit.fly-out", FreePRibbonText.AnimationExitFlyOutCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.AnimationExitFlyOutCommand.KeyTip);
                    g.Separator();
                    // Remove all animations from selected shape
                    g.Medium("freep.anim.none", FreePRibbonText.AnimationNoneCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.AnimationNoneCommand.KeyTip);
                });

                // Timing group — trigger, duration, delay, reorder.
                tab.Group("animation-timing", FreePRibbonText.AnimationTimingGroup.Label, FreePRibbonText.AnimationTimingGroup.KeyTip, 90, g =>
                {
                    g.ComboBox("freep.anim.trigger", FreePRibbonText.AnimationTriggerCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationTriggers,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                        Width = 130
                    });
                    g.ComboBox("freep.anim.duration", FreePRibbonText.AnimationDurationCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDurations,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.ComboBox("freep.anim.delay", FreePRibbonText.AnimationDelayCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDelays,
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.anim.move-earlier", FreePRibbonText.AnimationMoveEarlierCommand.Label, RibbonCommandIconKind.Previous, FreePRibbonText.AnimationMoveEarlierCommand.KeyTip);
                    g.Medium("freep.anim.move-later", FreePRibbonText.AnimationMoveLaterCommand.Label, RibbonCommandIconKind.Next, FreePRibbonText.AnimationMoveLaterCommand.KeyTip);
                });

                // Animation Pane toggle stub.
                tab.Group("animation-pane", FreePRibbonText.AdvancedAnimationGroup.Label, FreePRibbonText.AdvancedAnimationGroup.KeyTip, 80, g =>
                {
                    g.MediumToggle("freep.anim.pane", FreePRibbonText.AnimationPaneCommand.Label, RibbonCommandIconKind.List, FreePRibbonText.AnimationPaneCommand.KeyTip);
                });
            })
            .Tab("view", FreePRibbonText.ViewTab.Label, FreePRibbonText.ViewTab.KeyTip, tab =>
            {
                tab.Group("show", FreePRibbonText.ViewShowGroup.Label, FreePRibbonText.ViewShowGroup.KeyTip, 100, g =>
                {
                    g.MediumToggle("freep.view.show.gridlines", FreePRibbonText.ViewGridlinesCommand.Label,
                        RibbonCommandIconKind.Grid, FreePRibbonText.ViewGridlinesCommand.KeyTip);
                    g.MediumToggle("freep.view.show.guides", FreePRibbonText.ViewGuidesCommand.Label,
                        RibbonCommandIconKind.Align, FreePRibbonText.ViewGuidesCommand.KeyTip);
                });
                tab.Group("zoom", FreePRibbonText.ViewZoomGroup.Label, FreePRibbonText.ViewZoomGroup.KeyTip, 90, g =>
                {
                    g.Large("freep.view.zoom", FreePRibbonText.ViewZoomCommand.Label,
                        RibbonCommandIconKind.Zoom, FreePRibbonText.ViewZoomCommand.KeyTip);
                    g.Medium("freep.view.fit-to-window", FreePRibbonText.ViewFitToWindowCommand.Label,
                        RibbonCommandIconKind.Scale, FreePRibbonText.ViewFitToWindowCommand.KeyTip);
                });
            })
            .Build();
    }

    private static void AddParagraphControls(RibbonGroupBuilder g)
    {
        g.Dropdown(
            PresentationListGalleryPlanner.BulletsCommandId,
            FreePRibbonText.BulletsCommand.Label,
            BuildListGalleryMenu(PresentationListGalleryPlanner.BuildBulletGalleryPlan()),
            d => d with
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.List),
                KeyTip = FreePRibbonText.BulletsCommand.KeyTip,
            });
        g.Dropdown(
            PresentationListGalleryPlanner.NumberingCommandId,
            FreePRibbonText.NumberingCommand.Label,
            BuildListGalleryMenu(PresentationListGalleryPlanner.BuildNumberingGalleryPlan()),
            d => d with
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.List),
                KeyTip = FreePRibbonText.NumberingCommand.KeyTip,
            });
        g.Icon("freep.paragraph.align-left", FreePRibbonText.AlignLeftCommand.Label, RibbonCommandIconKind.AlignLeft, FreePRibbonText.AlignLeftCommand.KeyTip);
        g.Icon("freep.paragraph.align-center", FreePRibbonText.AlignCenterCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.AlignCenterCommand.KeyTip);
        g.Icon("freep.paragraph.align-right", FreePRibbonText.AlignRightCommand.Label, RibbonCommandIconKind.AlignRight, FreePRibbonText.AlignRightCommand.KeyTip);
        g.Icon("freep.paragraph.align-justify", FreePRibbonText.AlignJustifyCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.AlignJustifyCommand.KeyTip);
        g.Icon("freep.indent-decrease", FreePRibbonText.IndentDecreaseCommand.Label, RibbonCommandIconKind.ArrowLeft, FreePRibbonText.IndentDecreaseCommand.KeyTip);
        g.Icon("freep.indent-increase", FreePRibbonText.IndentIncreaseCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.IndentIncreaseCommand.KeyTip);
    }

    internal static RibbonMenu BuildListGalleryMenu(PresentationListGalleryPlan plan) =>
        new(plan.Items.Select(item => new RibbonMenuItem(
            item.PreviewText,
            new RibbonCommandId(item.CommandId))
        {
            IsEnabled = item.IsEnabled
        }).ToArray());
}
