using Avalonia.Input;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonKeyTipInputPlannerTests
{
    [Theory]
    [InlineData(Key.LeftAlt, KeyModifiers.None)]
    [InlineData(Key.RightAlt, KeyModifiers.Control)]
    [InlineData(Key.F10, KeyModifiers.None)]
    public void AltAndPlainF10ToggleMode(Key key, KeyModifiers modifiers)
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(key, modifiers, modeVisible: false)
            .Should().Be(new AvaloniaRibbonKeyTipInputPlan(
                AvaloniaRibbonKeyTipInputAction.ToggleMode));
    }

    [Theory]
    [InlineData(Key.F10, KeyModifiers.Shift)]
    [InlineData(Key.F10, KeyModifiers.Control)]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.Escape, KeyModifiers.None)]
    public void InputOutsideModeRemainsAvailableToTheProductHost(
        Key key,
        KeyModifiers modifiers)
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(key, modifiers, modeVisible: false)
            .Action.Should().Be(AvaloniaRibbonKeyTipInputAction.Ignore);
    }

    [Fact]
    public void EscapeDismissesAnOpenMode()
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(
                Key.Escape,
                KeyModifiers.None,
                modeVisible: true)
            .Should().Be(new AvaloniaRibbonKeyTipInputPlan(
                AvaloniaRibbonKeyTipInputAction.DismissMode));
    }

    [Theory]
    [InlineData(Key.A, "A")]
    [InlineData(Key.D7, "7")]
    public void OpenModeProducesNormalizedTokens(Key key, string token)
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(
                key,
                KeyModifiers.None,
                modeVisible: true)
            .Should().Be(new AvaloniaRibbonKeyTipInputPlan(
                AvaloniaRibbonKeyTipInputAction.ProcessToken,
                token));
    }

    [Fact]
    public void UnsupportedInputInsideModeRemainsAvailableToNestedOrProductRouting()
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(
                Key.OemPlus,
                KeyModifiers.None,
                modeVisible: true)
            .Action.Should().Be(AvaloniaRibbonKeyTipInputAction.Ignore);
    }

    [Fact]
    public void DirectAltTokensAreOptInForHostsWithLegacySequences()
    {
        AvaloniaRibbonKeyTipInputPlanner.Resolve(
                Key.D,
                KeyModifiers.Alt,
                modeVisible: false,
                acceptDirectAltToken: true)
            .Should().Be(new AvaloniaRibbonKeyTipInputPlan(
                AvaloniaRibbonKeyTipInputAction.ProcessToken,
                "D"));

        AvaloniaRibbonKeyTipInputPlanner.Resolve(
                Key.D,
                KeyModifiers.Alt,
                modeVisible: false)
            .Action.Should().Be(AvaloniaRibbonKeyTipInputAction.Ignore);
    }
}
