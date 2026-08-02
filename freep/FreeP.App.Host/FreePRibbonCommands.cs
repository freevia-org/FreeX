using System.Globalization;
using System.IO;
using System.Windows;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Binds FreeP's ribbon command ids (declared in <see cref="FreePRibbon"/>) to behavior, implementing the
/// shared <see cref="IRibbonCommandRegistry"/>.
///
/// Wave 3A: most ids are now real commands routed through <see cref="EditingSession"/>.
/// Wave 4C: Transitions tab + Animations tab + Slide Show buttons wired here.
///           Build() gains two extra parameters for the slideshow start Actions supplied by MainWindow.
/// Wave 5B: clipboard (copy/cut/paste) wired; font-family ComboBox wired; Design tab (themes +
///           slide-size) wired; Insert tables + charts wired; Format Painter wired.
///
/// Animation trigger, duration, and delay edits are routed through the shared
/// animation-pane timing mutation planner.
/// </summary>
internal static class FreePRibbonCommands
{
    /// <param name="stateStore">Ribbon state store (checked / enabled flags).</param>
    /// <param name="editor">The active editing session.</param>
    /// <param name="onStartFromStart">
    ///   Callback that starts the slideshow from the first slide (wired to MainWindow.StartSlideShow(true)).
    ///   Provided by Wave 4B; stub is acceptable here during parallel development.
    /// </param>
    /// <param name="onStartFromCurrent">
    ///   Callback that starts the slideshow from the current slide (wired to MainWindow.StartSlideShow(false)).
    /// </param>
    /// <param name="onEditChartData">
    ///   Callback that opens the chart data editing dialog for the currently selected chart.
    ///   Provided by Wave 9B / MainWindow.  When null the button is a no-op.
    /// </param>
    /// <param name="getSlideCanvas">
    ///   Wave 10A: a late-binding getter for the live SlideCanvas. Used to route
    ///   Bold/Italic/Underline/Font to the active RichTextBox editor when it is open, instead
    ///   of applying the command to the whole-shape TextBody. May be null (e.g. in tests);
    ///   routing is silently skipped when the getter returns null or no editor is active.
    /// </param>
    /// <param name="onCustomSlideSize">
    ///   Callback that opens the custom slide-size dialog (Wave 10B).
    ///   Wired to <c>MainWindow.OpenSlideSizeDialog()</c>.  When null the button is a no-op.
    /// </param>
    /// <param name="onLayoutPicker">
    ///   Callback that opens or announces the slide-layout picker.  The shared planner exposes this
    ///   as an explicit host intent so the command is no longer a silent stub.
    /// </param>
    /// <param name="osClipboard">
    ///   Optional OS-clipboard service (Wave 10B). When provided, ribbon Copy/Cut also
    ///   place content on the OS clipboard; ribbon Paste checks the OS clipboard first.
    ///   When null the ribbon uses the internal clipboard only (original Wave 5B behaviour).
    /// </param>
    /// <param name="onInsertLink">
    ///   Wave 11A: callback that opens the Insert Hyperlink dialog.
    ///   Provided by MainWindow which builds and owns the dialog.
    /// </param>
    /// <param name="onAnimPane">
    ///   Wave 16B: callback that toggles the Animation Pane panel visibility.
    ///   Provided by MainWindow.ToggleAnimationPane().  When null the stub is a no-op.
    /// </param>
    /// <param name="onTransitionSound">
    ///   Callback that opens the host audio picker and applies the selected transition sound.
    /// </param>
    public static RibbonCommandRegistry Build(
        RibbonStateStore    stateStore,
        EditingSession      editor,
        Action?             onStartFromStart   = null,
        Action?             onStartFromCurrent = null,
        Action?             onRehearseTimings  = null,
        Action?             onRecordTimings    = null,
        Action?             onEditChartData    = null,
        Func<SlideCanvas?>? getSlideCanvas     = null,
        Action?             onEditPoints       = null,
        Action?             onCustomSlideSize  = null,
        OsClipboardService? osClipboard        = null,
        Action?             onInsertLink       = null,
        Action?             onInsertSlideZoom = null,
        Action?             onInsertSectionZoom = null,
        Action?             onInsertSummaryZoom = null,
        // Wave 12B: Find & Replace dialog launchers.
        Action?             onFind             = null,
        Action?             onFindReplace      = null,
        Action?             onReviewCommentsPane = null,
        Action?             onReviewAccessibility = null,
        Action?             onReviewAltText = null,
        Action?             onReviewReadingOrder = null,
        Action?             onSelectionPane = null,
        Action?             onReviewProofing = null,
        Action?             onAddComment = null,
        Action?             onEditComment = null,
        Action?             onReplyComment = null,
        Action?             onDeleteComment = null,
        Action?             onPreviousComment = null,
        Action?             onNextComment = null,
        Action?             onResolveComment = null,
        Action?             onReopenComment = null,
        // Wave 16B: Animation pane toggle.
        Action?             onAnimPane         = null,
        Action?             onLayoutPicker     = null,
        Action?             onTablePicker      = null,
        Action<HeaderFooterCommandFocus>? onHeaderFooter = null,
        Func<PresentationViewShowState>? getViewShowState = null,
        Action<PresentationViewShowState>? applyViewShowState = null,
        Func<PresentationViewZoomState>? getViewZoomState = null,
        Action<PresentationViewZoomState>? applyViewZoomState = null,
        Action?             onCustomShows     = null,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload = null,
        Action<SmartArtColorPreset>? onSmartArtColorPreset = null,
        Action<SmartArtLayoutPreset>? onSmartArtLayoutPreset = null,
        Action<SmartArtQuickStylePreset>? onSmartArtQuickStylePreset = null,
        Action?             onEditChartOptions = null,
        Action?             onEditChartAxisOptions = null,
        Action?             onEditChartSeriesOptions = null,
        Action?             onEditChartPointOptions = null,
        Action?             onEditChartLayoutOptions = null,
        Action?             onEditChartDataTableOptions = null,
        Action?             onEditChartBubbleOptions = null,
        Action?             onEditChartPieOptions = null,
        Action?             onEditChartPlotStyleOptions = null,
        Action?             onEditChart3DViewOptions = null,
        Action?             onEditChartTextOptions = null,
        Action?             onEditChartAreaOptions = null,
        Action?             onEditChartProtectionOptions = null,
        Action?             onEditRotationOptions = null,
        Action?             onInsertEmbeddedObject = null,
        Action<OleObjectInfo>? onOpenEmbeddedObject = null,
        Func<bool>?          tryOpenInlineEmbeddedObject = null,
        Action?             onTransitionSound = null,
        Func<bool>?          getEditPointsEnabled = null,
        Action<bool>?         setEditPointsEnabled = null,
        Action?             onFormatZoom = null,
        Action?             onSetZoomCoverImage = null,
        Action?             onResetZoomCoverImage = null)
    {
        var registry = new RibbonCommandRegistry();
        registry.Register("freep.undo",
            new ActionRibbonCommand(() => editor.Undo()));
        registry.Register("freep.redo",
            new ActionRibbonCommand(() => editor.Redo()));

        // ── Slide management ─────────────────────────────────────────────────────

        registry.Register("freep.new-slide",
            new ActionRibbonCommand(() => editor.InsertSlide()));

        registry.Register("freep.duplicate-slide",
            new ActionRibbonCommand(() => editor.DuplicateCurrentSlide()));

        registry.Register("freep.delete-slide",
            new ActionRibbonCommand(() => editor.DeleteCurrentSlide()));

        // ── Insert shapes ────────────────────────────────────────────────────────

        RegisterSlideObjectInsertionCommands(registry, editor, includePictureCommand: true, onTablePicker);
        registry.Register(
            SlideZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => onInsertSlideZoom?.Invoke()));
        registry.Register(
            SectionZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => onInsertSectionZoom?.Invoke()));
        registry.Register(
            SummaryZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => onInsertSummaryZoom?.Invoke()));
        registry.Register(
            ZoomObjectPropertiesPlanner.CommandId,
            new ActionRibbonCommand(() => onFormatZoom?.Invoke()));
        registry.Register(
            ZoomCoverImagePlanner.CommandId,
            new ActionRibbonCommand(() => onSetZoomCoverImage?.Invoke()));
        registry.Register(
            ZoomCoverImagePlanner.ResetCommandId,
            new ActionRibbonCommand(() => onResetZoomCoverImage?.Invoke()));
        registry.Register(
            OleInsertionPlanner.InsertEmbeddedObjectCommandId,
            new ActionRibbonCommand(() => onInsertEmbeddedObject?.Invoke()));
        registry.Register(
            PictureCropAuthoringPlanner.InsetCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Inset())));
        registry.Register(
            PictureCropAuthoringPlanner.ResetCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Reset())));
        registry.Register(
            PictureColorEffectAuthoringPlanner.GrayscaleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Grayscale())));
        registry.Register(
            PictureColorEffectAuthoringPlanner.ResetCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Reset())));
        registry.Register(
            ShapeEffectAuthoringPlanner.NoneCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.None())));
        registry.Register(
            ShapeEffectAuthoringPlanner.SubtleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Subtle())));
        registry.Register(
            ShapeEffectAuthoringPlanner.OffsetCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Offset())));
        registry.Register(
            ShapeEffectAuthoringPlanner.GlowNoneCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowNone())));
        registry.Register(
            ShapeEffectAuthoringPlanner.GlowSubtleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowSubtle())));
        registry.Register(
            ShapeEffectAuthoringPlanner.GlowStrongCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowStrong())));
        registry.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeNone())));
        registry.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeSubtle())));
        registry.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeStrong())));
        registry.Register(
            ShapeEffectAuthoringPlanner.BevelNoneCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelNone())));
        registry.Register(
            ShapeEffectAuthoringPlanner.BevelSubtleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelSubtle())));
        registry.Register(
            ShapeEffectAuthoringPlanner.BevelStrongCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelStrong())));
        registry.Register(
            ShapeEffectAuthoringPlanner.Shape3dNoneCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dNone())));
        registry.Register(
            ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dSubtle())));
        registry.Register(
            ShapeEffectAuthoringPlanner.Shape3dStrongCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dStrong())));
        RegisterHeaderFooterCommands(registry, editor, onHeaderFooter);
        registry.Register(SmartArtAuthoringPlanner.ThemeAccentsCommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.ThemeAccents)));
        registry.Register(SmartArtAuthoringPlanner.SingleAccentCommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.SingleAccent)));
        registry.Register(SmartArtAuthoringPlanner.MonochromaticAccent2CommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.MonochromaticAccent2)));
        registry.Register(SmartArtAuthoringPlanner.MonochromaticAccent3CommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.MonochromaticAccent3)));
        registry.Register(SmartArtAuthoringPlanner.MonochromaticAccent4CommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.MonochromaticAccent4)));
        registry.Register(SmartArtAuthoringPlanner.MonochromaticAccent5CommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.MonochromaticAccent5)));
        registry.Register(SmartArtAuthoringPlanner.MonochromaticAccent6CommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.MonochromaticAccent6)));
        registry.Register(SmartArtAuthoringPlanner.GrayscaleCommandId,
            new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(SmartArtColorPreset.Grayscale)));
        foreach (var entry in SmartArtAuthoringPlanner.ColorGallery)
        {
            var preset = entry.Preset;
            registry.Register(entry.CommandId,
                new ActionRibbonCommand(() => onSmartArtColorPreset?.Invoke(preset)));
        }
        registry.Register(SmartArtAuthoringPlanner.BasicProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicProcess)));
        registry.Register(SmartArtAuthoringPlanner.AccentProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.AccentProcess)));
        registry.Register(SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.AscendingProcess)));
        registry.Register(SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.DescendingProcess)));
        registry.Register(SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicTimeline)));
        registry.Register(SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.CircleAccentTimeline)));
        registry.Register(SmartArtAuthoringPlanner.PhasedProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PhasedProcess)));
        registry.Register(SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.StepDownProcess)));
        registry.Register(SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ContinuousBlockProcess)));
        registry.Register(SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.SegmentedProcess)));
        registry.Register(SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ChevronProcess)));
        registry.Register(SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicChevronProcess)));
        registry.Register(SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ClosedChevronProcess)));
        registry.Register(SmartArtAuthoringPlanner.BendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BendingProcess)));
        registry.Register(SmartArtAuthoringPlanner.AlternatingProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.AlternatingProcess)));
        registry.Register(SmartArtAuthoringPlanner.ArrowRibbonLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ArrowRibbon)));
        registry.Register(SmartArtAuthoringPlanner.CircleProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.CircleProcess)));
        registry.Register(SmartArtAuthoringPlanner.CircleArrowProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.CircleArrowProcess)));
        registry.Register(SmartArtAuthoringPlanner.IncreasingCircleProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.IncreasingCircleProcess)));
        registry.Register(SmartArtAuthoringPlanner.FunnelProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.FunnelProcess)));
        registry.Register(SmartArtAuthoringPlanner.VerticalProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalProcess)));
        registry.Register(SmartArtAuthoringPlanner.VerticalBoxListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalBoxList)));
        registry.Register(SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalBlockList)));
        registry.Register(SmartArtAuthoringPlanner.VerticalChevronListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalChevronList)));
        registry.Register(SmartArtAuthoringPlanner.VerticalArrowListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalArrowList)));
        registry.Register(SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.VerticalBulletList)));
        registry.Register(SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.HorizontalBulletList)));
        registry.Register(SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.HorizontalBlockList)));
        registry.Register(SmartArtAuthoringPlanner.TrapezoidListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.TrapezoidList)));
        registry.Register(SmartArtAuthoringPlanner.BasicCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicCycle)));
        registry.Register(SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.MultidirectionalCycle)));
        registry.Register(SmartArtAuthoringPlanner.Cycle2LayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.Cycle2)));
        registry.Register(SmartArtAuthoringPlanner.ContinuousCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ContinuousCycle)));
        registry.Register(SmartArtAuthoringPlanner.GearCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.GearCycle)));
        registry.Register(SmartArtAuthoringPlanner.TextCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.TextCycle)));
        registry.Register(SmartArtAuthoringPlanner.BlockCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BlockCycle)));
        registry.Register(SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.NonDirectionalCycle)));
        registry.Register(SmartArtAuthoringPlanner.BasicBlockListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicBlockList)));
        registry.Register(SmartArtAuthoringPlanner.BasicListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicList)));
        registry.Register(SmartArtAuthoringPlanner.List2LayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.List2)));
        registry.Register(SmartArtAuthoringPlanner.StackedListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.StackedList)));
        registry.Register(SmartArtAuthoringPlanner.DescendingBlockListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.DescendingBlockList)));
        registry.Register(SmartArtAuthoringPlanner.BasicPyramidLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicPyramid)));
        registry.Register(SmartArtAuthoringPlanner.PyramidListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PyramidList)));
        registry.Register(SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.InvertedPyramid)));
        registry.Register(SmartArtAuthoringPlanner.RadialCycleLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.RadialCycle)));
        registry.Register(SmartArtAuthoringPlanner.BasicRadialLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicRadial)));
        registry.Register(SmartArtAuthoringPlanner.RadialClusterLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.RadialCluster)));
        registry.Register(SmartArtAuthoringPlanner.RadialListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.RadialList)));
        registry.Register(SmartArtAuthoringPlanner.BasicMatrixLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicMatrix)));
        registry.Register(SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.TitledMatrix)));
        registry.Register(SmartArtAuthoringPlanner.GridMatrixLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.GridMatrix)));
        registry.Register(SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicRelationship)));
        registry.Register(SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.OpposingIdeas)));
        registry.Register(SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ConvergingRadial)));
        registry.Register(SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.DivergingRadial)));
        registry.Register(SmartArtAuthoringPlanner.BasicVennLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicVenn)));
        registry.Register(SmartArtAuthoringPlanner.RadialVennLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.RadialVenn)));
        registry.Register(SmartArtAuthoringPlanner.TargetListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.TargetList)));
        registry.Register(SmartArtAuthoringPlanner.StackedVennLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.StackedVenn)));
        registry.Register(SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.InterlockingRings)));
        registry.Register(SmartArtAuthoringPlanner.BasicHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.BasicHierarchy)));
        registry.Register(SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.Hierarchy3)));
        registry.Register(SmartArtAuthoringPlanner.HorizontalHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.HorizontalHierarchy)));
        registry.Register(SmartArtAuthoringPlanner.OrgChartLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.OrgChart)));
        registry.Register(SmartArtAuthoringPlanner.NameAndTitleOrgChartLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.NameAndTitleOrgChart)));
        registry.Register(SmartArtAuthoringPlanner.PictureCaptionListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureCaptionList)));
        registry.Register(SmartArtAuthoringPlanner.PictureAccentListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureAccentList)));
        registry.Register(SmartArtAuthoringPlanner.PictureStackLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureStack)));
        registry.Register(SmartArtAuthoringPlanner.PictureLineupLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureLineup)));
        registry.Register(SmartArtAuthoringPlanner.PictureStripsLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureStrips)));
        registry.Register(SmartArtAuthoringPlanner.ContinuousPictureListLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.ContinuousPictureList)));
        registry.Register(SmartArtAuthoringPlanner.PictureGridLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureGrid)));
        registry.Register(SmartArtAuthoringPlanner.PictureAccentProcessLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.PictureAccentProcess)));
        registry.Register(SmartArtAuthoringPlanner.LabeledHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.LabeledHierarchy)));
        registry.Register(SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => onSmartArtLayoutPreset?.Invoke(SmartArtLayoutPreset.TableHierarchy)));
        registry.Register(SmartArtAuthoringPlanner.SimpleQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Simple)));
        registry.Register(SmartArtAuthoringPlanner.ModerateQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Moderate)));
        registry.Register(SmartArtAuthoringPlanner.IntenseQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Intense)));
        registry.Register(SmartArtAuthoringPlanner.SubtleQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Subtle)));
        registry.Register(SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.SoftEdge)));
        registry.Register(SmartArtAuthoringPlanner.InsertQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Insert)));
        registry.Register(SmartArtAuthoringPlanner.CartoonQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Cartoon)));
        registry.Register(SmartArtAuthoringPlanner.PowderQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Powder)));
        registry.Register(SmartArtAuthoringPlanner.PolishedQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.Polished)));
        registry.Register(SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.BrickScene)));
        registry.Register(SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.FlatScene)));
        registry.Register(SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.MetallicScene)));
        registry.Register(SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.SunsetScene)));
        registry.Register(SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => onSmartArtQuickStylePreset?.Invoke(SmartArtQuickStylePreset.BirdsEyeScene)));
        registry.Register(SmartArtAuthoringPlanner.ConvertToShapesCommandId,
            new ActionRibbonCommand(() =>
            {
                if (editor.SelectedShapeIds.Count == 1)
                    editor.ConvertSmartArtToShapes(editor.SelectedShapeIds[0]);
            }));

        // ── Format toggles (stateful) ────────────────────────────────────────────
        //
        // Wave 10A routing: when the in-canvas RichTextBox editor is active, format commands
        // apply to the RichTextBox selection; otherwise they fall through to the whole-shape
        // EditingSession toggles.  The routing helper is defined at the bottom of this class.
        //
        // 10B NOTE: this block is the only region that references slideCanvas in this file.
        // Keep it isolated here to minimise merge churn with 10B.

        registry.Register("freep.bold", new EditorToggleCommand(stateStore, "freep.bold", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplyBold(), e => e.ApplyBold())) return;
            if (editor.ToggleBoldOnActiveTableCell()) return;
            editor.ToggleBoldOnSelection();
        }));
        registry.Register("freep.italic", new EditorToggleCommand(stateStore, "freep.italic", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplyItalic(), e => e.ApplyItalic())) return;
            if (editor.ToggleItalicOnActiveTableCell()) return;
            editor.ToggleItalicOnSelection();
        }));
        registry.Register("freep.underline", new EditorToggleCommand(stateStore, "freep.underline", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplyUnderline(), e => e.ApplyUnderline())) return;
            if (editor.ToggleUnderlineOnActiveTableCell()) return;
            editor.ToggleUnderlineOnSelection();
        }));
        registry.Register("freep.superscript", new EditorToggleCommand(stateStore, "freep.superscript", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplySuperscript(), e => e.ApplySuperscript())) return;
            if (editor.ToggleSuperscriptOnActiveTableCell()) return;
            editor.ToggleSuperscriptOnSelection();
        }));
        registry.Register("freep.subscript", new EditorToggleCommand(stateStore, "freep.subscript", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplySubscript(), e => e.ApplySubscript())) return;
            if (editor.ToggleSubscriptOnActiveTableCell()) return;
            editor.ToggleSubscriptOnSelection();
        }));

        registry.Register("freep.paragraph.align-left",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Left) == true) return;
                editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Left);
            }));
        registry.Register("freep.paragraph.align-center",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Center) == true) return;
                editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Center);
            }));
        registry.Register("freep.paragraph.align-right",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Right) == true) return;
                editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Right);
            }));
        registry.Register("freep.paragraph.align-justify",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Justify) == true) return;
                editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Justify);
            }));
        registry.Register("freep.bullets",
            new ContextRibbonCommand(ctx =>
            {
                var shapeEditor = getSlideCanvas?.Invoke()?.TextEditor;
                if (PresentationListGalleryPlanner.TryGetPresetCommand(ctx.SelectedValue, out var shapePreset) &&
                    shapePreset is not null &&
                    shapeEditor?.TryApplyActiveShapeParagraphListPreset(shapePreset) == true) return;
                if (shapeEditor?.TryApplyActiveShapeParagraphBulletToggle() == true) return;
                if (ApplyTableCellListPreset(editor, ctx.SelectedValue)) return;
                editor.TryApplyActiveTableCellParagraphBulletToggle();
            }));
        registry.Register("freep.numbering",
            new ContextRibbonCommand(ctx =>
            {
                var shapeEditor = getSlideCanvas?.Invoke()?.TextEditor;
                if (PresentationListGalleryPlanner.TryGetPresetCommand(ctx.SelectedValue, out var shapePreset) &&
                    shapePreset is not null &&
                    shapeEditor?.TryApplyActiveShapeParagraphListPreset(shapePreset) == true) return;
                if (shapeEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true) return;
                if (ApplyTableCellListPreset(editor, ctx.SelectedValue)) return;
                editor.TryApplyActiveTableCellParagraphNumberingToggle();
            }));
        RegisterListGalleryPresetCommands(registry, editor, getSlideCanvas, pickPictureBulletPayload);
        registry.Register("freep.indent-increase",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphIndent() == true) return;
                editor.TryApplyActiveTableCellParagraphIndent();
            }));
        registry.Register("freep.indent-decrease",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true) return;
                editor.TryApplyActiveTableCellParagraphOutdent();
            }));
        registry.Register("freep.increase-indent",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphIndent() == true) return;
                editor.TryApplyActiveTableCellParagraphIndent();
            }));
        registry.Register("freep.decrease-indent",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true) return;
                editor.TryApplyActiveTableCellParagraphOutdent();
            }));

        // ── Clipboard — Wave 5B / 10B ─────────────────────────────────────────────
        // When osClipboard is provided (MainWindow injects it), Copy and Cut also push
        // content to the OS clipboard (PNG image + plain text); Paste checks OS first.

        registry.Register("freep.copy",
            new ActionRibbonCommand(() => WpfClipboardCommands.Copy(editor, osClipboard)));

        registry.Register("freep.cut",
            new ActionRibbonCommand(() => WpfClipboardCommands.Cut(editor, osClipboard)));

        registry.Register("freep.paste",
            new ActionRibbonCommand(() =>
            {
                if (osClipboard is not null)
                    osClipboard.Paste(editor, preferOsClipboard: true);
                else
                    editor.Paste();
            }));

        // ── Format Painter — Wave 5B ─────────────────────────────────────────────
        // Single-click mode: copies formatting from the first selected shape, then immediately
        // applies it to the rest of the multi-selection.
        // With one selected shape, the canvas gesture handler arms the source-then-target
        // interaction; multi-selection keeps the immediate source-to-selection behavior.
        registry.Register("freep.format-painter",
            new ActionRibbonCommand(() =>
            {
                if (editor.SelectedShapeIds.Count == 1 &&
                    getSlideCanvas?.Invoke()?.BeginFormatPainter() == true)
                    return;

                // Preserve the existing one-click multi-selection behavior: the first selected
                // shape is the source and all other selected shapes are painted immediately.
                editor.CopyFormatting();
                editor.ApplyFormattingToSelection();
            }));

        registry.Register(
            PresentationDesignCommandPlanner.LayoutCommandId,
            new ActionRibbonCommand(() =>
                ApplyDesignCommand(editor, PresentationDesignCommandPlanner.LayoutPlan, onCustomSlideSize, onLayoutPicker)));

        // ── Font family — Wave 5B / 10A ───────────────────────────────────────────
        // When the in-canvas editor is active, apply to the RichTextBox selection;
        // otherwise apply to the whole-shape selection.
        registry.Register("freep.font-family",
            new ContextRibbonCommand(ctx =>
            {
                var family = ctx.SelectedValue;
                if (string.IsNullOrEmpty(family)) return;
                if (RouteToActiveRichEditor(
                        getSlideCanvas?.Invoke(),
                        e => e.ApplyFont(family),
                        e => e.ApplyFont(family)))
                    return;
                editor.SetFontFamilyOnSelection(family);
            }));

        registry.Register("freep.font-size",
            new ContextRibbonCommand(ctx =>
            {
                if (!TryGetRibbonFontSize(ctx, out double sizePt)) return;
                if (RouteToActiveRichEditor(
                        getSlideCanvas?.Invoke(),
                        e => e.ApplyFontSize(sizePt),
                        e => e.ApplyFontSize(sizePt)))
                    return;
                if (editor.TryApplyActiveTableCellFontSize(sizePt)) return;
                editor.SetFontSizeOnSelection(sizePt);
            }));

        registry.Register("freep.font-color",
            new ContextRibbonCommand(ctx =>
            {
                if (!TryGetRibbonFontColor(ctx, out var color)) return;
                if (RouteToActiveRichEditor(
                        getSlideCanvas?.Invoke(),
                        e => e.ApplyColor(color),
                        e => e.ApplyColor(color)))
                    return;
                if (editor.TryApplyActiveTableCellColor(color)) return;
                editor.SetColorOnSelection(color);
            }));

        registry.Register("freep.text-autofit",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TextAutoFitOptionParser.TryParse(selection, out var kind))
                    return;

                editor.SetTextAutoFitOnSelection(kind);
            }));

        registry.Register("freep.text-direction",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TextVerticalTypeOptionParser.TryParse(selection, out var verticalType))
                    return;

                if (editor.TryApplyActiveTableCellTextVerticalType(verticalType))
                    return;
                editor.SetTextVerticalTypeOnSelection(verticalType);
            }));

        registry.Register("freep.text-columns",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TextColumnCountOptionParser.TryParse(selection, out var count))
                    return;

                editor.SetTextColumnCountOnSelection(count);
            }));

        registry.Register("freep.text-column-spacing",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TextColumnSpacingOptionParser.TryParse(selection, out var spacingEmu))
                    return;

                editor.SetTextColumnSpacingOnSelection(spacingEmu);
            }));

        registry.Register("freep.table-cell-fill",
            new ContextRibbonCommand(ctx =>
            {
                if (!TryGetRibbonFontColor(ctx, out var color)) return;
                editor.TryApplyActiveTableCellFill(color);
            }));

        registry.Register("freep.table-cell-anchor",
            new ContextRibbonCommand(ctx =>
            {
                if (!TryGetRibbonTableCellAnchor(ctx, out var anchor)) return;
                editor.TryApplyActiveTableCellAnchor(anchor);
            }));

        registry.Register("freep.table-cell-border",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TableCellBorderOptionParser.TryParse(selection, out var side, out var outline))
                    return;

                editor.TryApplyActiveTableCellBorder(side, outline);
            }));

        registry.Register("freep.table-cell-inset",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TableCellInsetOptionParser.TryParse(selection, out var side, out var insetPt))
                    return;

                editor.TryApplyActiveTableCellInset(side, insetPt);
            }));

        registry.Register("freep.table-row-height",
            new ContextRibbonCommand(ctx =>
            {
                if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                    value is not string selection ||
                    !TableRowHeightOptionParser.TryParse(selection, out var heightEmu))
                    return;

                editor.TryApplyActiveTableRowHeight(heightEmu);
            }));

        // ── Wave 4C: Transitions tab ─────────────────────────────────────────────

        registry.Register(TableCellEditPlanner.MergeCellsCommandId,
            new ActionRibbonCommand(() => editor.TryMergeActiveTableCell()));
        registry.Register(TableCellEditPlanner.SplitCellCommandId,
            new ActionRibbonCommand(() => editor.TrySplitActiveTableCell()));
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableFirstRowCommandId, TableStyleFlagKind.FirstRow);
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableLastRowCommandId, TableStyleFlagKind.LastRow);
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableFirstColCommandId, TableStyleFlagKind.FirstCol);
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableLastColCommandId, TableStyleFlagKind.LastCol);
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableBandRowCommandId, TableStyleFlagKind.BandRow);
        RegisterTableStyleFlagCommand(registry, editor,
            TableCellEditPlanner.TableBandColCommandId, TableStyleFlagKind.BandCol);

        RegisterTransitionCommands(registry, stateStore, editor, onTransitionSound);

        // ── Wave 4C: Slide Show buttons ──────────────────────────────────────────

        // From Beginning — delegates to MainWindow.StartSlideShow(true) via onStartFromStart.
        registry.Register("freep.slideshow.from-beginning",
            new ActionRibbonCommand(() => onStartFromStart?.Invoke()));

        // From Current Slide — delegates to MainWindow.StartSlideShow(false) via onStartFromCurrent.
        registry.Register("freep.slideshow.from-current-slide",
            new ActionRibbonCommand(() => onStartFromCurrent?.Invoke()));

        // Rehearse/Record Timings initialize the shared timing intent before playback.
        registry.Register("freep.slideshow.rehearse-timings",
            new ActionRibbonCommand(() => onRehearseTimings?.Invoke()));
        registry.Register("freep.slideshow.record-timings",
            new ActionRibbonCommand(() => onRecordTimings?.Invoke()));

        registry.Register("freep.slideshow.custom-shows",
            new ActionRibbonCommand(() => onCustomShows?.Invoke()));

        // ── Wave 4C: Animations tab ──────────────────────────────────────────────

        // Animation effects/timing/order/pane route through the shared planner.
        RegisterAnimationCommands(registry, stateStore, editor, onAnimPane);

        // ── Wave 5B: Insert — Tables ─────────────────────────────────────────────

        // ── Wave 5B: Insert — Charts ─────────────────────────────────────────────

        // ── Wave 5B: Design tab — Themes ─────────────────────────────────────────

        RegisterDesignCommands(registry, editor, onCustomSlideSize, onLayoutPicker);

        // ── Wave 5B: Design tab — Slide Size ─────────────────────────────────────



        // ── Wave 10B: Design tab — Custom Slide Size dialog ───────────────────────

        // ── Wave 9B: Chart data editing ───────────────────────────────────────────
        // Enabled only when a chart shape is selected; otherwise silently a no-op.
        registry.Register("freep.chart.edit-data",
            new ActionRibbonCommand(() =>
            {
                // If caller supplied a dedicated open-dialog callback (e.g. MainWindow),
                // use it; otherwise fall back to the no-op.
                if (onEditChartData is not null)
                    onEditChartData();
            }));
        registry.Register(ChartDataDialogPlanner.ChangeChartTypeCommandId,
            new ActionRibbonCommand(() => onEditChartData?.Invoke()));
        foreach (var option in ChartDataDialogPlanner.ChartTypeOptions)
        {
            var chartType = option.Value;
            registry.Register(
                ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(chartType),
                new ActionRibbonCommand(() => editor.ChangeSelectedChartType(chartType)));
        }
        registry.Register(ChartDisplayOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartOptions?.Invoke()));
        registry.Register(ChartAxisOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartAxisOptions?.Invoke()));
        registry.Register(ChartSeriesOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartSeriesOptions?.Invoke()));
        registry.Register(ChartPointOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartPointOptions?.Invoke()));
        registry.Register(ChartLayoutOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartLayoutOptions?.Invoke()));
        registry.Register(ChartDataTableOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartDataTableOptions?.Invoke()));
        registry.Register(ChartBubbleOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartBubbleOptions?.Invoke()));
        registry.Register(ChartPieOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartPieOptions?.Invoke()));
        registry.Register(ChartPlotStyleOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartPlotStyleOptions?.Invoke()));
        registry.Register(Chart3DViewOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChart3DViewOptions?.Invoke()));
        registry.Register(ChartTextOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartTextOptions?.Invoke()));
        registry.Register(ChartAreaOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartAreaOptions?.Invoke()));
        registry.Register(ChartProtectionOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditChartProtectionOptions?.Invoke()));
        registry.Register(ShapeTransparencyPlanner.FillCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedFillTransparency(0)));
        registry.Register(ShapeTransparencyPlanner.OutlineCommandId,
            new ActionRibbonCommand(() => editor.SetSelectedOutlineTransparency(0)));
        foreach (var option in ShapeTransparencyPlanner.Options)
        {
            registry.Register(
                ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Fill, option.Percent),
                new ActionRibbonCommand(() => editor.SetSelectedFillTransparency(option.Percent)));
            registry.Register(
                ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Outline, option.Percent),
                new ActionRibbonCommand(() => editor.SetSelectedOutlineTransparency(option.Percent)));
        }

        // ── Wave 11A: Hyperlinks ──────────────────────────────────────────────────

        // Insert/edit hyperlink — opens HyperlinkDialog (supplied by MainWindow).
        registry.Register("freep.insert-link",
            new ActionRibbonCommand(() => onInsertLink?.Invoke()));

        // Remove hyperlink — clears the shape-level hyperlink on all selected shapes.
        registry.Register("freep.remove-link",
            new ActionRibbonCommand(() =>
            {
                if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplySelectedShapeRunHyperlink(null) == true)
                    return;
                editor.RemoveShapeHyperlink();
            }));

        // ── Wave 12A: Arrange — Group / Ungroup / Z-order / Align / Distribute ────

        registry.Register("freep.arrange.group",
            new ActionRibbonCommand(() => editor.GroupSelectedShapes()));

        registry.Register("freep.arrange.ungroup",
            new ActionRibbonCommand(() => editor.UngroupSelected()));

        foreach (var (commandId, kind) in ShapeChangePlanner.Presets)
        {
            registry.Register(commandId,
                new ActionRibbonCommand(() => editor.ChangeSelectedAutoShapeKind(kind)));
        }

        registry.Register(OleActivationPlanner.OpenEmbeddedObjectCommandId,
            new ActionRibbonCommand(() =>
            {
                OleActivationPlanner.TryOpenInlineFirst(
                    tryOpenInlineEmbeddedObject,
                    () =>
                    {
                        if (editor.SelectedOleObject is not { } ole)
                            return false;

                        if (onOpenEmbeddedObject is { } open)
                            open(ole);
                        else
                            OleActivationService.TryActivate(ole);
                        return true;
                    });
            }));

        registry.Register(PresentationEditPointsModePlanner.CommandId,
            getEditPointsEnabled is not null && setEditPointsEnabled is not null
                ? new EditPointsToggleCommand(stateStore, getEditPointsEnabled, setEditPointsEnabled)
                : new EditorToggleCommand(stateStore, PresentationEditPointsModePlanner.CommandId,
                    () => onEditPoints?.Invoke(), initialChecked: true));

        registry.Register("freep.arrange.bring-to-front",
            new ActionRibbonCommand(() => editor.BringToFront()));

        registry.Register("freep.arrange.bring-forward",
            new ActionRibbonCommand(() => editor.BringForward()));

        registry.Register("freep.arrange.send-backward",
            new ActionRibbonCommand(() => editor.SendBackward()));

        registry.Register("freep.arrange.send-to-back",
            new ActionRibbonCommand(() => editor.SendToBack()));

        registry.Register("freep.arrange.flip-horizontal",
            new ActionRibbonCommand(() => editor.FlipSelectedHorizontal()));

        registry.Register("freep.arrange.flip-vertical",
            new ActionRibbonCommand(() => editor.FlipSelectedVertical()));

        registry.Register("freep.arrange.rotate-left-90",
            new ActionRibbonCommand(() => editor.RotateSelectedLeft90()));

        registry.Register("freep.arrange.rotate-right-90",
            new ActionRibbonCommand(() => editor.RotateSelectedRight90()));

        registry.Register(RotationOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => onEditRotationOptions?.Invoke()));

        registry.Register("freep.arrange.align-left",
            new ActionRibbonCommand(() => editor.AlignLeft()));

        registry.Register("freep.arrange.align-center-h",
            new ActionRibbonCommand(() => editor.AlignCenterH()));

        registry.Register("freep.arrange.align-right",
            new ActionRibbonCommand(() => editor.AlignRight()));

        registry.Register("freep.arrange.align-top",
            new ActionRibbonCommand(() => editor.AlignTop()));

        registry.Register("freep.arrange.align-middle",
            new ActionRibbonCommand(() => editor.AlignMiddle()));

        registry.Register("freep.arrange.align-bottom",
            new ActionRibbonCommand(() => editor.AlignBottom()));

        registry.Register("freep.arrange.align-left-to-slide",
            new ActionRibbonCommand(() => editor.AlignLeftToSlide()));
        registry.Register("freep.arrange.align-center-h-to-slide",
            new ActionRibbonCommand(() => editor.AlignCenterHToSlide()));
        registry.Register("freep.arrange.align-right-to-slide",
            new ActionRibbonCommand(() => editor.AlignRightToSlide()));
        registry.Register("freep.arrange.align-top-to-slide",
            new ActionRibbonCommand(() => editor.AlignTopToSlide()));
        registry.Register("freep.arrange.align-middle-to-slide",
            new ActionRibbonCommand(() => editor.AlignMiddleToSlide()));
        registry.Register("freep.arrange.align-bottom-to-slide",
            new ActionRibbonCommand(() => editor.AlignBottomToSlide()));

        registry.Register("freep.arrange.distribute-h",
            new ActionRibbonCommand(() => editor.DistributeHorizontally()));

        registry.Register("freep.arrange.distribute-v",
            new ActionRibbonCommand(() => editor.DistributeVertically()));

        // ── Wave 12B: Find & Replace ──────────────────────────────────────────────

        registry.Register("freep.find",
            new ActionRibbonCommand(() => onFind?.Invoke()));

        registry.Register("freep.replace",
            new ActionRibbonCommand(() => onFindReplace?.Invoke()));

        RegisterReviewWorkflowCommands(
            registry,
            onReviewCommentsPane,
            onReviewAccessibility,
            onReviewAltText,
            onReviewReadingOrder,
            onReviewProofing,
            onAddComment,
            onEditComment,
            onReplyComment,
            onDeleteComment,
            onPreviousComment,
            onNextComment,
            onResolveComment,
            onReopenComment);
        registry.Register(
            PresentationSelectionPanePlanner.SelectionPaneCommandId,
            new ActionRibbonCommand(() => onSelectionPane?.Invoke()));
        RegisterViewShowCommands(registry, stateStore, getViewShowState, applyViewShowState);
        RegisterViewZoomCommands(registry, getViewZoomState, applyViewZoomState);

        return registry;
    }

    private static void RegisterTableStyleFlagCommand(
        RibbonCommandRegistry registry,
        EditingSession editor,
        string commandId,
        TableStyleFlagKind kind)
    {
        registry.Register(commandId,
            new ActionRibbonCommand(() => editor.ToggleSelectedTableStyleFlag(kind)));
    }

    private static bool ApplyTableCellListPreset(EditingSession editor, string? presetId) =>
        !string.IsNullOrWhiteSpace(presetId) &&
        editor.TryApplyActiveTableCellParagraphListPreset(presetId);

    private static void RegisterListGalleryPresetCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        Func<SlideCanvas?>? getSlideCanvas,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload)
    {
        foreach (var item in PresentationListGalleryPlanner.BuildPlans().SelectMany(plan => plan.Items))
        {
            if (!item.IsEnabled || item.ListPreset is null)
                continue;

            registry.Register(
                item.CommandId,
                new ActionRibbonCommand(() =>
                {
                    if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphListPreset(item.ListPreset) == true) return;
                    editor.TryApplyActiveTableCellParagraphListPreset(item.ListPreset);
                }));
        }

        registry.Register(
            PresentationListGalleryPlanner.ImageBulletCommandId,
            new ActionRibbonCommand(() =>
            {
                var payload = (pickPictureBulletPayload ?? TryPickPictureBulletPayload)();
                if (payload is not null)
                {
                    if (getSlideCanvas?.Invoke()?.TextEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true) return;
                    editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
                }
            }));
    }

    internal static void RegisterSlideObjectInsertionCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        bool includePictureCommand,
        Action? onTablePicker = null)
    {
        foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)
        {
            if (plan.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId && onTablePicker is not null)
            {
                registry.Register(plan.CommandId, new ActionRibbonCommand(onTablePicker));
                continue;
            }

            if (plan.RequiresPicturePayload)
            {
                if (!includePictureCommand)
                {
                    continue;
                }

                registry.Register(plan.CommandId, new ActionRibbonCommand(() =>
                {
                    var payload = TryPickPicturePayload();
                    if (payload is not null)
                    {
                        SlideObjectInsertionPlanner.Apply(editor, plan, payload);
                    }
                }));
                continue;
            }

            if (plan.RequiresMediaPayload)
            {
                if (!includePictureCommand)
                {
                    continue;
                }

                var isVideo = plan.CommandId == SlideObjectInsertionPlanner.VideoCommandId;
                registry.Register(plan.CommandId, new ActionRibbonCommand(() =>
                {
                    var payload = TryPickMediaPayload(isVideo);
                    if (payload is not null)
                    {
                        SlideObjectInsertionPlanner.Apply(editor, plan, mediaPayload: payload);
                    }
                }));
                continue;
            }

            registry.Register(plan.CommandId, new ActionRibbonCommand(() =>
                SlideObjectInsertionPlanner.Apply(editor, plan)));
        }
    }

    private static SlideObjectPicturePayload? TryPickPicturePayload()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg;*.wmf;*.emf|All files|*.*",
            title: "Insert Picture");

        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
        {
            return null;
        }

        try
        {
            var bytes = System.IO.File.ReadAllBytes(result.FileName);
            return SlideObjectInsertionPlanner.CreatePicturePayload(bytes, result.FileName);
        }
        catch
        {
            return null;
        }
    }

    private static SlideObjectMediaPayload? TryPickMediaPayload(bool isVideo)
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: isVideo
                ? $"{PresentationFileTextResources.VideoFileTypeName}|*.mp4;*.mov;*.avi;*.wmv;*.m4v|All files|*.*"
                : $"{PresentationFileTextResources.AudioFileTypeName}|*.mp3;*.m4a;*.wav;*.wma|All files|*.*",
            title: isVideo
                ? PresentationFileTextResources.InsertVideoPickerTitle
                : PresentationFileTextResources.InsertAudioPickerTitle);

        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
        {
            return null;
        }

        try
        {
            return SlideObjectInsertionPlanner.CreateMediaPayload(
                File.ReadAllBytes(result.FileName),
                result.FileName,
                isVideo);
        }
        catch
        {
            return null;
        }
    }

    // ── Transition helpers ────────────────────────────────────────────────────────

    private static PresentationPictureBulletPayload? TryPickPictureBulletPayload()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg|All files|*.*",
            title: "Choose Picture Bullet");

        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
        {
            return null;
        }

        try
        {
            var bytes = System.IO.File.ReadAllBytes(result.FileName);
            return PresentationPictureBulletAuthoringPlanner.CreatePayloadFromFileName(bytes, result.FileName);
        }
        catch
        {
            return null;
        }
    }

    private static void RegisterHeaderFooterCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        Action<HeaderFooterCommandFocus>? onHeaderFooter)
    {
        registry.Register(
            HeaderFooterCommandPlanner.HeaderFooterCommandId,
            new ActionRibbonCommand(() => ExecuteHeaderFooterCommand(
                editor,
                HeaderFooterCommandFocus.HeaderFooter,
                onHeaderFooter)));
        registry.Register(
            HeaderFooterCommandPlanner.DateTimeCommandId,
            new ActionRibbonCommand(() => ExecuteHeaderFooterCommand(
                editor,
                HeaderFooterCommandFocus.DateTime,
                onHeaderFooter)));
        registry.Register(
            HeaderFooterCommandPlanner.SlideNumberCommandId,
            new ActionRibbonCommand(() => ExecuteHeaderFooterCommand(
                editor,
                HeaderFooterCommandFocus.SlideNumber,
                onHeaderFooter)));
    }

    private static void ExecuteHeaderFooterCommand(
        EditingSession editor,
        HeaderFooterCommandFocus focus,
        Action<HeaderFooterCommandFocus>? onHeaderFooter)
    {
        if (onHeaderFooter is not null)
        {
            onHeaderFooter(focus);
            return;
        }

        var state = HeaderFooterCommandPlanner.BuildState(editor);
        HeaderFooterCommandPlanner.TryApply(
            editor,
            HeaderFooterCommandPlanner.BuildDefaultOptions(state, focus),
            out _);
    }

    private static void RegisterTransitionCommands(
        RibbonCommandRegistry registry,
        RibbonStateStore stateStore,
        EditingSession editor,
        Action? onTransitionSound)
    {
        foreach (var plan in PresentationTransitionCommandPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                plan.Intent == PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick
                    ? new TransitionToggleCommand(stateStore, editor, plan)
                    : new ContextRibbonCommand(ctx =>
                        PresentationTransitionCommandPlanner.TryApply(
                            editor,
                            plan,
                            ctx.SelectedValue,
                            onTransitionSound)));
        }
    }

    // ── Animation helpers ─────────────────────────────────────────────────────────

    private static void RegisterDesignCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        Action? onCustomSlideSize,
        Action? onLayoutPicker)
    {
        foreach (var plan in PresentationDesignCommandPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                new ActionRibbonCommand(() =>
                    PresentationDesignCommandPlanner.TryApply(
                        editor,
                        plan,
                        CreateDesignHostCallback(plan, onCustomSlideSize, onLayoutPicker))));
        }
    }

    private static bool ApplyDesignCommand(
        EditingSession editor,
        PresentationDesignCommandPlan plan,
        Action? onCustomSlideSize,
        Action? onLayoutPicker) =>
        PresentationDesignCommandPlanner.TryApply(
            editor,
            plan,
            CreateDesignHostCallback(plan, onCustomSlideSize, onLayoutPicker));

    private static Action<PresentationDesignCommandPlan>? CreateDesignHostCallback(
        PresentationDesignCommandPlan plan,
        Action? onCustomSlideSize,
        Action? onLayoutPicker) =>
        plan.Intent switch
        {
            PresentationDesignCommandIntentKind.RequestCustomSlideSize when onCustomSlideSize is not null =>
                _ => onCustomSlideSize(),
            PresentationDesignCommandIntentKind.RequestLayoutPicker when onLayoutPicker is not null =>
                _ => onLayoutPicker(),
            _ => null,
        };

    private static void RegisterAnimationCommands(
        RibbonCommandRegistry registry,
        RibbonStateStore stateStore,
        EditingSession editor,
        Action? onAnimPane)
    {
        foreach (var plan in PresentationAnimationCommandPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                plan.Intent == PresentationAnimationCommandIntentKind.TogglePane
                    ? new AnimationPaneToggleCommand(stateStore, editor, plan, onAnimPane)
                    : new ContextRibbonCommand(ctx =>
                        PresentationAnimationCommandPlanner.TryApply(editor, plan, ctx.SelectedValue)));
        }
    }

    // ── Wave 10A: active-editor routing ──────────────────────────────────────────
    //
    // This region is the ONLY place in this file that references SlideCanvas for 10A.
    // 10B must not add slideCanvas references outside this region.

    private static void RegisterViewShowCommands(
        RibbonCommandRegistry registry,
        RibbonStateStore stateStore,
        Func<PresentationViewShowState>? getViewShowState,
        Action<PresentationViewShowState>? applyViewShowState)
    {
        foreach (var plan in PresentationViewShowPlanner.BuildPlans(
                     getViewShowState?.Invoke() ?? PresentationViewShowState.Default))
        {
            registry.Register(
                plan.CommandId,
                new ViewShowToggleCommand(
                    stateStore,
                    plan,
                    getViewShowState,
                    applyViewShowState));
        }
    }

    private static void RegisterViewZoomCommands(
        RibbonCommandRegistry registry,
        Func<PresentationViewZoomState>? getViewZoomState,
        Action<PresentationViewZoomState>? applyViewZoomState)
    {
        var localState = PresentationViewZoomState.FitToWindow;
        PresentationViewZoomState CurrentState() => getViewZoomState?.Invoke() ?? localState;

        foreach (var plan in PresentationViewZoomPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                new ContextRibbonCommand(ctx =>
                {
                    var result = PresentationViewZoomPlanner.Execute(
                        CurrentState(),
                        plan,
                        ctx.SelectedValue);
                    localState = result.State;
                    applyViewZoomState?.Invoke(result.State);
                }));
        }
    }

    private static void RegisterReviewWorkflowCommands(
        RibbonCommandRegistry registry,
        Action? onCommentsPane,
        Action? onAccessibility,
        Action? onAltText,
        Action? onReadingOrder,
        Action? onProofing,
        Action? onAddComment,
        Action? onEditComment,
        Action? onReplyComment,
        Action? onDeleteComment,
        Action? onPreviousComment,
        Action? onNextComment,
        Action? onResolveComment,
        Action? onReopenComment)
    {
        registry.Register(
            PresentationReviewWorkflowPlanner.CommentsPaneCommandId,
            new ActionRibbonCommand(() => onCommentsPane?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AccessibilityCommandId,
            new ActionRibbonCommand(() => onAccessibility?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            new ActionRibbonCommand(() => onAltText?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId,
            new ActionRibbonCommand(() => onReadingOrder?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ProofingCommandId,
            new ActionRibbonCommand(() => onProofing?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AddCommentCommandId,
            new ActionRibbonCommand(() => onAddComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.EditCommentCommandId,
            new ActionRibbonCommand(() => onEditComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReplyCommentCommandId,
            new ActionRibbonCommand(() => onReplyComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.DeleteCommentCommandId,
            new ActionRibbonCommand(() => onDeleteComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.PreviousCommentCommandId,
            new ActionRibbonCommand(() => onPreviousComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.NextCommentCommandId,
            new ActionRibbonCommand(() => onNextComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ResolveCommentCommandId,
            new ActionRibbonCommand(() => onResolveComment?.Invoke()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReopenCommentCommandId,
            new ActionRibbonCommand(() => onReopenComment?.Invoke()));
    }

    /// <summary>
    /// Routes a format action to the active in-canvas RichTextBox editor (shape or table-cell),
    /// if one is currently open.  Returns true if the action was routed (caller should skip the
    /// whole-shape fallback); false if no editor is active.
    /// </summary>
    private static bool RouteToActiveRichEditor(
        SlideCanvas?                     canvas,
        Action<InCanvasTextEditor>       shapeAction,
        Action<InCanvasTableCellEditor>  tableAction)
    {
        if (canvas is null) return false;

        // Shape editor takes priority.
        if (canvas.TextEditor?.IsActive == true)
        {
            shapeAction(canvas.TextEditor);
            return true;
        }

        // Table cell editor.
        if (canvas.TableCellEditor?.IsCellRichEditActive == true)
        {
            tableAction(canvas.TableCellEditor);
            return true;
        }

        return false;
    }

    private static bool TryGetRibbonFontSize(RibbonCommandContext ctx, out double sizePt)
    {
        sizePt = 0;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case double d:
                sizePt = d;
                break;
            case float f:
                sizePt = f;
                break;
            case int i:
                sizePt = i;
                break;
            case decimal m:
                sizePt = (double)m;
                break;
            case string s:
                var text = s.Trim();
                if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
                    text = text[..^2].Trim();
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out sizePt))
                    return false;
                break;
            default:
                return false;
        }

        return sizePt > 0 && !double.IsNaN(sizePt) && !double.IsInfinity(sizePt);
    }

    private static bool TryGetRibbonFontColor(RibbonCommandContext ctx, out ThemeAwareColor? color)
    {
        color = null;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case ThemeAwareColor themeColor:
                color = themeColor;
                return true;
            case SrgbColor srgb:
                color = new ThemeAwareColor(srgb);
                return true;
            case string s:
                return TryParseRibbonFontColor(s, out color);
            default:
                return false;
        }
    }

    private static bool TryGetRibbonTableCellAnchor(RibbonCommandContext ctx, out TableCellAnchor? anchor)
    {
        anchor = null;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case TableCellAnchor cellAnchor:
                anchor = cellAnchor;
                return true;
            case string s:
                return TryParseRibbonTableCellAnchor(s, out anchor);
            default:
                return false;
        }
    }

    private static bool TryParseRibbonTableCellAnchor(string? value, out TableCellAnchor? anchor)
    {
        anchor = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "automatic":
            case "auto":
            case "default":
                return true;
            case "top":
                anchor = TableCellAnchor.Top;
                return true;
            case "middle":
            case "center":
            case "centre":
                anchor = TableCellAnchor.Middle;
                return true;
            case "bottom":
                anchor = TableCellAnchor.Bottom;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseRibbonFontColor(string? value, out ThemeAwareColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Equals("automatic", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;

        var hex = text.StartsWith("#", StringComparison.Ordinal) ? text[1..] : text;
        if (hex.Length == 6 &&
            int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(rgb));
            return true;
        }

        color = text.ToLowerInvariant() switch
        {
            "black" => ThemeAwareColor.Black,
            "white" => ThemeAwareColor.White,
            "red" => new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
            "green" => new ThemeAwareColor(SrgbColor.FromRgb(0x008000)),
            "blue" => new ThemeAwareColor(SrgbColor.FromRgb(0x0000FF)),
            "yellow" => new ThemeAwareColor(SrgbColor.FromRgb(0xFFFF00)),
            "orange" => new ThemeAwareColor(SrgbColor.FromRgb(0xF4B183)),
            "purple" => new ThemeAwareColor(SrgbColor.FromRgb(0x7030A0)),
            "dark-red" or "dark red" => new ThemeAwareColor(SrgbColor.FromRgb(0x800000)),
            "dark-blue" or "dark blue" => new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
            _ => null,
        };

        return color is not null;
    }

    // ── Inner helpers ─────────────────────────────────────────────────────────────

    /// <summary>Stateful toggle that routes through the editor and updates the ribbon state store.</summary>
    private sealed class EditorToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly RibbonCommandId _id;
        private readonly Action _toggle;
        private bool _checked;

        public EditorToggleCommand(
            RibbonStateStore stateStore,
            RibbonCommandId id,
            Action toggle,
            bool initialChecked = false)
        {
            _stateStore = stateStore;
            _id         = id;
            _toggle     = toggle;
            _checked    = initialChecked;
        }

        public void Execute(RibbonCommandContext context)
        {
            _toggle();
            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
    }

    private sealed class EditPointsToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly Func<bool> _getEnabled;
        private readonly Action<bool> _setEnabled;
        private readonly RibbonCommandId _id = PresentationEditPointsModePlanner.CommandId;

        public EditPointsToggleCommand(
            RibbonStateStore stateStore,
            Func<bool> getEnabled,
            Action<bool> setEnabled)
        {
            _stateStore = stateStore;
            _getEnabled = getEnabled;
            _setEnabled = setEnabled;
            SyncState();
        }

        public void Execute(RibbonCommandContext context)
        {
            var plan = PresentationEditPointsModePlanner.BuildTogglePlan(_getEnabled());
            _setEnabled(plan.NextIsEnabled);
            SyncState();
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _getEnabled());

        private void SyncState() => _stateStore.SetChecked(_id, GetState().IsChecked);
    }

    private sealed class TransitionToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly EditingSession _editor;
        private readonly PresentationTransitionCommandPlan _plan;
        private readonly RibbonCommandId _id;

        public TransitionToggleCommand(
            RibbonStateStore stateStore,
            EditingSession editor,
            PresentationTransitionCommandPlan plan)
        {
            _stateStore = stateStore;
            _editor = editor;
            _plan = plan;
            _id = plan.CommandId;
            SyncState();
            _editor.Changed += SyncState;
            _editor.CurrentSlideChanged += OnCurrentSlideChanged;
        }

        public void Execute(RibbonCommandContext context)
        {
            if (!PresentationTransitionCommandPlanner.TryApply(_editor, _plan, context.SelectedValue))
            {
                return;
            }

            SyncState();
        }

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: PresentationTransitionCommandPlanner.IsAdvanceOnClickChecked(
                _editor.CurrentSlideTransition));

        private void OnCurrentSlideChanged(object? sender, EventArgs e) => SyncState();

        private void SyncState() => _stateStore.SetChecked(_id, GetState().IsChecked);
    }

    private sealed class AnimationPaneToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly EditingSession _editor;
        private readonly PresentationAnimationCommandPlan _plan;
        private readonly Action? _onAnimPane;
        private readonly RibbonCommandId _id;
        private bool _checked;

        public AnimationPaneToggleCommand(
            RibbonStateStore stateStore,
            EditingSession editor,
            PresentationAnimationCommandPlan plan,
            Action? onAnimPane)
        {
            _stateStore = stateStore;
            _editor = editor;
            _plan = plan;
            _onAnimPane = onAnimPane;
            _id = plan.CommandId;
        }

        public void Execute(RibbonCommandContext context)
        {
            if (!PresentationAnimationCommandPlanner.TryApply(
                    _editor,
                    _plan,
                    context.SelectedValue,
                    _onAnimPane is null ? null : _ => _onAnimPane()))
            {
                return;
            }

            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
    }

    private sealed class ViewShowToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly PresentationViewShowCommandPlan _plan;
        private readonly Func<PresentationViewShowState>? _getState;
        private readonly Action<PresentationViewShowState>? _applyState;
        private PresentationViewShowState _localState;

        public ViewShowToggleCommand(
            RibbonStateStore stateStore,
            PresentationViewShowCommandPlan plan,
            Func<PresentationViewShowState>? getState,
            Action<PresentationViewShowState>? applyState)
        {
            _stateStore = stateStore;
            _plan = plan;
            _getState = getState;
            _applyState = applyState;
            _localState = PresentationViewShowState.Default;
            _stateStore.SetChecked(_plan.CommandId, GetState().IsChecked);
        }

        public void Execute(RibbonCommandContext context)
        {
            var result = PresentationViewShowPlanner.Toggle(CurrentState(), _plan);
            _localState = result.State;
            _applyState?.Invoke(result.State);
            _stateStore.SetChecked(_plan.CommandId, result.IsChecked);
        }

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: PresentationViewShowPlanner.IsChecked(CurrentState(), _plan.Kind));

        private PresentationViewShowState CurrentState() => _getState?.Invoke() ?? _localState;
    }
}
