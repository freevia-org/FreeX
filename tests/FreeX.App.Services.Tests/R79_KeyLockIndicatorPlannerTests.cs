using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R79-render-namebar-statusbar-5-3: the status bar never showed Excel's CAPS LOCK / NUM LOCK
/// warning indicators -- there was no keyboard-state -> indicator mapping anywhere in the
/// codebase. These tests cover the new <see cref="KeyLockIndicatorPlanner"/> pure mapping.
/// </summary>
public sealed class R79_KeyLockIndicatorPlannerTests
{
    [Fact]
    public void Build_CapsLockOnly_ShowsOnlyCapsLockIndicator()
    {
        // Failing before the fix: KeyLockIndicatorPlanner did not exist, so there was no way to
        // turn a toggled-on Caps Lock key into a status bar indicator at all.
        var plan = KeyLockIndicatorPlanner.Build(capsLockOn: true, numLockOn: false);

        plan.CapsLockVisible.Should().BeTrue();
        plan.NumLockVisible.Should().BeFalse();
    }

    [Fact]
    public void Build_BothLocksOn_ShowsBothIndicators()
    {
        var plan = KeyLockIndicatorPlanner.Build(capsLockOn: true, numLockOn: true);

        plan.CapsLockVisible.Should().BeTrue();
        plan.NumLockVisible.Should().BeTrue();
    }

    [Fact]
    public void Build_NeitherLockOn_HidesBothIndicators()
    {
        // No-regression sibling: the default, unmodified-keyboard state must hide both
        // indicators, matching real Excel's status bar showing nothing when no lock key is on.
        var plan = KeyLockIndicatorPlanner.Build(capsLockOn: false, numLockOn: false);

        plan.CapsLockVisible.Should().BeFalse();
        plan.NumLockVisible.Should().BeFalse();
    }

    [Fact]
    public void Build_NumLockOnly_ShowsOnlyNumLockIndicator()
    {
        var plan = KeyLockIndicatorPlanner.Build(capsLockOn: false, numLockOn: true);

        plan.CapsLockVisible.Should().BeFalse();
        plan.NumLockVisible.Should().BeTrue();
    }
}
