using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ViewZoomCommandKeyTips_ResetAndFitSelection()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.HandleKeyTip(Key.D2);
            harness.StatusZoomText.Should().Be("200%");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.Z);

            harness.KeyTipScope.Should().Be("Commands", "Z is a visible prefix for 100% and Zoom to Selection");

            harness.HandleKeyTip(Key.D1);

            harness.StatusZoomText.Should().Be("100%");
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();

            harness.SelectRange(1, 1, 12, 6);
            var expectedFitPercent = harness.ExpectedZoomSelectionPercent;

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.Z);
            harness.HandleKeyTip(Key.S);

            harness.StatusZoomText.Should().Be($"{expectedFitPercent}%");
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();

            harness.OpenRibbonMenu(Key.H, Key.B);
            harness.HandleKeyTip(Key.S);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemSubmenuIsOpen("Line Style").Should().BeTrue();
            harness.ActiveMenuItemGestureText("Dashed").Should().Be("D");

            harness.HandleKeyTip(Key.D);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void ZoomCustomDialogCancel_ReturnsFocusToWorksheet()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenCustomZoomDialogAndCancel();

            harness.FocusedElementIsWorksheet.Should().BeTrue();
        });
    }

    [Fact]
    public void ViewShowToggleKeyTips_UpdateSheetAndFormulaBarState()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetViewOptions.Should().Be((true, true, true));
            var initialFormulaBarVisibility = harness.FormulaBarIsVisible;

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);
            harness.KeyTipScope.Should().Be("Commands", "V is the shared Excel Show group prefix for Gridlines, Headings, and Formula Bar");
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetViewOptions.Should().Be((false, true, true));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);
            harness.HandleKeyTip(Key.H);

            harness.ActiveSheetViewOptions.Should().Be((false, false, true));
            harness.KeyTipScope.Should().Be("None");

            // Outside Page Layout, Ruler (RU) is disabled, but Reset Window Position (RP) is always
            // live and shares the "R" prefix, so pressing R now enters the pending command scope.
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("Commands", "R is now a live prefix for the Reset Window Position keytip RP");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true));

            // Completing RP resets this window's geometry; it never touches the worksheet view options.
            harness.HandleKeyTip(Key.P);

            harness.KeyTipScope.Should().Be("None");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true), "Excel leaves Ruler unavailable outside Page Layout view");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageLayout);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("Commands", "R is the prefix for the Ruler keytip RU");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true));

            harness.HandleKeyTip(Key.U);

            harness.ActiveSheetViewOptions.Should().Be((false, false, false));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);

            harness.KeyTipScope.Should().Be("Commands", "V is the prefix for Excel-style Show keytips VG/VH/VF");
            harness.FormulaBarIsVisible.Should().Be(initialFormulaBarVisibility);

            harness.HandleKeyTip(Key.F);

            harness.FormulaBarIsVisible.Should().Be(!initialFormulaBarVisibility);
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void ViewWorkbookModeKeyTips_UpdateSheetViewMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.Normal);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.I);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageLayout);
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.L);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.Normal);
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

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

    [Fact]
    public void ViewWindowSingleHostState_DisablesMultiWindowCommandsButKeepsResetLive()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);

            // New Window is always live; Reset Window Position is always available (re-centers this window).
            harness.CommandButtonIsEnabled("ViewNewWindowBtn").Should().BeTrue();
            harness.VisibleCommandKeyTips("NW").Should().ContainSingle("New Window");
            harness.CommandButtonIsEnabled("ViewResetWindowPositionBtn").Should().BeTrue();
            harness.VisibleCommandKeyTips("RP").Should().ContainSingle("Reset Window Position");

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

    [Fact]
    public void FocusedRibbonTabAndEscape_StayInRibbonThenReturnToWorksheet()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusSelectedRibbonTab().Should().BeTrue();
            harness.FocusedElementIsInsideRibbon.Should().BeTrue();

            harness.HandleFocusedRibbonKey(Key.Tab).Should().BeTrue();

            harness.FocusedElementIsInsideRibbon.Should().BeTrue("focused-ribbon Tab should request WPF ribbon traversal instead of worksheet movement");

            harness.HandleFocusedRibbonKey(Key.Escape).Should().BeTrue();

            harness.FocusedElementIsWorksheet.Should().BeTrue("Escape should leave focused ribbon navigation and return to the worksheet");
        });
    }

}
