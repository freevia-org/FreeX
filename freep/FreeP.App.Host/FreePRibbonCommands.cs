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
/// Still stubbed (noted below): freep.layout, freep.anim.trigger / .duration / .delay combo-box
///   live-change, freep.anim.pane toggle.
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
        // Wave 16B: Animation pane toggle.
        Action?             onAnimPane         = null)
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

        RegisterSlideObjectInsertionCommands(registry, editor, includePictureCommand: true);

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
            editor.ToggleBoldOnSelection();
        }));
        registry.Register("freep.italic", new EditorToggleCommand(stateStore, "freep.italic", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplyItalic(), e => e.ApplyItalic())) return;
            editor.ToggleItalicOnSelection();
        }));
        registry.Register("freep.underline", new EditorToggleCommand(stateStore, "freep.underline", () =>
        {
            if (RouteToActiveRichEditor(getSlideCanvas?.Invoke(), e => e.ApplyUnderline(), e => e.ApplyUnderline())) return;
            editor.ToggleUnderlineOnSelection();
        }));

        // ── Clipboard — Wave 5B / 10B ─────────────────────────────────────────────
        // When osClipboard is provided (MainWindow injects it), Copy and Cut also push
        // content to the OS clipboard (PNG image + plain text); Paste checks OS first.

        registry.Register("freep.copy",
            new ActionRibbonCommand(() =>
            {
                editor.CopySelectedShapes();
                osClipboard?.PlaceSelectionOnOsClipboard(editor);
            }));

        registry.Register("freep.cut",
            new ActionRibbonCommand(() =>
            {
                editor.CutSelectedShapes();
                osClipboard?.PlaceSelectionOnOsClipboard(editor);
            }));

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
                editor.CopyFormatting();
                editor.ApplyFormattingToSelection();
            }));

        // ── Layout — STUBBED (no layout model yet) ────────────────────────────────
        registry.Register("freep.layout", new ActionRibbonCommand(() => { /* STUB: layout picker deferred */ }));

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

        // ── Wave 4C: Transitions tab ─────────────────────────────────────────────

        // Transition gallery — set Kind on current slide, preserve other transition properties.
        RegisterTransitionKind(registry, editor, "freep.transition.none",     TransitionKind.None);
        RegisterTransitionKind(registry, editor, "freep.transition.fade",     TransitionKind.Fade);
        RegisterTransitionKind(registry, editor, "freep.transition.push",     TransitionKind.Push);
        RegisterTransitionKind(registry, editor, "freep.transition.wipe",     TransitionKind.Wipe);
        RegisterTransitionKind(registry, editor, "freep.transition.split",    TransitionKind.Split);
        RegisterTransitionKind(registry, editor, "freep.transition.cut",      TransitionKind.Cut);
        RegisterTransitionKind(registry, editor, "freep.transition.cover",    TransitionKind.Cover);
        RegisterTransitionKind(registry, editor, "freep.transition.uncover",  TransitionKind.Uncover);
        RegisterTransitionKind(registry, editor, "freep.transition.blinds",   TransitionKind.Blinds);
        RegisterTransitionKind(registry, editor, "freep.transition.dissolve", TransitionKind.Dissolve);
        RegisterTransitionKind(registry, editor, "freep.transition.zoom",     TransitionKind.Zoom);
        RegisterTransitionKind(registry, editor, "freep.transition.wheel",    TransitionKind.Wheel);

        // Transition timing — Duration combo: 0=500ms, 1=750ms, 2=1000ms, 3=1500ms, 4=2000ms.
        registry.Register("freep.transition.duration", new ActionRibbonCommand(() =>
        {
            // ComboBox selection is not yet fed back via context in Wave 4C; stub.
            /* STUB: Wave 5 will feed the selected index through RibbonCommandContext */
        }));

        // Advance on click toggle.
        registry.Register("freep.transition.advance-on-click",
            new EditorToggleCommand(stateStore, "freep.transition.advance-on-click", () =>
            {
                var t = GetOrCreateTransition(editor);
                t.AdvanceOnClick = !t.AdvanceOnClick;
                editor.SetTransition(t);
            }));

        // Advance after time combo.
        registry.Register("freep.transition.advance-after", new ActionRibbonCommand(() =>
        {
            /* STUB: Wave 5 will wire the selected time value */
        }));

        // Apply To All — copies the current slide's transition to every slide.
        registry.Register("freep.transition.apply-all", new ActionRibbonCommand(() =>
        {
            var currentTransition = editor.CurrentSlideTransition;
            var pres = editor.Presentation;
            for (int i = 0; i < pres.Slides.Count; i++)
            {
                // Clone so each slide owns its own object; null clears any existing transition.
                SlideTransition? copy = currentTransition is null ? null : new SlideTransition
                {
                    Kind           = currentTransition.Kind,
                    Direction      = currentTransition.Direction,
                    DurationMs     = currentTransition.DurationMs,
                    AdvanceOnClick = currentTransition.AdvanceOnClick,
                    AdvanceAfterMs = currentTransition.AdvanceAfterMs,
                };
                pres.Slides[i].Transition = copy;
            }
        }));

        // ── Wave 4C: Slide Show buttons ──────────────────────────────────────────

        // From Beginning — delegates to MainWindow.StartSlideShow(true) via onStartFromStart.
        registry.Register("freep.slideshow.from-beginning",
            new ActionRibbonCommand(() => onStartFromStart?.Invoke()));

        // From Current Slide — delegates to MainWindow.StartSlideShow(false) via onStartFromCurrent.
        registry.Register("freep.slideshow.from-current-slide",
            new ActionRibbonCommand(() => onStartFromCurrent?.Invoke()));

        // ── Wave 4C: Animations tab ──────────────────────────────────────────────

        // Entrance effects — AddAnimation with Kind=Entrance + appropriate Preset.
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.appear", AnimationPreset.Appear);
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.fade",   AnimationPreset.Fade);
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.fly-in", AnimationPreset.FlyIn);
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.wipe",   AnimationPreset.Wipe);
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.zoom",   AnimationPreset.Zoom);
        RegisterEntranceAnim(registry, editor, "freep.anim.entrance.split",  AnimationPreset.Split);

        // Emphasis effects.
        RegisterEmphasisAnim(registry, editor, "freep.anim.emphasis.pulse",       AnimationPreset.Pulse);
        RegisterEmphasisAnim(registry, editor, "freep.anim.emphasis.spin",        AnimationPreset.Spin);
        RegisterEmphasisAnim(registry, editor, "freep.anim.emphasis.grow-shrink", AnimationPreset.Grow);

        // Exit effects.
        RegisterExitAnim(registry, editor, "freep.anim.exit.disappear", AnimationPreset.Appear);
        RegisterExitAnim(registry, editor, "freep.anim.exit.fade-out",  AnimationPreset.Fade);
        RegisterExitAnim(registry, editor, "freep.anim.exit.fly-out",   AnimationPreset.FlyIn);

        // No animation — removes the first animation that targets the selected shape.
        registry.Register("freep.anim.none", new ActionRibbonCommand(() =>
        {
            var animations = editor.CurrentSlideAnimations;
            var selectedIds = editor.SelectedShapeIds;
            if (selectedIds.Count == 0) return;
            var targetId = selectedIds[0];
            // Walk backwards to keep indices valid after removal.
            for (int i = animations.Count - 1; i >= 0; i--)
            {
                if (animations[i].ShapeId == targetId)
                    editor.RemoveAnimation(i);
            }
        }));

        // Timing: trigger combo (STUB — full implementation deferred to Wave 5).
        registry.Register("freep.anim.trigger", new ActionRibbonCommand(() =>
        {
            /* STUB: Wave 5 will read the selected item and call editor.SetAnimation() */
        }));

        // Timing: duration + delay combos (STUBs).
        registry.Register("freep.anim.duration", new ActionRibbonCommand(() => { /* STUB */ }));
        registry.Register("freep.anim.delay",    new ActionRibbonCommand(() => { /* STUB */ }));

        // Reorder animations — Move Earlier / Move Later.
        registry.Register("freep.anim.move-earlier", new ActionRibbonCommand(() =>
        {
            var animations  = editor.CurrentSlideAnimations;
            var selectedIds = editor.SelectedShapeIds;
            if (selectedIds.Count == 0 || animations.Count == 0) return;
            var targetId = selectedIds[0];
            var idx = FindLastAnimationIndex(animations, targetId);
            if (idx > 0)
                editor.MoveAnimation(idx, idx - 1);
        }));

        registry.Register("freep.anim.move-later", new ActionRibbonCommand(() =>
        {
            var animations  = editor.CurrentSlideAnimations;
            var selectedIds = editor.SelectedShapeIds;
            if (selectedIds.Count == 0 || animations.Count == 0) return;
            var targetId = selectedIds[0];
            var idx = FindLastAnimationIndex(animations, targetId);
            if (idx >= 0 && idx < animations.Count - 1)
                editor.MoveAnimation(idx, idx + 1);
        }));

        // Animation Pane toggle — Wave 16B: wired to MainWindow.ToggleAnimationPane().
        registry.Register("freep.anim.pane",
            new EditorToggleCommand(stateStore, "freep.anim.pane", () =>
            {
                onAnimPane?.Invoke();
            }));

        // ── Wave 5B: Insert — Tables ─────────────────────────────────────────────

        // ── Wave 5B: Insert — Charts ─────────────────────────────────────────────

        // ── Wave 5B: Design tab — Themes ─────────────────────────────────────────

        registry.Register("freep.theme.office",
            new ActionRibbonCommand(() => editor.SetTheme(BuiltInThemes.Id.Office)));

        registry.Register("freep.theme.berlin",
            new ActionRibbonCommand(() => editor.SetTheme(BuiltInThemes.Id.Berlin)));

        registry.Register("freep.theme.facet",
            new ActionRibbonCommand(() => editor.SetTheme(BuiltInThemes.Id.Facet)));

        registry.Register("freep.theme.ion",
            new ActionRibbonCommand(() => editor.SetTheme(BuiltInThemes.Id.Ion)));

        registry.Register("freep.theme.slice",
            new ActionRibbonCommand(() => editor.SetTheme(BuiltInThemes.Id.Slice)));

        // ── Wave 5B: Design tab — Slide Size ─────────────────────────────────────

        registry.Register("freep.slide-size-16x9",
            new ActionRibbonCommand(() => editor.SetSlideSize16x9()));

        registry.Register("freep.slide-size-4x3",
            new ActionRibbonCommand(() => editor.SetSlideSize4x3()));

        // ── Wave 10B: Design tab — Custom Slide Size dialog ───────────────────────
        registry.Register("freep.slide-size-custom",
            new ActionRibbonCommand(() => onCustomSlideSize?.Invoke()));

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

        return registry;
    }

    internal static void RegisterSlideObjectInsertionCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        bool includePictureCommand)
    {
        foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)
        {
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
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Insert Picture",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg|All files|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            var bytes = System.IO.File.ReadAllBytes(dlg.FileName);
            return SlideObjectInsertionPlanner.CreatePicturePayload(bytes, dlg.FileName);
        }
        catch
        {
            return null;
        }
    }

    // ── Transition helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current slide's transition if it exists, or a new default one.
    /// Does NOT call SetTransition — the caller must do so after mutating.
    /// </summary>
    private static SlideTransition GetOrCreateTransition(EditingSession editor)
        => editor.CurrentSlideTransition is not null
            ? new SlideTransition
              {
                  Kind           = editor.CurrentSlideTransition.Kind,
                  Direction      = editor.CurrentSlideTransition.Direction,
                  DurationMs     = editor.CurrentSlideTransition.DurationMs,
                  AdvanceOnClick = editor.CurrentSlideTransition.AdvanceOnClick,
                  AdvanceAfterMs = editor.CurrentSlideTransition.AdvanceAfterMs,
              }
            : new SlideTransition();

    private static void RegisterTransitionKind(
        RibbonCommandRegistry registry,
        EditingSession        editor,
        string                id,
        TransitionKind        kind)
    {
        registry.Register(id, new ActionRibbonCommand(() =>
        {
            if (kind == TransitionKind.None)
            {
                editor.SetTransition(null);
            }
            else
            {
                var t = GetOrCreateTransition(editor);
                t.Kind = kind;
                editor.SetTransition(t);
            }
        }));
    }

    // ── Animation helpers ─────────────────────────────────────────────────────────

    private static void RegisterEntranceAnim(
        RibbonCommandRegistry registry,
        EditingSession        editor,
        string                id,
        AnimationPreset       preset)
        => registry.Register(id, new ActionRibbonCommand(() =>
            editor.AddAnimation(0, new ShapeAnimation
            {
                Kind       = AnimationKind.Entrance,
                Preset     = preset,
                Trigger    = AnimationTrigger.OnClick,
                DurationMs = 500,
            })));

    private static void RegisterEmphasisAnim(
        RibbonCommandRegistry registry,
        EditingSession        editor,
        string                id,
        AnimationPreset       preset)
        => registry.Register(id, new ActionRibbonCommand(() =>
            editor.AddAnimation(0, new ShapeAnimation
            {
                Kind       = AnimationKind.Emphasis,
                Preset     = preset,
                Trigger    = AnimationTrigger.OnClick,
                DurationMs = 500,
            })));

    private static void RegisterExitAnim(
        RibbonCommandRegistry registry,
        EditingSession        editor,
        string                id,
        AnimationPreset       preset)
        => registry.Register(id, new ActionRibbonCommand(() =>
            editor.AddAnimation(0, new ShapeAnimation
            {
                Kind       = AnimationKind.Exit,
                Preset     = preset,
                Trigger    = AnimationTrigger.OnClick,
                DurationMs = 500,
            })));

    // ── Wave 10A: active-editor routing ──────────────────────────────────────────
    //
    // This region is the ONLY place in this file that references SlideCanvas for 10A.
    // 10B must not add slideCanvas references outside this region.

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

    /// <summary>Finds the last animation index targeting <paramref name="shapeId"/>; -1 if not found.</summary>
    private static int FindLastAnimationIndex(
        IReadOnlyList<ShapeAnimation> animations,
        uint shapeId)
    {
        for (int i = animations.Count - 1; i >= 0; i--)
            if (animations[i].ShapeId == shapeId) return i;
        return -1;
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
}
