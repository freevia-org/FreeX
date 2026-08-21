namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round-161 finding: an auto-play (WithPrevious/AfterPrevious) main-sequence animation head is
/// round-tripped correctly by the model and writer (round-160), but the live SlideShowController
/// player still gated it behind an extra Advance(). <see cref="SlideShowController.BuildSteps"/>
/// always groups a slide's very first main-sequence animation into its own click-step regardless
/// of its stored trigger, so a WithPrevious/AfterPrevious head sat in
/// <see cref="SlideShowController.CurrentSteps"/>[0] waiting for the same click an OnClick head
/// waits for -- one Advance() more than real PowerPoint needs. These tests drive the controller
/// end to end (construct/navigate, then read state) to prove
/// <see cref="SlideShowController.ConsumeEntryAutoPlayStep"/> delivers such a head as soon as the
/// slide is entered, with no extra Advance() call, while leaving an ordinary click-gated head
/// (and the existing Advance()/StepCount contract) completely unchanged.
/// </summary>
public sealed class SlideShowControllerEntryAutoPlayTests
{
    private static Slide MakeSlide(
        params (AnimationTrigger trigger, int durationMs, int delayMs)[] animSpecs)
    {
        var slide = new Slide();
        uint id = 1;
        foreach (var (trigger, durationMs, delayMs) in animSpecs)
        {
            slide.Shapes.Add(new SlideShape
            {
                Id            = id,
                Name          = $"Shape{id}",
                Kind          = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                ExtentCxEmu   = 914400,
                ExtentCyEmu   = 914400,
            });
            slide.Animations.Add(new ShapeAnimation
            {
                ShapeId    = id++,
                Kind       = AnimationKind.Entrance,
                Preset     = AnimationPreset.Appear,
                Trigger    = trigger,
                DurationMs = durationMs,
                DelayMs    = delayMs,
            });
        }
        return slide;
    }

    /// <summary>
    /// Core repro from the finding: two slides, the second's main sequence starts with a
    /// WithPrevious head followed by a real OnClick step. Before the fix, reaching that slide via
    /// GoToSlide/Advance left the head sitting as CurrentSteps[0], requiring one extra Advance()
    /// to reveal it (proved by the finding's probe: first Advance() → NavigateToSlide with nothing
    /// played, second Advance() → PlayStep for the head). After the fix, the head is available the
    /// instant the slide is entered, and Advance() proceeds straight to the real OnClick step.
    /// </summary>
    [Fact]
    public void ConsumeEntryAutoPlayStep_PlaysWithPreviousHead_WithoutExtraAdvance()
    {
        var slide0 = new Slide();
        var slide1 = MakeSlide(
            (AnimationTrigger.WithPrevious, durationMs: 400, delayMs: 0),   // auto-play head
            (AnimationTrigger.OnClick,      durationMs: 300, delayMs: 0));  // real click step

        var controller = new SlideShowController([slide0, slide1], startIndex: 0);

        // Land on slide1 the same way Advance()'s NavigateToSlide branch does internally.
        controller.GoToSlide(1);

        // The head is retrievable immediately -- no Advance() call was made to obtain it.
        var autoStep = controller.ConsumeEntryAutoPlayStep();
        autoStep.Should().NotBeNull("a WithPrevious head should auto-play as soon as the slide is entered");
        autoStep!.Entries.Should().ContainSingle(e => e.Animation.Trigger == AnimationTrigger.WithPrevious);

        // BuildSteps still groups the head into its own click-step (that grouping itself is
        // correct and unchanged -- see BuildSteps's own doc comment); what changed is that the
        // caller can now consume it without waiting for a click.
        controller.StepCount.Should().Be(2);
        controller.HasPendingSteps.Should().BeTrue("the real OnClick step still requires a click");

        var result = controller.Advance();
        result.Should().BeOfType<AdvanceResult.PlayStep>();
        var playStep = (AdvanceResult.PlayStep)result;
        playStep.Step.Entries.Should().ContainSingle(e => e.Animation.Trigger == AnimationTrigger.OnClick);
        playStep.Step.Should().NotBeSameAs(autoStep, "the auto-play head must not be re-delivered by Advance()");
    }

    /// <summary>AfterPrevious behaves identically to WithPrevious for entry auto-play.</summary>
    [Fact]
    public void ConsumeEntryAutoPlayStep_PlaysAfterPreviousHead_WithoutExtraAdvance()
    {
        var slide = MakeSlide((AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0));
        var controller = new SlideShowController([slide], startIndex: 0);

        var autoStep = controller.ConsumeEntryAutoPlayStep();
        autoStep.Should().NotBeNull();
        autoStep!.Entries[0].Animation.Trigger.Should().Be(AnimationTrigger.AfterPrevious);
    }

    /// <summary>
    /// Once consumed, the same head must not be handed out again (by a stray double-call, or by
    /// a later Advance()) -- it already "happened" on entry.
    /// </summary>
    [Fact]
    public void ConsumeEntryAutoPlayStep_ReturnsNullOnceAlreadyConsumed()
    {
        var slide = MakeSlide((AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0));
        var controller = new SlideShowController([slide], startIndex: 0);

        controller.ConsumeEntryAutoPlayStep().Should().NotBeNull();
        controller.ConsumeEntryAutoPlayStep().Should().BeNull("the head was already consumed");
    }

    // ── Sibling / no-regression: an ordinary OnClick head is completely unaffected ─────────────

    /// <summary>
    /// The far more common case -- a slide whose first main-sequence animation is legitimately
    /// OnClick -- must keep requiring exactly one Advance(), exactly as before. Nothing about this
    /// fix may auto-fire a real click-gated head.
    /// </summary>
    [Fact]
    public void ConsumeEntryAutoPlayStep_ReturnsNull_ForOrdinaryOnClickHead()
    {
        var slide0 = new Slide();
        var slide1 = MakeSlide((AnimationTrigger.OnClick, durationMs: 300, delayMs: 0));

        var controller = new SlideShowController([slide0, slide1], startIndex: 0);
        controller.GoToSlide(1);

        controller.ConsumeEntryAutoPlayStep().Should().BeNull(
            "an OnClick head legitimately requires a click and must not be auto-fired");

        // Advance() still delivers it exactly as before -- unchanged behaviour for this case.
        var result = controller.Advance();
        result.Should().BeOfType<AdvanceResult.PlayStep>();
        var playStep = (AdvanceResult.PlayStep)result;
        playStep.Step.Entries[0].Animation.Trigger.Should().Be(AnimationTrigger.OnClick);
    }

    /// <summary>
    /// A caller that never calls ConsumeEntryAutoPlayStep() at all (i.e. every existing call site
    /// today) must see byte-for-byte the same Advance()/StepCount behaviour as before this fix --
    /// the new method is purely additive and opt-in.
    /// </summary>
    [Fact]
    public void WithPreviousHead_StillDeliveredByPlainAdvance_WhenAutoPlayNeverConsumed()
    {
        var slide0 = new Slide();
        var slide1 = MakeSlide(
            (AnimationTrigger.WithPrevious, durationMs: 400, delayMs: 0),
            (AnimationTrigger.OnClick,      durationMs: 300, delayMs: 0));

        var controller = new SlideShowController([slide0, slide1], startIndex: 0);
        controller.GoToSlide(1);

        // No call to ConsumeEntryAutoPlayStep() here -- exercise the old path unchanged.
        controller.StepCount.Should().Be(2);
        var first = controller.Advance();
        first.Should().BeOfType<AdvanceResult.PlayStep>();
        ((AdvanceResult.PlayStep)first).Step.Entries[0].Animation.Trigger
            .Should().Be(AnimationTrigger.WithPrevious);
    }
}
