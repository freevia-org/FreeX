using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.Ribbon.Definitions;

/// <summary>
/// FreeP ribbon definition for the cross-platform host. Command wiring stays in
/// the consuming app's registry; do not add per-command lambdas here.
/// </summary>
internal static class FreePAvaloniaRibbonDefinition
{
    internal static RibbonDefinition Build()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", FreePRibbonText.HomeTabLabel, FreePRibbonText.HomeTabKeyTip, tab =>
            {
                tab.Group("file", FreePRibbonText.FileGroupLabel, FreePRibbonText.FileGroupKeyTip, 100, g =>
                {
                    g.Large("freep.file.new", FreePRibbonText.FileNewLabel, RibbonCommandIconKind.Insert, FreePRibbonText.FileNewKeyTip);
                    g.Large("freep.file.open", FreePRibbonText.FileOpenLabel, RibbonCommandIconKind.Refresh, FreePRibbonText.FileOpenKeyTip);
                    g.Large("freep.file.save", FreePRibbonText.FileSaveLabel, RibbonCommandIconKind.Save, FreePRibbonText.FileSaveKeyTip);
                    g.Medium("freep.file.save-as", FreePRibbonText.FileSaveAsLabel, RibbonCommandIconKind.Save, FreePRibbonText.FileSaveAsKeyTip);
                });
                tab.Group("slides", FreePRibbonText.SlidesGroupLabel, FreePRibbonText.SlidesGroupKeyTip, 90, g =>
                {
                    g.Large("freep.new-slide", FreePRibbonText.NewSlideLabel, RibbonCommandIconKind.Insert, FreePRibbonText.NewSlideAvaloniaKeyTip);
                    g.Medium("freep.duplicate-slide", FreePRibbonText.DuplicateSlideLabel, RibbonCommandIconKind.Copy, FreePRibbonText.DuplicateSlideKeyTip);
                    g.Medium("freep.delete-slide", FreePRibbonText.DeleteSlideLabel, RibbonCommandIconKind.Delete, FreePRibbonText.DeleteSlideKeyTip);
                    g.Medium("freep.layout", FreePRibbonText.LayoutLabel, RibbonCommandIconKind.Grid, FreePRibbonText.LayoutKeyTip);
                });
                tab.Group("clipboard", FreePRibbonText.ClipboardGroupLabel, FreePRibbonText.ClipboardGroupKeyTip, 88, g =>
                {
                    g.Large("freep.paste", FreePRibbonText.PasteLabel, RibbonCommandIconKind.Paste, FreePRibbonText.PasteKeyTip);
                    g.Medium("freep.cut", FreePRibbonText.CutLabel, RibbonCommandIconKind.Cut, FreePRibbonText.CutKeyTip);
                    g.Medium("freep.copy", FreePRibbonText.CopyLabel, RibbonCommandIconKind.Copy, FreePRibbonText.CopyKeyTip);
                    g.Medium("freep.format-painter", FreePRibbonText.FormatPainterLabel, RibbonCommandIconKind.FormatPainter, FreePRibbonText.FormatPainterKeyTip);
                });
                tab.Group("font", FreePRibbonText.FontGroupLabel, FreePRibbonText.FontGroupKeyTip, 86, g =>
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
                tab.Group("paragraph", FreePRibbonText.ParagraphGroup.Label, FreePRibbonText.ParagraphGroup.KeyTip, 84, AddParagraphControls);
                tab.Group("arrange", FreePRibbonText.ArrangeGroup.Label, FreePRibbonText.ArrangeGroup.KeyTip, 85, g =>
                {
                    g.Large("freep.arrange.group", FreePRibbonText.ArrangeGroupCommand.Label, RibbonCommandIconKind.Group, FreePRibbonText.ArrangeGroupCommand.KeyTip);
                    g.Medium("freep.arrange.ungroup", FreePRibbonText.ArrangeUngroupCommand.Label, RibbonCommandIconKind.Ungroup, FreePRibbonText.ArrangeUngroupCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.bring-to-front", FreePRibbonText.ArrangeBringToFrontCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringToFrontCommand.KeyTip);
                    g.Medium("freep.arrange.bring-forward", FreePRibbonText.ArrangeBringForwardCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringForwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-backward", FreePRibbonText.ArrangeSendBackwardCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendBackwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-to-back", FreePRibbonText.ArrangeSendToBackCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendToBackCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.align-left", FreePRibbonText.ArrangeAlignLeftCommand.Label, RibbonCommandIconKind.AlignLeft, FreePRibbonText.ArrangeAlignLeftCommand.KeyTip);
                    g.Medium("freep.arrange.align-center-h", FreePRibbonText.ArrangeAlignCenterHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeAlignCenterHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.align-right", FreePRibbonText.ArrangeAlignRightCommand.Label, RibbonCommandIconKind.AlignRight, FreePRibbonText.ArrangeAlignRightCommand.KeyTip);
                    g.Medium("freep.arrange.align-top", FreePRibbonText.ArrangeAlignTopCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeAlignTopCommand.KeyTip);
                    g.Medium("freep.arrange.align-middle", FreePRibbonText.ArrangeAlignMiddleCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeAlignMiddleCommand.KeyTip);
                    g.Medium("freep.arrange.align-bottom", FreePRibbonText.ArrangeAlignBottomCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeAlignBottomCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.distribute-h", FreePRibbonText.ArrangeDistributeHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeDistributeHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.distribute-v", FreePRibbonText.ArrangeDistributeVerticalCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeDistributeVerticalCommand.KeyTip);
                });
                tab.Group("edit", FreePRibbonText.EditGroupLabel, FreePRibbonText.EditGroupKeyTip, 80, g =>
                {
                    g.Large("freep.undo", FreePRibbonText.UndoLabel, RibbonCommandIconKind.Undo, FreePRibbonText.UndoKeyTip);
                    g.Large("freep.redo", FreePRibbonText.RedoLabel, RibbonCommandIconKind.Redo, FreePRibbonText.RedoKeyTip);
                });
                tab.Group("editing", FreePRibbonText.EditingGroupLabel, FreePRibbonText.EditingGroupKeyTip, 75, g =>
                {
                    g.Large("freep.find", FreePRibbonText.FindLabel, RibbonCommandIconKind.Search, FreePRibbonText.FindKeyTip);
                    g.Medium("freep.replace", FreePRibbonText.ReplaceLabel, RibbonCommandIconKind.Refresh, FreePRibbonText.ReplaceKeyTip);
                });
                tab.Group("slideshow", FreePRibbonText.SlideShowGroupLabel, FreePRibbonText.SlideShowGroupAvaloniaKeyTip, 70, g =>
                {
                    g.Large("freep.slideshow.from-beginning", FreePRibbonText.SlideShowFromBeginningLabel,
                        RibbonCommandIconKind.Next, FreePRibbonText.SlideShowFromBeginningKeyTip);
                    g.Large("freep.slideshow.from-current-slide", FreePRibbonText.SlideShowFromCurrentSlideLabel,
                        RibbonCommandIconKind.Next, FreePRibbonText.SlideShowFromCurrentSlideKeyTip);
                    g.Medium("freep.slideshow.custom-shows", FreePRibbonText.SlideShowCustomShowsLabel,
                        RibbonCommandIconKind.List, FreePRibbonText.SlideShowCustomShowsKeyTip);
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
                tab.Group("tables", FreePRibbonText.TablesGroupLabel, FreePRibbonText.TablesGroupKeyTip, 95, g =>
                {
                    g.Large("freep.insert-table-3x3", FreePRibbonText.InsertTable3x3Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable3x3KeyTip);
                    g.Medium("freep.insert-table-2x2", FreePRibbonText.InsertTable2x2Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable2x2KeyTip);
                    g.Medium("freep.insert-table-4x4", FreePRibbonText.InsertTable4x4Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable4x4KeyTip);
                });
                tab.Group("charts", FreePRibbonText.ChartsGroupLabel, FreePRibbonText.ChartsGroupKeyTip, 93, g =>
                {
                    g.Medium("freep.insert-chart-column", FreePRibbonText.InsertChartColumnLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartColumnKeyTip);
                    g.Medium("freep.insert-chart-bar", FreePRibbonText.InsertChartBarLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartBarKeyTip);
                    g.Medium("freep.insert-chart-line", FreePRibbonText.InsertChartLineLabel, RibbonCommandIconKind.ChartLine, FreePRibbonText.InsertChartLineKeyTip);
                    g.Medium("freep.insert-chart-pie", FreePRibbonText.InsertChartPieLabel, RibbonCommandIconKind.ChartPie, FreePRibbonText.InsertChartPieKeyTip);
                    g.Medium("freep.chart.edit-data", FreePRibbonText.ChartEditDataLabel, RibbonCommandIconKind.ChartTitle, FreePRibbonText.ChartEditDataKeyTip);
                });
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
            .Tab("design", FreePRibbonText.DesignTab.Label, FreePRibbonText.DesignTab.KeyTip, tab =>
            {
                tab.Group("themes", FreePRibbonText.ThemesGroup.Label, FreePRibbonText.ThemesGroup.KeyTip, 100, g =>
                {
                    g.Large("freep.theme.office", FreePRibbonText.ThemeOfficeCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeOfficeCommand.KeyTip);
                    g.Medium("freep.theme.berlin", FreePRibbonText.ThemeBerlinCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeBerlinCommand.KeyTip);
                    g.Medium("freep.theme.facet", FreePRibbonText.ThemeFacetCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeFacetCommand.KeyTip);
                    g.Medium("freep.theme.ion", FreePRibbonText.ThemeIonCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeIonCommand.KeyTip);
                    g.Medium("freep.theme.slice", FreePRibbonText.ThemeSliceCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeSliceCommand.KeyTip);
                });
                tab.Group("customize", FreePRibbonText.CustomizeGroup.Label, FreePRibbonText.CustomizeGroup.KeyTip, 90, g =>
                {
                    g.Large("freep.slide-size-16x9", FreePRibbonText.SlideSizeWidescreenCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeWidescreenCommand.KeyTip);
                    g.Large("freep.slide-size-4x3", FreePRibbonText.SlideSizeStandardCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeStandardCommand.KeyTip);
                    g.Medium("freep.slide-size-custom", FreePRibbonText.SlideSizeCustomCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeCustomCommand.KeyTip);
                });
            })
            .Tab("transitions", FreePRibbonText.TransitionsTab.Label, FreePRibbonText.TransitionsTab.KeyTip, tab =>
            {
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

                tab.Group("transition-timing", FreePRibbonText.TransitionTimingGroup.Label, FreePRibbonText.TransitionTimingGroup.KeyTip, 90, g =>
                {
                    g.ComboBox("freep.transition.duration", FreePRibbonText.TransitionDurationCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.TransitionDurations,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.MediumToggle("freep.transition.advance-on-click", FreePRibbonText.TransitionAdvanceOnClickCommand.Label,
                        RibbonCommandIconKind.Next, FreePRibbonText.TransitionAdvanceOnClickCommand.KeyTip);
                    g.ComboBox("freep.transition.advance-after", FreePRibbonText.TransitionAdvanceAfterCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.TransitionAdvanceAfterOptions,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.transition.apply-all", FreePRibbonText.TransitionApplyAllCommand.Label,
                        RibbonCommandIconKind.Refresh, FreePRibbonText.TransitionApplyAllCommand.KeyTip);
                });
            })
            .Tab("animations", FreePRibbonText.AnimationsTab.Label, FreePRibbonText.AnimationsTab.KeyTip, tab =>
            {
                tab.Group("animation-effects", FreePRibbonText.AnimationEffectsGroup.Label, FreePRibbonText.AnimationEffectsGroup.KeyTip, 100, g =>
                {
                    g.Medium("freep.anim.entrance.appear", FreePRibbonText.AnimationEntranceAppearCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEntranceAppearCommand.KeyTip);
                    g.Medium("freep.anim.entrance.fade", FreePRibbonText.AnimationEntranceFadeCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationEntranceFadeCommand.KeyTip);
                    g.Medium("freep.anim.entrance.fly-in", FreePRibbonText.AnimationEntranceFlyInCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.AnimationEntranceFlyInCommand.KeyTip);
                    g.Medium("freep.anim.entrance.wipe", FreePRibbonText.AnimationEntranceWipeCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.AnimationEntranceWipeCommand.KeyTip);
                    g.Medium("freep.anim.entrance.zoom", FreePRibbonText.AnimationEntranceZoomCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.AnimationEntranceZoomCommand.KeyTip);
                    g.Medium("freep.anim.entrance.split", FreePRibbonText.AnimationEntranceSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.AnimationEntranceSplitCommand.KeyTip);
                    g.Medium("freep.anim.emphasis.pulse", FreePRibbonText.AnimationEmphasisPulseCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEmphasisPulseCommand.KeyTip);
                    g.Medium("freep.anim.emphasis.spin", FreePRibbonText.AnimationEmphasisSpinCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.AnimationEmphasisSpinCommand.KeyTip);
                    g.Medium("freep.anim.emphasis.grow-shrink", FreePRibbonText.AnimationEmphasisGrowShrinkCommand.Label, RibbonCommandIconKind.Scale, FreePRibbonText.AnimationEmphasisGrowShrinkCommand.KeyTip);
                    g.Medium("freep.anim.exit.disappear", FreePRibbonText.AnimationExitDisappearCommand.Label, RibbonCommandIconKind.Delete, FreePRibbonText.AnimationExitDisappearCommand.KeyTip);
                    g.Medium("freep.anim.exit.fade-out", FreePRibbonText.AnimationExitFadeOutCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationExitFadeOutCommand.KeyTip);
                    g.Medium("freep.anim.exit.fly-out", FreePRibbonText.AnimationExitFlyOutCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.AnimationExitFlyOutCommand.KeyTip);
                    g.Medium("freep.anim.none", FreePRibbonText.AnimationNoneCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.AnimationNoneCommand.KeyTip);
                });

                tab.Group("animation-timing", FreePRibbonText.AnimationTimingGroup.Label, FreePRibbonText.AnimationTimingGroup.KeyTip, 90, g =>
                {
                    g.ComboBox("freep.anim.trigger", FreePRibbonText.AnimationTriggerCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationTriggers,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                        Width = 120
                    });
                    g.ComboBox("freep.anim.duration", FreePRibbonText.AnimationDurationCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDurations,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.ComboBox("freep.anim.delay", FreePRibbonText.AnimationDelayCommand.Label, c => c with
                    {
                        Items = FreePRibbonDefinitionData.AnimationDelays,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.anim.move-earlier", FreePRibbonText.AnimationMoveEarlierCommand.Label, RibbonCommandIconKind.Previous, FreePRibbonText.AnimationMoveEarlierCommand.KeyTip);
                    g.Medium("freep.anim.move-later", FreePRibbonText.AnimationMoveLaterCommand.Label, RibbonCommandIconKind.Next, FreePRibbonText.AnimationMoveLaterCommand.KeyTip);
                });

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
            FreePRibbon.BuildListGalleryMenu(PresentationListGalleryPlanner.BuildBulletGalleryPlan()),
            d => d with
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.List),
                KeyTip = FreePRibbonText.BulletsCommand.KeyTip,
            });
        g.Dropdown(
            PresentationListGalleryPlanner.NumberingCommandId,
            FreePRibbonText.NumberingCommand.Label,
            FreePRibbon.BuildListGalleryMenu(PresentationListGalleryPlanner.BuildNumberingGalleryPlan()),
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
}
