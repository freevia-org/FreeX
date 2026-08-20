using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 16B: Unit tests for AnimationPane and its MainWindow / ribbon wiring.
///
/// All UI tests use [StaFact] (STA thread required for WPF controls).
/// Non-UI tests use [Fact].
/// </summary>
public sealed class AnimationPaneTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static EditingSession MakeSessionWithAnimations(int slideCount = 1)
    {
        var pres = new Presentation();
        for (int i = 0; i < slideCount; i++)
        {
            // Use a blank slide (no Title set) so the auto-placeholder code in Slide.Title
            // does NOT inject a shape at id=1 that would shadow our test shapes.
            var slide = new Slide();

            // Add two named shapes with ids that won't conflict with any auto-injected placeholder.
            slide.Shapes.Add(new SlideShape
            {
                Id = 10u, Name = "Title Box",
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            });
            slide.Shapes.Add(new SlideShape
            {
                Id = 20u, Name = "Content Box",
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            });

            // Two animations targeting the two shapes.
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId    = 10u,
                Kind       = AnimationKind.Entrance,
                Preset     = AnimationPreset.Appear,
                Trigger    = AnimationTrigger.OnClick,
                DurationMs = 500,
            });
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId    = 20u,
                Kind       = AnimationKind.Entrance,
                Preset     = AnimationPreset.Fade,
                Trigger    = AnimationTrigger.WithPrevious,
                DurationMs = 1000,
            });

            pres.Slides.Add(slide);
        }

        var bus = new PresentationCommandBus(pres);
        return new EditingSession(pres, bus);
    }

    // ── Construction + row count ──────────────────────────────────────────────────

    /// <summary>
    /// AnimationPane constructs over a session with 2 animations and shows 2 rows.
    /// </summary>
    [StaFact]
    public void AnimationPane_Constructs_And_Shows_CorrectRowCount()
    {
        var editor = MakeSessionWithAnimations();

        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        pane.Should().NotBeNull();
        // The list is in a StackPanel inside a ScrollViewer inside a Grid inside a Border.
        int rowCount = CountAnimationRows(pane);
        rowCount.Should().Be(2, "there are 2 animations on the current slide");
    }

    // ── Shape name resolution ─────────────────────────────────────────────────────

    /// <summary>
    /// Each row shows the correct shape name resolved from the current slide's Shapes list.
    /// </summary>
    [StaFact]
    public void AnimationPane_Rows_Show_CorrectShapeNames()
    {
        var editor = MakeSessionWithAnimations();

        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var names = CollectRowShapeNames(pane);
        names.Should().HaveCount(2);
        names[0].Should().Be("Title Box",   "first animation targets shape id 1");
        names[1].Should().Be("Content Box", "second animation targets shape id 2");
    }

    // ── Move up ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicking Move Up on row 1 reorders the animations (row 0 becomes the second).
    /// </summary>
    [StaFact]
    public void AnimationPane_MoveUp_Reorders_Animations()
    {
        var editor = MakeSessionWithAnimations();

        // Before: [ShapeId=10, ShapeId=20]
        editor.CurrentSlideAnimations[0].ShapeId.Should().Be(10u);
        editor.CurrentSlideAnimations[1].ShapeId.Should().Be(20u);

        // Move row 1 (ShapeId=20) up → should become index 0.
        editor.MoveAnimation(1, 0);

        // After: [ShapeId=20, ShapeId=10]
        editor.CurrentSlideAnimations[0].ShapeId.Should().Be(20u, "row 1 moved to index 0");
        editor.CurrentSlideAnimations[1].ShapeId.Should().Be(10u);
    }

    // ── Remove ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removing the first animation leaves one row and the correct animation.
    /// </summary>
    [StaFact]
    public void AnimationPane_Remove_DeletesOneAnimation()
    {
        var editor = MakeSessionWithAnimations();

        // Remove animation at index 0 (ShapeId=10).
        editor.RemoveAnimation(0);

        var anims = editor.CurrentSlideAnimations;
        anims.Should().HaveCount(1, "one animation was removed");
        anims[0].ShapeId.Should().Be(20u, "the remaining animation targets shape 20");

        // The pane rebuilds on Changed; create a fresh pane to verify.
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));
        int rowCount = CountAnimationRows(pane);
        rowCount.Should().Be(1);
    }

    // ── Trigger change ────────────────────────────────────────────────────────────

    /// <summary>
    /// Changing a trigger via Editor.SetAnimation updates the model correctly.
    /// </summary>
    [Fact]
    public void SetAnimation_Trigger_UpdatesModel()
    {
        var editor = MakeSessionWithAnimations();

        var original = editor.CurrentSlideAnimations[0];
        // Animation 0 is OnClick (set in the helper).
        original.Trigger.Should().Be(AnimationTrigger.OnClick);

        // Simulate what the pane's trigger ComboBox does.
        var updated = new ShapeAnimation
        {
            ShapeId    = original.ShapeId,
            Kind       = original.Kind,
            Preset     = original.Preset,
            Trigger    = AnimationTrigger.AfterPrevious,  // changed
            DurationMs = original.DurationMs,
        };
        editor.SetAnimation(0, updated);

        editor.CurrentSlideAnimations[0].Trigger
            .Should().Be(AnimationTrigger.AfterPrevious, "SetAnimation should update the trigger");
    }

    /// <summary>
    /// SetAnimation via editor is undoable (the bus records it).
    /// </summary>
    [Fact]
    public void SetAnimation_Trigger_IsUndoable()
    {
        var editor = MakeSessionWithAnimations();

        // Animation 0 starts OnClick.
        var updated = new ShapeAnimation
        {
            ShapeId    = editor.CurrentSlideAnimations[0].ShapeId,
            Kind       = editor.CurrentSlideAnimations[0].Kind,
            Preset     = editor.CurrentSlideAnimations[0].Preset,
            Trigger    = AnimationTrigger.WithPrevious,
            DurationMs = editor.CurrentSlideAnimations[0].DurationMs,
        };
        editor.SetAnimation(0, updated);
        editor.CurrentSlideAnimations[0].Trigger.Should().Be(AnimationTrigger.WithPrevious);

        editor.Undo();
        editor.CurrentSlideAnimations[0].Trigger
            .Should().Be(AnimationTrigger.OnClick, "undo should revert the trigger change");
    }

    // ── Empty slide ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AnimationPane over a slide with no animations shows the empty-state message.
    /// </summary>
    [StaFact]
    public void AnimationPane_EmptySlide_ShowsNoRows()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Title = "Empty" });
        var bus = new PresentationCommandBus(pres);
        var editor = new EditingSession(pres, bus);

        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        int rowCount = CountAnimationRows(pane);
        rowCount.Should().Be(0, "an empty slide has no animation rows");
    }

    // ── Ribbon toggle shows/hides the pane ───────────────────────────────────────

    /// <summary>
    /// Calling ToggleAnimationPane twice: first call shows the pane, second hides it.
    /// </summary>
    [StaFact]
    public void MainWindow_ToggleAnimationPane_ShowsAndHidesPaneHost()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            // Initially collapsed.
            var host = window.AnimPaneHostForTest;
            host.Should().NotBeNull();
            host!.Visibility.Should().Be(Visibility.Collapsed, "pane is hidden by default");

            // First toggle → visible.
            window.ToggleAnimationPane();
            host.Visibility.Should().Be(Visibility.Visible, "first toggle shows the pane");

            // Second toggle → hidden again.
            window.ToggleAnimationPane();
            host.Visibility.Should().Be(Visibility.Collapsed, "second toggle hides the pane");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// After ToggleAnimationPane is called, the pane child is an AnimationPane instance.
    /// </summary>
    [StaFact]
    public void MainWindow_ToggleAnimationPane_CreatesPaneChild()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.ToggleAnimationPane();

            var host = window.AnimPaneHostForTest!;
            host.Child.Should().BeOfType<AnimationPane>("the pane child should be an AnimationPane");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Slide-changed event refreshes the pane ────────────────────────────────────

    /// <summary>
    /// When the current slide changes, AnimationPane.Rebuild is re-invoked so the rows
    /// match the new slide's animation count.
    /// </summary>
    [StaFact]
    public void AnimationPane_RefreshesOn_CurrentSlideChanged()
    {
        var pres = new Presentation();

        // Slide 0: 2 animations.
        var slide0 = new Slide();
        slide0.Shapes.Add(new SlideShape { Id = 10u, Name = "A", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200 });
        slide0.Animations.Add(new ShapeAnimation { ShapeId = 10u, Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear, Trigger = AnimationTrigger.OnClick, DurationMs = 500 });
        slide0.Animations.Add(new ShapeAnimation { ShapeId = 10u, Kind = AnimationKind.Exit,
            Preset = AnimationPreset.Fade, Trigger = AnimationTrigger.AfterPrevious, DurationMs = 500 });
        pres.Slides.Add(slide0);

        // Slide 1: 0 animations.
        pres.Slides.Add(new Slide { Title = "S1" });

        var bus    = new PresentationCommandBus(pres);
        var editor = new EditingSession(pres, bus);

        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        // Initially on slide 0 → 2 rows.
        CountAnimationRows(pane).Should().Be(2);

        // Navigate to slide 1 → 0 rows. The HOST drives the refresh now: "Share FreeP animation pane
        // lifecycle" moved refresh ownership out of the pane's own event subscription and onto
        // MainWindow, which calls Rebuild() through RefreshAnimationPaneAfterNavigation (and after
        // editor change, selection and presentation change -- see MainWindow.WorkareaEndpoint.cs).
        // Driving it the same way here is what keeps this test measuring the production path.
        editor.SelectSlide(1);
        pane.Rebuild();
        CountAnimationRows(pane).Should().Be(0, "pane should refresh when the host rebuilds it after a slide change");
    }

    [StaFact]
    public void AnimationPane_ExposesSharedTimelinePlan()
    {
        var editor = MakeSessionWithAnimations();
        editor.Select(20u);

        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var plan = pane.CurrentTimelinePlanForTest;
        plan.Items.Should().HaveCount(2);
        plan.SelectedIndex.Should().Be(1);
        plan.SelectedItem!.ShapeName.Should().Be("Content Box");
        plan.SelectedItem.EffectText.Should().Be("In: Fade");
        plan.SelectedItem.CanMoveEarlier.Should().BeTrue();
        plan.SelectedItem.CanMoveLater.Should().BeFalse();
        plan.PreviewIntent.CanExecute.Should().BeTrue();
        pane.CurrentPlaybackControlsForTest.Should().HaveCount(4);
        pane.CurrentPlaybackControlsForTest.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected
            && control.IsEnabled
            && control.StartAnimationIndex == 1);
        pane.CurrentPlaybackControlsForTest.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop
            && !control.IsEnabled);
        pane.CurrentWorkflowViewPlanForTest.Heading.Should().Be("Animation Pane - slide 1 (2 animations)");
        pane.CurrentWorkflowViewPlanForTest.Message.Should().Be("Selected: Content Box - In: Fade");
        pane.CurrentWorkflowViewPlanForTest.RowSummaries.Should().Contain(row =>
            row.Contains("2. Content Box - In: Fade", StringComparison.Ordinal)
            && row.Contains("move earlier available", StringComparison.Ordinal));
        pane.CurrentWorkflowViewPlanForTest.PlaybackControlSummaries.Should().Equal(
            "Preview: available",
            "Play From Selected: available",
            "Play All: available",
            "Stop: unavailable");
        pane.CurrentWorkflowEvidencePlanForTest.RowCount.Should().Be(2);
        pane.CurrentWorkflowEvidencePlanForTest.EditableTimingRowCount.Should().Be(2);
        pane.CurrentWorkflowEvidencePlanForTest.HasSelectedRow.Should().BeTrue();
        pane.CurrentWorkflowEvidencePlanForTest.CanPlayFromSelected.Should().BeTrue();
        pane.CurrentWorkflowEvidencePlanForTest.EvidenceLines.Should().Contain(
            "Rows: 2; selected: 2; timing editors: 2; effect-option rows: 0; reorderable rows: 2");
    }

    [StaFact]
    public void AnimationPane_EasingControlsApplyAndUndoSharedMutation()
    {
        var editor = MakeSessionWithAnimations();
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var plan = pane.ApplyAnimationPaneEasingEditForTest(0, "35.5%", "12%");

        plan.ShouldApply.Should().BeTrue();
        editor.CurrentSlideAnimations[0].Acceleration.Should().Be(35500);
        editor.CurrentSlideAnimations[0].Deceleration.Should().Be(12000);
        pane.CurrentTimelinePlanForTest.Items[0].Acceleration.Should().Be(35500);
        pane.CurrentTimelinePlanForTest.Items[0].Deceleration.Should().Be(12000);

        editor.Undo();
        editor.CurrentSlideAnimations[0].Acceleration.Should().BeNull();
        editor.CurrentSlideAnimations[0].Deceleration.Should().BeNull();
    }

    [StaFact]
    public void AnimationPane_FlyIn_ExposesAndAppliesDiagonalEffectOptions()
    {
        var presentation = Presentation.CreateEmpty();
        var shapeId = presentation.Slides[0].Shapes[0].Id;
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Direction = AnimationDirection.FromLeft,
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var options = pane.CurrentTimelinePlanForTest.Items[0].EffectOptions;
        options.Options.Select(option => option.Id).Should().Equal(
            "from-bottom",
            "from-left",
            "from-right",
            "from-top",
            "from-top-left",
            "from-top-right",
            "from-bottom-left",
            "from-bottom-right");

        var mutation = pane.ApplyAnimationPaneEffectOptionEditForTest(0, "from-top-right");

        mutation.ShouldApply.Should().BeTrue();
        mutation.Direction.Should().Be(AnimationDirection.FromTopRight);
        editor.CurrentSlideAnimations[0].Direction.Should().Be(AnimationDirection.FromTopRight);
        pane.CurrentTimelinePlanForTest.Items[0].EffectOptions.SelectedOptionText
            .Should().Be("From Top Right");
    }

    [StaFact]
    public void AnimationPane_Split_UsesSharedFourDirectionOptionsAndMutation()
    {
        var editor = MakeSessionWithAnimations();
        editor.CurrentSlideAnimations[0].Preset = AnimationPreset.Split;
        editor.CurrentSlideAnimations[0].Direction = AnimationDirection.HorizontalIn;
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        pane.CurrentTimelinePlanForTest.Items[0].EffectOptions.Options
            .Select(option => option.DisplayText)
            .Should().Equal("Horizontal In", "Horizontal Out", "Vertical In", "Vertical Out");

        var mutation = pane.ApplyAnimationPaneEffectOptionEditForTest(0, "vertical-out");
        mutation.ShouldApply.Should().BeTrue();
        mutation.Direction.Should().Be(AnimationDirection.VerticalOut);
        editor.CurrentSlideAnimations[0].Direction.Should().Be(AnimationDirection.VerticalOut);
    }

    [StaFact]
    public void AnimationPane_GrowShrink_ExposesAmountOptionsAndUsesUndoableMutation()
    {
        var presentation = Presentation.CreateEmpty();
        var shapeId = presentation.Slides[0].Shapes[0].Id;
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var options = pane.CurrentTimelinePlanForTest.Items[0].EffectOptions;
        options.Options.Select(option => option.DisplayText)
            .Should().Equal("Tiny (25%)", "Smaller (50%)", "Larger (150%)", "Huge (400%)");
        options.SelectedOptionText.Should().Be("Larger (150%)");

        pane.ApplyAnimationPaneEffectOptionEditForTest(0, "amount-400").ShouldApply.Should().BeTrue();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX.Should().Be("400000");
        editor.Undo();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX.Should().Be("150000");
    }

    [StaFact]
    public void AnimationPane_ProjectsSharedPlaybackSessionState()
    {
        var editor = MakeSessionWithAnimations();
        editor.Select(20u);
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var playSession = pane.ExecutePlaybackControlForTest(AnimationPanePlaybackControlKind.PlayFromSelected);
        var workflowEvidence = pane.CurrentPlaybackWorkflowEvidencePlanForTest;

        playSession.State.Should().Be(AnimationPanePlaybackSessionState.Running);
        playSession.StartAnimationIndex.Should().Be(1);
        playSession.Segments.Should().ContainSingle(segment =>
            segment.AnimationIndex == 1
            && segment.ShapeId == 20u
            && segment.RelativeStartMs == 0);
        workflowEvidence.Should().NotBeNull();
        workflowEvidence!.CommandKind.Should().Be(AnimationPanePlaybackControlKind.PlayFromSelected);
        workflowEvidence.SessionState.Should().Be(AnimationPanePlaybackSessionState.Running);
        workflowEvidence.SegmentCount.Should().Be(1);
        workflowEvidence.PlaybackCheckpointCount.Should().Be(0);
        workflowEvidence.HasSharedNoComHostEvidence.Should().BeTrue();
        workflowEvidence.HostRows.Select(row => row.Host)
            .Should()
            .Equal(AnimationPanePlaybackWorkflowHost.Wpf, AnimationPanePlaybackWorkflowHost.Avalonia);
        workflowEvidence.EvidenceLines.Should().Contain(
            "Shared host rows: WPF/Avalonia; PowerPoint COM required: false");
        pane.CurrentPlaybackSessionPlanForTest.Should().BeSameAs(playSession);
        pane.CurrentPlaybackControlsForTest.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop && control.IsEnabled);
        pane.CurrentPlaybackControlsForTest.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected && !control.IsEnabled);

        var stopSession = pane.ExecutePlaybackControlForTest(AnimationPanePlaybackControlKind.Stop);

        stopSession.State.Should().Be(AnimationPanePlaybackSessionState.Stopped);
        stopSession.Segments.Should().BeEmpty();
        pane.CurrentPlaybackControlsForTest.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop && !control.IsEnabled);
    }

    [StaFact]
    public void AnimationPane_ReordersThroughSharedMutationPlan()
    {
        var editor = MakeSessionWithAnimations();
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var plan = pane.MoveAnimationForTest(1, -1);

        plan.Should().Be(new AnimationPaneReorderMutationPlan(
            true,
            1,
            0,
            0,
            "Move animation 2 earlier",
            null));
        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(20u, 10u);
        // The model mutation above is the assertion that matters; the native row projection is
        // refreshed by the host (Rebuild), not by the pane observing the bus -- see the comment in
        // AnimationPane_RefreshesOn_CurrentSlideChanged.
        pane.Rebuild();
        CollectRowShapeNames(pane).Should().Equal(
            "Content Box",
            "Title Box");
        pane.CurrentTimelinePlanForTest.SelectedIndex.Should().Be(0);

        var invalid = pane.MoveAnimationForTest(0, -1);
        invalid.ShouldApply.Should().BeFalse();
        invalid.DisabledReason.Should().Be(AnimationPanePlanner.InvalidReorderMessage);
    }

    [Fact]
    public void AnimationPane_UsesSharedPlannerForPolicy()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Host", "AnimationPane.cs");

        source.Should().Contain("private readonly AnimationPaneSession _session;");
        source.Should().Contain("_session.Refresh()");
        source.Should().Contain("_session.ExecutePlayback(control.Kind)");
        source.Should().Contain("_session.SelectAnimation(capturedIndex)");
        source.Should().Contain("plan.PlaybackControls");
        source.Should().Contain("var effectText = item.EffectText");
        source.Should().Contain("_session.BuildItemControlPlan(item, _onEditMotionPath is not null)");
        source.Should().Contain("controls.EffectOptions.Options");
        source.Should().Contain("controls.WheelSpokes.Options");
        source.Should().Contain("controls.EffectOptions.ResolveOptionId(");
        source.Should().Contain("_session.ApplyEffectOption(animationIndex, optionId)");
        source.Should().Contain("_session.MoveAnimation(animationIndex, offset)");
        source.Should().Contain("_session.RemoveAnimation(animationIndex)");
        source.Should().Contain("_session.ControlSchema.Heading");
        source.Should().Contain("controls.Trigger.Options");
        source.Should().Contain("controls.Repeat.Options");
        source.Should().Contain("Text              = controls.Duration.Text");
        source.Should().Contain("Text              = controls.Delay.Text");
        source.Should().Contain("_session.ApplyTrigger(capturedIndex, triggerCombo.SelectedIndex)");
        source.Should().Contain("_session.ApplyDuration(capturedIndex, durationBox.Text)");
        source.Should().Contain("_session.ApplyDelay(capturedIndex, delayBox.Text)");
        source.Should().Contain("_session.ApplyEasing(animationIndex, accelerationText, decelerationText)");
        source.Should().NotContain("item.EffectOptions.Options");
        source.Should().NotContain("item.EffectOptions.WheelSpokeOptions");
        source.Should().NotContain("AnimationPanePlanner.FormatEasing(");
        source.Should().NotContain("AnimationPanePlanner.FormatRepeat(");
        source.Should().NotContain("AnimationPanePlanner.BuildParagraphBuildMutationPlan(");
        source.Should().NotContain(".GetRequired(AnimationPaneControlKind.");
        source.Should().NotContain("AnimationPanePlanner.");
        source.Should().NotContain("AnimationPanePlanner.BuildPlaybackSessionPlan(");
        source.Should().NotContain("AnimationPanePlanner.TryApplyTimingMutation(");
        source.Should().NotContain("AnimationPanePlanner.TryApplyEasingMutation(");
        source.Should().NotContain("updated.Trigger =");
        source.Should().NotContain("updated.DurationMs =");
        source.Should().NotContain("updated.DelayMs =");
        source.Should().NotContain("private static string FormatEffect");
        source.Should().NotContain("private static string FormatDuration");
        source.Should().NotContain("private static bool TryParseDuration");
        source.Should().NotContain("private string ResolveShapeName");
        source.Should().NotContain("private static ShapeAnimation CloneAnimation");
        source.Should().NotContain("double.TryParse");
        source.Should().NotContain("_editor.MoveAnimation(");
    }

    [StaFact]
    public void AnimationPane_RemovesThroughSharedUndoableMutationPlan()
    {
        var editor = MakeSessionWithAnimations();
        var pane = new AnimationPane(new AnimationPaneSession(() => editor));

        var plan = pane.RemoveAnimationForTest(0);

        plan.Should().Be(new AnimationPaneRemoveMutationPlan(
            true,
            0,
            0,
            "Remove animation 1",
            null));
        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(20u);
        pane.CurrentTimelinePlanForTest.Items.Should().ContainSingle();

        editor.Undo();
        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(10u, 20u);
    }

    // ── Test-seam helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Counts Border children of the inner StackPanel that have a Tag that is an int
    /// (those are animation rows; the empty-state TextBlock is not tagged).
    /// </summary>
    private static int CountAnimationRows(AnimationPane pane)
    {
        var root  = pane.Child as Grid;
        var scroll = root?.Children.OfType<ScrollViewer>().FirstOrDefault();
        var stack = scroll?.Content as StackPanel;
        if (stack is null) return 0;
        return stack.Children.OfType<Border>().Count(b => b.Tag is int);
    }

    /// <summary>
    /// Returns the shape-name TextBlock texts from each animation row.
    /// The name TextBlock is at column 1 of the inner Grid.
    /// </summary>
    private static List<string> CollectRowShapeNames(AnimationPane pane)
    {
        var root   = pane.Child as Grid;
        var scroll = root?.Children.OfType<ScrollViewer>().FirstOrDefault();
        var stack  = scroll?.Content as StackPanel;
        if (stack is null) return new List<string>();

        var names = new List<string>();
        foreach (var child in stack.Children.OfType<Border>().Where(b => b.Tag is int))
        {
            var innerGrid = child.Child as Grid;
            if (innerGrid is null) continue;
            // Column 1 of the inner Grid is the shape-name TextBlock.
            var nameBlock = innerGrid.Children.OfType<TextBlock>()
                .FirstOrDefault(tb => Grid.GetColumn(tb) == 1);
            if (nameBlock is not null)
                names.Add(nameBlock.Text ?? string.Empty);
        }
        return names;
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
