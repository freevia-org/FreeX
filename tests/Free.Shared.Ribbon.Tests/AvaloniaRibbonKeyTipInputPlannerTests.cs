using Avalonia.Input;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonKeyTipInputPlannerTests
{
    [Fact]
    public void AllAvaloniaProductHostsDelegateCommonInputDecisionsToThePlanner()
    {
        var root = FindRepositoryRoot();
        var hostSources = new[]
        {
            Read(root, "src", "FreeX.App.Avalonia", "MainWindow.DesktopChrome.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in hostSources)
        {
            source.Should().Contain("AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(")
                .And.NotContain("input.Action == AvaloniaRibbonKeyTipInputAction")
                .And.NotContain("args.Key is Key.LeftAlt or Key.RightAlt")
                .And.NotContain("args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None");
        }
    }

    [Theory]
    [InlineData(Key.LeftAlt, false, true)]
    [InlineData(Key.RightAlt, true, false)]
    public void ModeTransitionTogglesVisibilityAndConsumesAlt(
        Key key,
        bool currentVisibility,
        bool expectedVisibility)
    {
        AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
                key,
                KeyModifiers.None,
                currentVisibility)
            .Should().Be(new AvaloniaRibbonKeyTipModeTransitionPlan(
                ShouldRouteToken: false,
                Handled: true,
                ModeVisible: expectedVisibility));
    }

    [Fact]
    public void ModeTransitionDismissesVisibleModeAndConsumesEscape()
    {
        AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
                Key.Escape,
                KeyModifiers.None,
                modeVisible: true)
            .Should().Be(new AvaloniaRibbonKeyTipModeTransitionPlan(
                ShouldRouteToken: false,
                Handled: true,
                ModeVisible: false));
    }

    [Fact]
    public void ModeTransitionLeavesIgnoredInputForTheProductHost()
    {
        AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
                Key.A,
                KeyModifiers.None,
                modeVisible: false)
            .Should().Be(new AvaloniaRibbonKeyTipModeTransitionPlan(
                ShouldRouteToken: false,
                Handled: false));
    }

    [Fact]
    public void ModeTransitionRoutesNormalizedTokensWithoutApplyingProductPolicy()
    {
        AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
                Key.D,
                KeyModifiers.Alt,
                modeVisible: false,
                acceptDirectAltToken: true)
            .Should().Be(new AvaloniaRibbonKeyTipModeTransitionPlan(
                ShouldRouteToken: true,
                Handled: false,
                Token: "D"));
    }

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

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
