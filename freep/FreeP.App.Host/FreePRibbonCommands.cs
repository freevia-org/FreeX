using System.Globalization;
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
/// Still stubbed (noted below): freep.anim.trigger / .duration / .delay combo-box live-change.
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
    public static RibbonCommandRegistry Build(
        RibbonStateStore    stateStore,
        EditingSession      editor,
        Action?             onStartFromStart   = null,
        Action?             onStartFromCurrent = null,
        Action?             onEditChartData    = null,
        Func<SlideCanvas?>? getSlideCanvas     = null,
        Action?             onCustomSlideSize  = null,
        OsClipboardService? osClipboard        = null,
        Action?             onInsertLink       = null,
        // Wave 12B: Find & Replace dialog launchers.
        Action?             onFind             = null,
        Action?             onFindReplace      = null,
        Action?             onReviewCommentsPane = null,
        Action?             onReviewAccessibility = null,
        Action?             onReviewAltText = null,
        Action?             onReviewReadingOrder = null,
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
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload = null)
    {
        var registry = new RibbonCommandRegistry();

        // ── Slide management ─────────────────────────────────────────────────────

        registry.Register("freep.new-slide",
            new ActionRibbonCommand(() => editor.InsertSlide()));

        registry.Register("freep.duplicate-slide",
            new ActionRibbonCommand(() => editor.DuplicateCurrentSlide()));

        registry.Register("freep.delete-slide",
            new ActionRibbonCommand(() => editor.DeleteCurrentSlide()));

        // ── Insert shapes ────────────────────────────────────────────────────────

        RegisterSlideObjectInsertionCommands(registry, editor, includePictureCommand: true, onTablePicker);
        RegisterHeaderFooterCommands(registry, editor, onHeaderFooter);

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

        registry.Register("freep.paragraph.align-left",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Left)));
        registry.Register("freep.paragraph.align-center",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Center)));
        registry.Register("freep.paragraph.align-right",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Right)));
        registry.Register("freep.paragraph.align-justify",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Justify)));
        registry.Register("freep.bullets",
            new ContextRibbonCommand(ctx =>
            {
                if (ApplyTableCellListPreset(editor, ctx.SelectedValue)) return;
                editor.TryApplyActiveTableCellParagraphBulletToggle();
            }));
        registry.Register("freep.numbering",
            new ContextRibbonCommand(ctx =>
            {
                if (ApplyTableCellListPreset(editor, ctx.SelectedValue)) return;
                editor.TryApplyActiveTableCellParagraphNumberingToggle();
            }));
        RegisterListGalleryPresetCommands(registry, editor, pickPictureBulletPayload);
        registry.Register("freep.indent-increase",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphIndent()));
        registry.Register("freep.indent-decrease",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphOutdent()));
        registry.Register("freep.increase-indent",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphIndent()));
        registry.Register("freep.decrease-indent",
            new ActionRibbonCommand(() => editor.TryApplyActiveTableCellParagraphOutdent()));

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
        // NOTE: full "click source → click target" canvas mode is deferred (requires a
        // modal interaction state in the gesture handler).
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

        // ── Wave 4C: Transitions tab ─────────────────────────────────────────────

        RegisterTransitionCommands(registry, stateStore, editor);

        // ── Wave 4C: Slide Show buttons ──────────────────────────────────────────

        // From Beginning — delegates to MainWindow.StartSlideShow(true) via onStartFromStart.
        registry.Register("freep.slideshow.from-beginning",
            new ActionRibbonCommand(() => onStartFromStart?.Invoke()));

        // From Current Slide — delegates to MainWindow.StartSlideShow(false) via onStartFromCurrent.
        registry.Register("freep.slideshow.from-current-slide",
            new ActionRibbonCommand(() => onStartFromCurrent?.Invoke()));

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

        // ── Wave 11A: Hyperlinks ──────────────────────────────────────────────────

        // Insert/edit hyperlink — opens HyperlinkDialog (supplied by MainWindow).
        registry.Register("freep.insert-link",
            new ActionRibbonCommand(() => onInsertLink?.Invoke()));

        // Remove hyperlink — clears the shape-level hyperlink on all selected shapes.
        registry.Register("freep.remove-link",
            new ActionRibbonCommand(() => editor.RemoveShapeHyperlink()));

        // ── Wave 12A: Arrange — Group / Ungroup / Z-order / Align / Distribute ────

        registry.Register("freep.arrange.group",
            new ActionRibbonCommand(() => editor.GroupSelectedShapes()));

        registry.Register("freep.arrange.ungroup",
            new ActionRibbonCommand(() => editor.UngroupSelected()));

        registry.Register("freep.arrange.bring-to-front",
            new ActionRibbonCommand(() => editor.BringToFront()));

        registry.Register("freep.arrange.bring-forward",
            new ActionRibbonCommand(() => editor.BringForward()));

        registry.Register("freep.arrange.send-backward",
            new ActionRibbonCommand(() => editor.SendBackward()));

        registry.Register("freep.arrange.send-to-back",
            new ActionRibbonCommand(() => editor.SendToBack()));

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
        RegisterViewShowCommands(registry, stateStore, getViewShowState, applyViewShowState);
        RegisterViewZoomCommands(registry, getViewZoomState, applyViewZoomState);

        return registry;
    }

    private static bool ApplyTableCellListPreset(EditingSession editor, string? presetId) =>
        !string.IsNullOrWhiteSpace(presetId) &&
        editor.TryApplyActiveTableCellParagraphListPreset(presetId);

    private static void RegisterListGalleryPresetCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload)
    {
        foreach (var item in PresentationListGalleryPlanner.BuildPlans().SelectMany(plan => plan.Items))
        {
            if (!item.IsEnabled || item.ListPreset is null)
                continue;

            registry.Register(
                item.CommandId,
                new ActionRibbonCommand(() =>
                    editor.TryApplyActiveTableCellParagraphListPreset(item.ListPreset)));
        }

        registry.Register(
            PresentationListGalleryPlanner.ImageBulletCommandId,
            new ActionRibbonCommand(() =>
            {
                var payload = (pickPictureBulletPayload ?? TryPickPictureBulletPayload)();
                if (payload is not null)
                    editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
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
        EditingSession editor)
    {
        foreach (var plan in PresentationTransitionCommandPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                plan.Intent == PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick
                    ? new TransitionToggleCommand(stateStore, editor, plan)
                    : new ContextRibbonCommand(ctx =>
                        PresentationTransitionCommandPlanner.TryApply(editor, plan, ctx.SelectedValue)));
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

    /// <summary>
    /// Stateful toggle that routes through the editor and updates the ribbon state store.
    /// The checked state is a local indicator only; in a full implementation the editor would
    /// expose selection-format query methods that 3C can feed back.
    /// </summary>
    private sealed class EditorToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly RibbonCommandId _id;
        private readonly Action _toggle;
        private bool _checked;

        public EditorToggleCommand(RibbonStateStore stateStore, RibbonCommandId id, Action toggle)
        {
            _stateStore = stateStore;
            _id         = id;
            _toggle     = toggle;
        }

        public void Execute(RibbonCommandContext context)
        {
            _toggle();
            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
    }

    private sealed class TransitionToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly EditingSession _editor;
        private readonly PresentationTransitionCommandPlan _plan;
        private readonly RibbonCommandId _id;
        private bool _checked;

        public TransitionToggleCommand(
            RibbonStateStore stateStore,
            EditingSession editor,
            PresentationTransitionCommandPlan plan)
        {
            _stateStore = stateStore;
            _editor = editor;
            _plan = plan;
            _id = plan.CommandId;
        }

        public void Execute(RibbonCommandContext context)
        {
            if (!PresentationTransitionCommandPlanner.TryApply(_editor, _plan, context.SelectedValue))
            {
                return;
            }

            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
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
