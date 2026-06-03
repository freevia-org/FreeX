using System.Reflection;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
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
    public void FileKeyTip_RoutesThroughBackstageCommandsOnly()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.F);

            harness.StartScreenIsVisible.Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.OverlayBadgeTexts.Should().Contain(["N", "O", "SH"]);
            harness.OverlayBadgeTexts.Should().NotContain("FG", "covered Home ribbon controls should not participate while Backstage is open");
            harness.VisibleCommandKeyTips("N").Should().ContainSingle().Which.Should().Be("New");
        });
    }

    [Fact]
    public void FileKeyTip_DoesNotExposeDuplicateRecentFileRowKeyTips()
    {
        RunSta(() =>
        {
            using var tempFiles = TempRecentFiles.Create(4);
            using var harness = MainWindowHarness.Create();
            harness.SetRecentFiles(tempFiles.Paths.Take(2), tempFiles.Paths.Skip(2));

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.F);

            harness.OverlayBadgeTexts
                .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Should()
                .BeEmpty("Backstage keytips must be unique within the visible File scope");
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

    [Fact]
    public void DirectAltQatKeyTips_InvokeUndoRedoQuickAccessToolbarCommands()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.UndoQatIsEnabled.Should().BeFalse();
            harness.RedoQatIsEnabled.Should().BeFalse();
            harness.SelectActiveCell();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);

            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();

            harness.HandleDirectTopLevelKeyTip(Key.D2).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeFalse();
            harness.UndoQatIsEnabled.Should().BeFalse();
            harness.RedoQatIsEnabled.Should().BeTrue();

            harness.HandleDirectTopLevelKeyTip(Key.D3).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void DirectAltQatKeyTips_NormalizeAttachedKeyTipMetadata()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell();
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);
            harness.ActiveCellBold.Should().BeTrue();

            var originalKeyTip = harness.SetButtonKeyTip("UndoQatBtn", " 2 ");

            try
            {
                harness.HandleDirectTopLevelKeyTip(Key.D2).Should().BeTrue();

                harness.ActiveCellBold.Should().BeFalse();
                harness.KeyTipScope.Should().Be("None");
            }
            finally
            {
                harness.SetButtonKeyTip("UndoQatBtn", originalKeyTip ?? "");
            }
        });
    }

    [Fact]
    public void CustomQuickAccessToolbar_RebuildsBelowRibbonAndRoutesCustomKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ConfigureQuickAccessToolbar(
            [
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Undo,
                QuickAccessToolbarCommandIds.Redo,
                QuickAccessToolbarCommandIds.Bold,
                QuickAccessToolbarCommandIds.Italic,
                QuickAccessToolbarCommandIds.Underline,
                QuickAccessToolbarCommandIds.Print,
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.InsertFunction,
                QuickAccessToolbarCommandIds.NameManager
            ],
            belowRibbon: true);

            harness.TitleBarQatIsVisible.Should().BeFalse();
            harness.BelowRibbonQatIsVisible.Should().BeTrue();
            harness.ButtonIsInBelowRibbonQat("NameManagerQatBtn").Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["1", "4", "01"]);

            harness.SelectActiveCell();
            harness.HandleKeyTip(Key.D4);

            harness.ActiveCellBold.Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void QuickAccessToolbarCatalogKeyTips_AreUniqueAndPrefixSafe()
    {
        var formatter = typeof(MainWindow).GetMethod(
            "FormatQuickAccessToolbarKeyTip",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "FormatQuickAccessToolbarKeyTip");

        var keyTips = Enumerable.Range(1, QuickAccessToolbarCatalog.Commands.Count)
            .Select(index => (string)formatter.Invoke(null, [index])!)
            .ToList();

        keyTips.Should().OnlyHaveUniqueItems();
        keyTips
            .SelectMany(first => keyTips
                .Where(second => !string.Equals(first, second, StringComparison.OrdinalIgnoreCase) &&
                    second.StartsWith(first, StringComparison.OrdinalIgnoreCase))
                .Select(second => $"{first}->{second}"))
            .Should()
            .BeEmpty("top-level QAT keytips must not hold shorter commands hostage as prefixes");
    }

    [Fact]
    public void ContextualPivotKeyTips_WaitForJaBeforeSelectingAnalyzeTab()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowPivotContextualTabs();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);

            harness.SelectedRibbonTabHeader.Should().NotBe("Draw", "visible JA/JD contextual keytips should keep J as a prefix");
            harness.KeyTipScope.Should().Be("TopLevel");

            harness.HandleKeyTip(Key.A);

            harness.SelectedRibbonTabHeader.Should().Be("PivotTable Analyze");
            harness.KeyTipScope.Should().Be("Commands");

            harness.ShowPivotContextualTabs();
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.D);

            harness.SelectedRibbonTabHeader.Should().Be("Design");
            harness.KeyTipScope.Should().Be("Commands");
        });
    }

    [Fact]
    public void CrossTabMenuKeyTips_RouteThroughStaticRibbonMenus()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.SelectedRibbonTabHeader.Should().Be("Page Layout");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Insert Page Break").Should().Be("I");
            harness.ActiveMenuItemGestureText("Remove Page Break").Should().Be("R");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.M, Key.E, Key.C);
            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Error Checking...").Should().Be("E");
            harness.ActiveMenuItemGestureText("Error Checking Options...").Should().Be("O");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.SelectedRibbonTabHeader.Should().Be("View");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Freeze Panes").Should().Be("F");
            harness.ActiveMenuItemGestureText("Unfreeze Panes").Should().Be("U");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.ActiveMenuItemGestureText("100%").Should().Be("1");
            harness.ActiveMenuItemGestureText("Custom...").Should().Be("C");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.A);
            harness.ActiveMenuItemGestureText("Tiled").Should().Be("T");
            harness.ActiveMenuItemGestureText("Cascade").Should().Be("C");
        });
    }

}
