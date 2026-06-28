using System.Globalization;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class AnimationPanePlannerTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(AnimationKind.Entrance, AnimationPreset.Appear, "In: Appear")]
    [InlineData(AnimationKind.Exit, AnimationPreset.Fade, "Out: Fade")]
    [InlineData(AnimationKind.Emphasis, AnimationPreset.Pulse, "Em: Pulse")]
    [InlineData(AnimationKind.Motion, AnimationPreset.Fade, "Mv: Motion")]
    public void FormatEffect_ReturnsPaneLabel(
        AnimationKind kind,
        AnimationPreset preset,
        string expected)
    {
        var label = AnimationPanePlanner.FormatEffect(new ShapeAnimation
        {
            Kind = kind,
            Preset = preset
        });

        label.Should().Be(expected);
    }

    [Fact]
    public void TriggerLabels_MatchTriggerIndexes()
    {
        AnimationPanePlanner.TriggerLabels.Should().Equal(
            "On Click",
            "With Previous",
            "After Previous");

        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.OnClick).Should().Be(0);
        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.WithPrevious).Should().Be(1);
        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.AfterPrevious).Should().Be(2);
    }

    [Theory]
    [InlineData(0, AnimationTrigger.OnClick)]
    [InlineData(1, AnimationTrigger.WithPrevious)]
    [InlineData(2, AnimationTrigger.AfterPrevious)]
    public void TryGetTrigger_MapsValidIndexes(int index, AnimationTrigger expected)
    {
        AnimationPanePlanner.TryGetTrigger(index, out var trigger).Should().BeTrue();
        trigger.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void TryGetTrigger_RejectsInvalidIndexes(int index)
    {
        AnimationPanePlanner.TryGetTrigger(index, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(500, "0.5")]
    [InlineData(1000, "1")]
    [InlineData(1250, "1.25")]
    public void FormatDuration_FormatsSeconds(int durationMs, string expected)
    {
        AnimationPanePlanner.FormatDuration(durationMs, Invariant).Should().Be(expected);
    }

    [Theory]
    [InlineData("0.75", 750)]
    [InlineData("1.2345", 1234)]
    public void TryParseDuration_AcceptsPositiveInvariantSeconds(
        string text,
        int expectedMs)
    {
        AnimationPanePlanner.TryParseDuration(text, out int ms).Should().BeTrue();
        ms.Should().Be(expectedMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public void TryParseDuration_RejectsInvalidOrNonPositiveSeconds(string text)
    {
        AnimationPanePlanner.TryParseDuration(text, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildDurationEditPlan_ChangedValidText_RequestsUpdate()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("1.25", 500, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(true, 1250, "1.25"));
    }

    [Fact]
    public void BuildDurationEditPlan_SameValue_NormalizesDisplayWithoutUpdate()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("1.0", 1000, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(false, 1000, "1"));
    }

    [Fact]
    public void BuildDurationEditPlan_InvalidText_RevertsToCurrentDisplay()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("oops", 500, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(false, 500, "0.5"));
    }
}
