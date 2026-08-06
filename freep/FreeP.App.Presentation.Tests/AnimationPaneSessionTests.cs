using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class AnimationPaneSessionTests
{
    [Fact]
    public void Session_OwnsSelectionPlaybackAndMutations()
    {
        var editor = CreateEditor();
        var session = new AnimationPaneSession(() => editor);

        session.Refresh().Items.Should().HaveCount(2);
        session.SelectAnimation(1).SelectedIndex.Should().Be(1);
        editor.SelectedShapeIds.Should().ContainSingle().Which.Should().Be(2);

        var transition = session.ExecutePlayback(AnimationPanePlaybackControlKind.PlayFromSelected);

        transition.ShouldStartPreview.Should().BeTrue();
        transition.Playback.StartAnimationIndex.Should().Be(1);
        session.PlaybackWorkflowEvidence!.HasSharedNoComHostEvidence.Should().BeTrue();

        session.ExecutePlayback(AnimationPanePlaybackControlKind.Stop).ShouldStartPreview.Should().BeFalse();
        session.Playback!.State.Should().Be(AnimationPanePlaybackSessionState.Stopped);

        session.MoveAnimation(1, -1).ShouldApply.Should().BeTrue();
        session.SelectedAnimationIndex.Should().Be(0);
        editor.CurrentSlideAnimations[0].ShapeId.Should().Be(2);

        session.ApplyDuration(0, "1.25").ShouldApply.Should().BeTrue();
        editor.CurrentSlideAnimations[0].DurationMs.Should().Be(1250);
    }

    [Fact]
    public void Reset_ClearsRendererIndependentState()
    {
        var editor = CreateEditor();
        var session = new AnimationPaneSession(() => editor);
        session.SelectAnimation(0);
        session.ExecutePlayback(AnimationPanePlaybackControlKind.PreviewCurrentSlide);

        session.Reset();

        session.SelectedAnimationIndex.Should().Be(-1);
        session.Timeline.Should().BeNull();
        session.WorkflowEvidence.Should().BeNull();
        session.Playback.Should().BeNull();
        session.PlaybackWorkflowEvidence.Should().BeNull();
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 1, Name = "First" });
        slide.Shapes.Add(new SlideShape { Id = 2, Name = "Second" });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.AfterPrevious,
            DurationMs = 750,
        });
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
