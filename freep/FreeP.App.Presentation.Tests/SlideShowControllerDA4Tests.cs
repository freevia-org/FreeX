using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// DA4: AfterPrevious animation chaining.
///
/// PowerPoint semantics:
///   OnClick A   → A starts on the user click (new click-step).
///   WithPrevious B → B starts simultaneously with A (same step, StartDelayMs = B.DelayMs).
///   AfterPrevious C → C starts when the preceding animation in the chain completes:
///                     StartDelayMs = accumulated(prior.StartDelayMs + prior.DurationMs) + C.DelayMs.
///
/// Before this fix BuildSteps merged all three into one step and set StartDelayMs = DelayMs
/// for every entry, making them all fire simultaneously (at time 0 + their own DelayMs).
///
/// After this fix:
///   • OnClick A (500 ms)       → StartDelayMs = 0
///   • WithPrevious B (300 ms)  → StartDelayMs = 0   (simultaneous with A)
///   • AfterPrevious C (200 ms) → StartDelayMs = 500  (waits for A to finish — the OnClick spine)
///   • AfterPrevious D (100 ms) → StartDelayMs = 500+200 = 700 (waits for C to finish)
/// </summary>
public sealed class SlideShowControllerDA4Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a slide whose main-sequence animations are specified by the caller.
    /// Each item describes (trigger, durationMs, delayMs).  ShapeIds are auto-assigned.
    /// </summary>
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
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
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

    // ── DA4 core: AfterPrevious start offsets ──────────────────────────────────────

    /// <summary>
    /// Three-animation chain: OnClick A (500 ms), AfterPrevious B (300 ms), AfterPrevious C —
    /// after ONE click, A starts at 0, B starts at 500 (after A completes), C starts at 800
    /// (after A+B complete).  NOT all at 0.
    ///
    /// This is the canonical DA4 test case.
    /// </summary>
    [Fact]
    public void DA4_OnClick_AfterPrevious_Chain_StartDelays_Accumulated()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 500, delayMs: 0),  // A
            (AnimationTrigger.AfterPrevious, durationMs: 300, delayMs: 0),  // B → after A
            (AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0)); // C → after A+B

        var steps = SlideShowController.BuildSteps(slide);

        // All three belong to ONE click-step (no additional OnClick broke the chain).
        steps.Should().HaveCount(1, "OnClick + two AfterPrevious form a single click-step");

        var entries = steps[0].Entries;
        entries.Should().HaveCount(3);

        // A: OnClick — starts immediately.
        entries[0].StartDelayMs.Should().Be(0,    "OnClick animation A starts at t=0");
        // B: AfterPrevious — starts after A (duration=500).
        entries[1].StartDelayMs.Should().Be(500,  "AfterPrevious B starts after A completes (500 ms)");
        // C: AfterPrevious — starts after A+B (500+300=800).
        entries[2].StartDelayMs.Should().Be(800,  "AfterPrevious C starts after A+B complete (800 ms)");
    }

    /// <summary>
    /// WithPrevious fires simultaneously — StartDelayMs equals only the animation's own DelayMs.
    /// </summary>
    [Fact]
    public void DA4_WithPrevious_StartsSimultaneously_NotAfterPrior()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,      durationMs: 500, delayMs: 0),  // A
            (AnimationTrigger.WithPrevious, durationMs: 300, delayMs: 0)); // B — simultaneous

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        var entries = steps[0].Entries;
        entries.Should().HaveCount(2);

        entries[0].StartDelayMs.Should().Be(0, "OnClick animation A starts at t=0");
        entries[1].StartDelayMs.Should().Be(0, "WithPrevious B starts simultaneously with A (t=0)");
    }

    /// <summary>
    /// Mixed chain: OnClick A, WithPrevious B, AfterPrevious C.
    /// B is simultaneous with A.  C must wait for the OnClick spine (A at 500 ms),
    /// NOT for A+B (which only runs concurrently, not extending the spine).
    /// </summary>
    [Fact]
    public void DA4_WithPrevious_DoesNotExtendAfterPreviousSpine()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 500, delayMs: 0),  // A (OnClick spine)
            (AnimationTrigger.WithPrevious,  durationMs: 999, delayMs: 0),  // B (concurrent, NOT spine)
            (AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0)); // C → after A's spine

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        var entries = steps[0].Entries;
        entries.Should().HaveCount(3);

        // B is WithPrevious — simultaneous with A, does NOT change the spine's accumulated end.
        entries[1].StartDelayMs.Should().Be(0,   "WithPrevious B is simultaneous (StartDelayMs=0)");
        // C is AfterPrevious — must use the spine (A @ 500 ms), not B's 999 ms duration.
        entries[2].StartDelayMs.Should().Be(500, "AfterPrevious C waits for spine A (500 ms), not WithPrevious B");
    }

    /// <summary>
    /// DelayMs on an AfterPrevious animation is added on top of the accumulated prior duration.
    /// </summary>
    [Fact]
    public void DA4_AfterPrevious_WithOwnDelayMs_AddsToAccumulated()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 400, delayMs:   0),  // A
            (AnimationTrigger.AfterPrevious, durationMs: 300, delayMs: 100)); // B: wait 100 after A ends

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        var entries = steps[0].Entries;
        entries.Should().HaveCount(2);

        // B starts at accumulated 400 (A's end) + 100 (B's own delay) = 500.
        entries[1].StartDelayMs.Should().Be(500,
            "AfterPrevious B's start = A.DurationMs (400) + B.DelayMs (100) = 500");
    }

    /// <summary>
    /// OnClick's own DelayMs is included in the accumulated spine end time.
    /// </summary>
    [Fact]
    public void DA4_OnClick_WithDelayMs_IsIncludedInSpineAccumulation()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 400, delayMs: 100),  // A: delay 100, ends at 500
            (AnimationTrigger.AfterPrevious, durationMs: 200, delayMs:   0)); // B: should start at 500

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        var entries = steps[0].Entries;

        // A starts at delayMs=100, ends at 100+400=500.
        entries[0].StartDelayMs.Should().Be(100, "OnClick A starts at its own DelayMs=100");
        // B starts at A's end = 500.
        entries[1].StartDelayMs.Should().Be(500, "AfterPrevious B starts when A ends (100+400=500)");
    }

    /// <summary>
    /// Multiple separate OnClick steps: each OnClick resets the accumulated chain.
    /// AfterPrevious after the SECOND OnClick waits only for the second OnClick duration,
    /// not the first.
    /// </summary>
    [Fact]
    public void DA4_SecondOnClick_ResetsAccumulatedChain()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 999, delayMs: 0),  // A — step 1
            (AnimationTrigger.OnClick,       durationMs: 300, delayMs: 0),  // B — step 2
            (AnimationTrigger.AfterPrevious, durationMs: 100, delayMs: 0)); // C — step 2, waits for B only

        var steps = SlideShowController.BuildSteps(slide);

        // Two click-steps: {A} and {B, C}.
        steps.Should().HaveCount(2);

        var step2 = steps[1].Entries;
        step2.Should().HaveCount(2);

        step2[0].StartDelayMs.Should().Be(0,   "B is the new OnClick (start at 0 within step 2)");
        step2[1].StartDelayMs.Should().Be(300, "C waits for B only (300 ms), not A+B");
    }

    /// <summary>
    /// Three-step AfterPrevious chain: A→B→C each waits for the prior to complete.
    /// Validates the full accumulated walk.
    /// </summary>
    [Fact]
    public void DA4_ThreeStepAfterPreviousChain_FullAccumulation()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 500, delayMs: 0),  // A ends at 500
            (AnimationTrigger.AfterPrevious, durationMs: 300, delayMs: 0),  // B starts 500, ends 800
            (AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0),  // C starts 800, ends 1000
            (AnimationTrigger.AfterPrevious, durationMs: 100, delayMs: 0)); // D starts 1000

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1, "all four animations are in one click-step");
        var entries = steps[0].Entries;
        entries.Should().HaveCount(4);

        entries[0].StartDelayMs.Should().Be(0,    "A starts at t=0");
        entries[1].StartDelayMs.Should().Be(500,  "B starts after A (500)");
        entries[2].StartDelayMs.Should().Be(800,  "C starts after B (500+300=800)");
        entries[3].StartDelayMs.Should().Be(1000, "D starts after C (800+200=1000)");
    }

    /// <summary>
    /// Regression: step grouping still places AfterPrevious in the SAME click-step as its
    /// triggering OnClick, not in a NEW step. AfterPrevious is auto-play, not click-triggered.
    /// </summary>
    [Fact]
    public void DA4_AfterPrevious_BelongsToSameClickStep_NotNewStep()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 500, delayMs: 0),  // A
            (AnimationTrigger.AfterPrevious, durationMs: 300, delayMs: 0)); // B — auto after A

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1,
            "AfterPrevious B is in the SAME click-step as A — it auto-plays, does not require a new click");
        steps[0].Entries.Should().HaveCount(2);
    }

    /// <summary>
    /// Back-compat: the Animations property on AnimationStep still returns the raw ShapeAnimation
    /// list in declaration order, for consumers that don't need start delays.
    /// </summary>
    [Fact]
    public void DA4_AnimationsBackCompatProperty_ReturnsCorrectCount()
    {
        var slide = MakeSlide(
            (AnimationTrigger.OnClick,       durationMs: 500, delayMs: 0),
            (AnimationTrigger.AfterPrevious, durationMs: 300, delayMs: 0),
            (AnimationTrigger.AfterPrevious, durationMs: 200, delayMs: 0));

        var steps = SlideShowController.BuildSteps(slide);

        steps.Should().HaveCount(1);
        // Back-compat Animations property must contain all 3 animations.
        steps[0].Animations.Should().HaveCount(3,
            "Animations back-compat property must return all 3 animations in the step");
    }

    // ── BuildTriggerSteps: same rules apply ───────────────────────────────────────

    [Fact]
    public void DA4_BuildTriggerSteps_AfterPrevious_AccumulatesDelay()
    {
        var anims = new List<ShapeAnimation>
        {
            new() { ShapeId = 1, Trigger = AnimationTrigger.OnClick,       DurationMs = 400, DelayMs = 0 },
            new() { ShapeId = 2, Trigger = AnimationTrigger.AfterPrevious, DurationMs = 200, DelayMs = 0 },
        };

        var steps = SlideShowController.BuildTriggerSteps(anims);

        steps.Should().HaveCount(1);
        var entries = steps[0].Entries;
        entries[0].StartDelayMs.Should().Be(0,   "trigger OnClick starts at 0");
        entries[1].StartDelayMs.Should().Be(400, "trigger AfterPrevious starts after OnClick completes");
    }

    [Fact]
    public void DA4_BuildTriggerSteps_WithPrevious_SimultaneousWithPrior()
    {
        var anims = new List<ShapeAnimation>
        {
            new() { ShapeId = 1, Trigger = AnimationTrigger.OnClick,      DurationMs = 400, DelayMs = 0 },
            new() { ShapeId = 2, Trigger = AnimationTrigger.WithPrevious, DurationMs = 200, DelayMs = 0 },
        };

        var steps = SlideShowController.BuildTriggerSteps(anims);

        steps.Should().HaveCount(1);
        steps[0].Entries[1].StartDelayMs.Should().Be(0,
            "trigger WithPrevious is simultaneous (StartDelayMs=0)");
    }

    // ── AnimationEntry record ─────────────────────────────────────────────────────

    [Fact]
    public void DA4_AnimationEntry_ExposesAnimationAndStartDelay()
    {
        var anim = new ShapeAnimation
        {
            ShapeId    = 7,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.Fade,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 500,
            DelayMs    = 0,
        };
        var entry = new AnimationEntry(anim, StartDelayMs: 123);

        entry.Animation.Should().BeSameAs(anim);
        entry.StartDelayMs.Should().Be(123);
    }
}
