using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ViewFreezePanesMenuKeyTips_ApplyPresetsAndExitKeyTipMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.ActiveMenuItemGestureText("Freeze Top Row").Should().Be("R");
            harness.HandleKeyTip(Key.R);

            harness.ActiveSheetFrozenPanes.Should().Be((1u, 0u));
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.ActiveMenuItemGestureText("Freeze First Column").Should().Be("C");
            harness.HandleKeyTip(Key.C);

            harness.ActiveSheetFrozenPanes.Should().Be((0u, 1u));
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.SelectRange(2, 2, 2, 2);
            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.ActiveMenuItemGestureText("Freeze Panes").Should().Be("F");
            harness.HandleKeyTip(Key.F);

            harness.ActiveSheetFrozenPanes.Should().Be((1u, 1u));
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.ActiveMenuItemGestureText("Unfreeze Panes").Should().Be("U");
            harness.HandleKeyTip(Key.U);

            harness.ActiveSheetFrozenPanes.Should().Be((0u, 0u));
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void ViewArrangeAllMenuKeyTips_UpdateWorkbookArrangementAndExitKeyTipMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.WorkbookArrangement.Should().Be(WorkbookWindowArrangement.Tiled);

            harness.OpenRibbonMenu(Key.W, Key.A);
            harness.ActiveMenuItemGestureText("Tiled").Should().Be("T");
            harness.ActiveMenuItemIsChecked("Tiled").Should().BeTrue();
            harness.HandleKeyTip(Key.V);

            harness.WorkbookArrangement.Should().Be(WorkbookWindowArrangement.Vertical);
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.OpenRibbonMenu(Key.W, Key.A);
            harness.ActiveMenuItemGestureText("Cascade").Should().Be("C");
            harness.ActiveMenuItemIsChecked("Vertical").Should().BeTrue();
            harness.HandleKeyTip(Key.C);

            harness.WorkbookArrangement.Should().Be(WorkbookWindowArrangement.Cascade);
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void ViewSplitKeyTip_TogglesSheetSplitAndExitsKeyTipMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(2, 2, 2, 2);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.S);

            harness.KeyTipScope.Should().Be("Commands", "S is a visible prefix for the Split keytip on the View tab");
            harness.ActiveSheetSplitPanes.Should().Be((null, null));

            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetSplitPanes.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.S);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetSplitPanes.Should().Be((null, null));
            harness.KeyTipScope.Should().Be("None");
        });
    }

    // Regression: a brand-new split must be positioned at the selection's active/anchor cell, not
    // the viewport-midpoint fallback that Excel reserves ONLY for an A1 active cell (see
    // SplitViewBtn_Click in MainWindow.ViewCommands.cs). A multi-cell selection whose anchor is
    // deep in the sheet (C4:E6, anchor C4) must split exactly at that anchor -- proving the Split
    // handler reads the real selection anchor rather than a stale one left over from A1.
    [Fact]
    public void ViewSplit_PositionsNewSplitAtSelectionAnchor_NotViewportMidpoint()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(4, 3, 6, 5);
            harness.SelectionAnchor.Should().Be((4u, 3u),
                "SelectRange must keep the window's active/anchor cell in sync with the selection");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.S);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetSplitPanes.Should().Be((4u, 3u),
                "a new split anchors at the selected cell (row 4, col 3), not the midpoint-of-viewport A1 fallback");
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void ViewWindowSingleHostState_DisablesPairCommandsIncludingReset()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);

            // New Window is always live. Reset Window Position belongs to an active side-by-side pair.
            harness.CommandButtonIsEnabled("ViewNewWindowBtn").Should().BeTrue();
            harness.VisibleCommandKeyTips("NW").Should().ContainSingle("New Window");
            harness.CommandButtonIsEnabled("ViewResetWindowPositionBtn").Should().BeFalse();
            harness.VisibleCommandKeyTips("RP").Should().BeEmpty();

            // The remaining multi-window commands exist but are disabled in a lone-window host, with
            // owned help text explaining why, so they expose no command key tip while disabled.
            harness.CommandButtonIsEnabled("ViewSwitchWindowsBtn").Should().BeFalse();
            harness.CommandButtonHelpText("ViewSwitchWindowsBtn").Should().Contain("more than one visible workbook window");

            harness.CommandButtonIsEnabled("ViewHideWindowBtn").Should().BeFalse();
            harness.CommandButtonHelpText("ViewHideWindowBtn").Should().Contain("at least one other window");

            harness.CommandButtonIsEnabled("ViewUnhideWindowBtn").Should().BeFalse();
            harness.CommandButtonHelpText("ViewUnhideWindowBtn").Should().Contain("no workbook window is currently hidden");

            harness.CommandButtonIsEnabled("ViewSideBySideBtn").Should().BeFalse();
            harness.CommandButtonHelpText("ViewSideBySideBtn").Should().Contain("requires a second visible workbook window");

            harness.CommandButtonIsEnabled("ViewSynchronousScrollingBtn").Should().BeFalse();
            harness.CommandButtonHelpText("ViewSynchronousScrollingBtn").Should().Contain("View Side by Side");

            harness.VisibleCommandKeyTips("H").Should().NotContain("Hide");
            harness.VisibleCommandKeyTips("U").Should().NotContain("Unhide");
            harness.VisibleCommandKeyTips("B").Should().NotContain("View Side by Side");
            harness.VisibleCommandKeyTips("SS").Should().BeEmpty();
        });
    }
}
