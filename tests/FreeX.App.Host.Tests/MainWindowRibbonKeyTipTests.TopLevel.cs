using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void TopLevelKeyTipOverlay_ExposesEveryVisibleMainRibbonTab()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetRibbonWidth(1520);

            harness.EnterKeyTipScope("TopLevel");

            var tabKeyTips = new[]
            {
                "F", "H", "N", "J", "P", "M", "A", "R", "W", "Y"
            };
            harness.OverlayBadgeTexts.Should().Contain(tabKeyTips);
            harness.OverlayBadgeColors("N").Should().Be((Colors.DimGray, Colors.White),
                "top-level tab keytips need to remain distinguishable from Home command labels");
            harness.OverlayBadgeColors("W").Should().Be((Colors.DimGray, Colors.White));

            var tabBadgeBounds = tabKeyTips
                .Select(harness.OverlayBadgeBounds)
                .ToArray();
            for (var index = 0; index < tabBadgeBounds.Length; index++)
            {
                for (var otherIndex = index + 1; otherIndex < tabBadgeBounds.Length; otherIndex++)
                {
                    tabBadgeBounds[index]
                        .IntersectsWith(tabBadgeBounds[otherIndex])
                        .Should()
                        .BeFalse("top-level Alt keytips must remain individually readable");
                }
            }
        });
    }

    [Fact]
    public void TopLevelAndCommandKeyTips_RouteThroughVisibleRibbonControls()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["H", "N", "1"]);
            harness.OverlayBadgeTexts.Should().NotContain("B", "top-level Alt mode should show tabs and QAT, not active-tab command badges");
            harness.HandleKeyTip(Key.N);
            harness.SelectedRibbonTabHeader.Should().Be("Insert");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Commands");
            harness.OverlayBadgeTexts.Should().Contain(["B", "1"]);
            harness.OverlayBadgeTexts.Should().NotContain("SC", "command-scope Alt mode should not show off-tab Insert chart badges");
            harness.VisibleCommandKeyTips("B").Should().ContainSingle("Borders");
            harness.HandleKeyTip(Key.B);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("All Borders").Should().Be("A");
            harness.HandleKeyTip(Key.Escape);

            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
            harness.OverlayBadgeTexts.Should().BeEmpty("Escape should clear any visible keytip badges");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.B);

            harness.HandleKeyTip(Key.A);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a menu keytip should leave keytip mode fully closed");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);

            harness.IsToggleChecked("BoldButton").Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a command keytip should leave keytip mode fully closed");
        });
    }

    [Fact]
    public void KeyTipOverlay_NormalizesAttachedKeyTipMetadata()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            var originalKeyTip = harness.SetButtonKeyTip("SaveQatBtn", " q ");

            try
            {
                harness.EnterKeyTipScope("TopLevel");

                harness.OverlayBadgeTexts.Should().Contain("Q");
                harness.OverlayBadgeTexts.Should().NotContain(" q ");
                harness.OverlayBadgeTexts.Should().NotContain("q");
            }
            finally
            {
                harness.SetButtonKeyTip("SaveQatBtn", originalKeyTip ?? "");
            }
        });
    }

    [Fact]
    public void CommandKeyTipCandidates_AreReusedDuringScopeAndRefreshedOnReentry()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.VisibleCommandKeyTips("ZZ").Should().BeEmpty();

            using var dynamicCommand = harness.AddHomeRibbonCommandButton("ZZ", "Late Test Command");

            harness.VisibleCommandKeyTips("ZZ")
                .Should()
                .BeEmpty("an active command keytip pass should reuse the candidates captured when its overlay was shown");

            harness.HandleKeyTip(Key.Z);
            harness.KeyTipScope.Should().Be("None", "the late command should not extend the active cached command scope");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            harness.VisibleCommandKeyTips("ZZ")
                .Should()
                .ContainSingle("Late Test Command", "a fresh keytip pass should refresh visible command candidates");
        });
    }

    [Fact]
    public void DirectAltTopLevelKeyTips_OpenTabsAndBackstage()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.HandleDirectTopLevelKeyTip(Key.N).Should().BeTrue();

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.KeyTipScope.Should().Be("Commands");

            harness.HandleDirectTopLevelKeyTip(Key.F).Should().BeTrue();

            harness.StartScreenIsVisible.Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("N").Should().ContainSingle().Which.Should().Be("New");
        });
    }
}
