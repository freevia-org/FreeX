using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowRibbonKeyTipTests
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
            harness.ActiveMenuItemGestureText("Unfreeze All").Should().Be("U");
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

    [Fact]
    public void ViewZoomMenuKeyTip_AppliesPresetAndExitsKeyTipMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.StatusZoomText.Should().Be("100%");

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.ActiveMenuItemGestureText("200%").Should().Be("2");

            harness.HandleKeyTip(Key.D2);

            harness.StatusZoomText.Should().Be("200%");
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a zoom preset should close menu keytip mode like Excel");
        });
    }

    [Fact]
    public void PageLayoutBreaksMenuKeyTips_UpdateSheetPageBreaks()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(5, 3, 5, 3);
            harness.ActiveSheetRowPageBreaks.Should().BeEmpty();
            harness.ActiveSheetColumnPageBreaks.Should().BeEmpty();

            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.ActiveMenuItemGestureText("Insert Page Break").Should().Be("I");
            harness.HandleKeyTip(Key.I);

            harness.ActiveSheetRowPageBreaks.Should().Equal(5u);
            harness.ActiveSheetColumnPageBreaks.Should().Equal(3u);
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.ActiveMenuItemGestureText("Remove Page Break").Should().Be("R");
            harness.HandleKeyTip(Key.R);

            harness.ActiveSheetRowPageBreaks.Should().BeEmpty();
            harness.ActiveSheetColumnPageBreaks.Should().BeEmpty();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.HandleKeyTip(Key.I);
            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.ActiveMenuItemGestureText("Reset All Page Breaks").Should().Be("A");
            harness.HandleKeyTip(Key.A);

            harness.ActiveSheetRowPageBreaks.Should().BeEmpty();
            harness.ActiveSheetColumnPageBreaks.Should().BeEmpty();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void PageLayoutSetupMenuKeyTips_UpdatePrintSettings()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetPageMargins.Should().Be(WorksheetPageMargins.Narrow);
            harness.ActiveSheetPageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
            harness.ActiveSheetPaperSize.Should().Be(WorksheetPaperSize.A4);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.P);
            harness.VisibleCommandKeyTips("M").Should().ContainSingle("Margins");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.P, Key.M);
            harness.ActiveMenuItemGestureText("Wide").Should().Be("W");
            harness.HandleKeyTip(Key.W);

            harness.ActiveSheetPageMargins.Should().Be(WorksheetPageMargins.Wide);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.P, Key.O, Key.R);
            harness.ActiveMenuItemGestureText("Landscape").Should().Be("L");
            harness.HandleKeyTip(Key.L);

            harness.ActiveSheetPageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.P, Key.S, Key.Z);
            harness.ActiveMenuItemGestureText("Legal (8.5x14)").Should().Be("G");
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetPaperSize.Should().Be(WorksheetPaperSize.Legal);
            harness.KeyTipScope.Should().Be("None");

            harness.SelectRange(2, 2, 4, 3);
            harness.ActiveSheetPrintArea.Should().BeNull();
            harness.OpenRibbonMenu(Key.P, Key.P, Key.A);
            harness.ActiveMenuItemGestureText("Set Print Area").Should().Be("S");
            harness.HandleKeyTip(Key.S);

            harness.ActiveSheetPrintArea.Should().Be((2u, 2u, 4u, 3u));
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.P, Key.P, Key.A);
            harness.ActiveMenuItemGestureText("Clear Print Area").Should().Be("C");
            harness.HandleKeyTip(Key.C);

            harness.ActiveSheetPrintArea.Should().BeNull();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void PageLayoutSheetOptionKeyTips_TogglePrintGridlinesAndHeadings()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetPrintOptions.Should().Be((false, false));

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.P);
            harness.HandleKeyTip(Key.P);
            harness.KeyTipScope.Should().Be("Commands", "P is the shared Page Layout print-option prefix for PG and PH");
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetPrintOptions.Should().Be((true, false));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.P);
            harness.HandleKeyTip(Key.P);
            harness.HandleKeyTip(Key.H);

            harness.ActiveSheetPrintOptions.Should().Be((true, true));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.P);
            harness.HandleKeyTip(Key.P);
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetPrintOptions.Should().Be((false, true));
            harness.KeyTipScope.Should().Be("None");
        });
    }

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

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("Commands", "R is the prefix for the Ruler keytip RU");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true));

            harness.HandleKeyTip(Key.U);

            harness.ActiveSheetViewOptions.Should().Be((false, false, true), "Excel leaves Ruler unavailable outside Page Layout view");
            harness.KeyTipScope.Should().Be("None");

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
            harness.ActiveMenuItemGestureText("Unfreeze All").Should().Be("U");
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

            harness.KeyTipScope.Should().Be("Commands", "S is a visible prefix for Split and Synchronous Scrolling on the View tab");
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

    [Fact]
    public void DataWhatIfKeyTip_OpensAnalysisMenuWithExcelChoices()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.A, Key.W);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Goal Seek...").Should().Be("G");
            harness.ActiveMenuItemGestureText("Scenario Manager...").Should().Be("S");
            harness.ActiveMenuItemGestureText("Data Table...").Should().Be("D");
        });
    }

    [Fact]
    public void DataOutlineKeyTips_GroupAndUngroupSelectedRows()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.G);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(1);
            harness.RowOutlineLevel(3).Should().Be(1);
            harness.RowOutlineLevel(4).Should().Be(1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.U);

            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(0);
            harness.RowOutlineLevel(3).Should().Be(0);
            harness.RowOutlineLevel(4).Should().Be(0);
        });
    }

    [Fact]
    public void ReviewNoteNavigationKeyTips_MoveAcrossNotesAndThreadedComments()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.AddNote(2, 2, "Plain note");
            harness.AddThreadedComment(4, 4, "Threaded note");
            harness.SelectRange(1, 1, 1, 1);

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((4u, 4u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.P);
            harness.KeyTipScope.Should().Be("Commands", "P is a shared Review prefix before Previous Note resolves");
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void ReviewAllowEditRangesKeyTip_IsDisabledWhenSheetIsProtected()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                workbook.Sheets[0].IsProtected = true;
            });

            harness.RefreshSheetProtectionUi();

            harness.NamedButtonIsEnabled("AllowEditRangesButton").Should().BeFalse();
            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.A);

            harness.KeyTipScope.Should().Be("None", "disabled Review commands should not stay routable through keytips");
            harness.StartScreenIsVisible.Should().BeFalse("Alt,R,A,R must not open the Allow Edit Ranges workflow on a protected sheet");
        });
    }

    [Fact]
    public void InsertShapesKeyTip_OpensShapeMenuAndInsertsRectangle()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(3, 2, 3, 2);

            harness.OpenRibbonMenu(Key.N, Key.S, Key.H);

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Rectangle").Should().Be("R");
            harness.ActiveMenuItemGestureText("Ellipse").Should().Be("E");
            harness.ActiveMenuItemGestureText("Line").Should().Be("L");

            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("None");
            harness.DrawingShapeCount.Should().Be(1);
            harness.LastDrawingShapeKind.Should().Be(DrawingShapeKind.Rectangle);
            harness.LastDrawingShapeAnchor.Should().Be((3u, 2u));
        });
    }

    [Fact]
    public void HomePasteKeyTip_OpensExcelStylePasteMenu()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.V);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Paste").Should().Be("P");
            harness.ActiveMenuItemGestureText("Values").Should().Be("V");
            harness.ActiveMenuItemGestureText("Formulas").Should().Be("F");
            harness.ActiveMenuItemGestureText("Formatting").Should().Be("R");
            harness.ActiveMenuItemGestureText("Transpose").Should().Be("T");
            harness.ActiveMenuItemGestureText("Paste Special...").Should().Be("S");
        });
    }

    [Fact]
    public void HomeNumberFormatKeyTip_OpensDropdownAndFocusesComboBox()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.N);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.NumberFormatDropDownIsOpen.Should().BeTrue();
            harness.NumberFormatBoxHasKeyboardFocus.Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void HomeFormatKeyTip_OpensRowAndColumnSizingMenu()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.O);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Row Height...").Should().Be("R");
            harness.ActiveMenuItemGestureText("AutoFit Row Height").Should().Be("A");
            harness.ActiveMenuItemGestureText("Column Width...").Should().Be("C");
            harness.ActiveMenuItemGestureText("AutoFit Column Width").Should().Be("W");
        });
    }

    [Fact]
    public void CommandKeyTipComboBoxInvocation_ExplicitlyFocusesComboBoxBeforeOpening()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyTips.cs"));

        var comboStart = source.IndexOf("if (match is ComboBox comboBox)", StringComparison.Ordinal);
        var openDropDown = "comboBox.IsDropDownOpen = true;";
        var comboEnd = source.IndexOf(openDropDown, comboStart, StringComparison.Ordinal) + openDropDown.Length;
        var comboBranch = source[comboStart..comboEnd];

        comboBranch.Should().Contain("comboBox.Focus();");
        comboBranch.Should().Contain("Keyboard.Focus(comboBox);");
        comboBranch.IndexOf("comboBox.Focus();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(comboBranch.IndexOf("comboBox.IsDropDownOpen = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedMenuKeyTips_OpenSubmenuScopeBeforeRoutingChildKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.B);
            harness.HandleKeyTip(Key.C);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemSubmenuIsOpen("Line Color").Should().BeTrue();

            harness.HandleKeyTip(Key.K);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void ConditionalFormattingNestedMenuKeyTips_RoutePrefixedChildChoices()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.L);
            harness.HandleKeyTip(Key.I);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemSubmenuIsOpen("Icon Sets").Should().BeTrue();
            harness.ActiveMenuItemGestureText("3 Arrows").Should().Be("I3");

            harness.HandleKeyTip(Key.D3);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void CollapsedRibbonGroupKeyTip_RoutesThroughVisibleOverflowGroup()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetRibbonWidth(220);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.E);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Commands", "E should be treated as the first character of the visible Editing group keytip ED");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.HandleKeyTip(Key.D);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Find & Select").Should().Be("FD");
        });
    }

    [Fact]
    public void CollapsedInsertChartsKeyTip_RoutesThroughVisibleOverflowGroup()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Insert", 800);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.N);
            harness.HandleKeyTip(Key.C);

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.VisibleCommandKeyTips("CH").Should().ContainSingle("Charts");
            harness.KeyTipScope.Should().Be("Commands", "C should be treated as the first character of the collapsed Charts group keytip CH");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.HandleKeyTip(Key.H);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Recommended Charts").Should().Be("RC");
            harness.ActiveMenuItemGestureText("Column Chart").Should().Be("CC");
        });
    }

    [Fact]
    public void FormulasFunctionLibraryDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.L);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("IF").Should().Be("I");
        });
    }

    [Fact]
    public void FormulasUseInFormulaDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedRange(
                    "Sales",
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 1, 1)));
            });

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.I);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Sales").Should().Be("S");
        });
    }

    [Fact]
    public void FormulasAutoSumAndCalculationOptionKeyTips_InvokeMenuItems()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetNumber(1, 2, 10);
            harness.SetNumber(2, 2, 20);
            harness.SelectRange(3, 2, 3, 2);

            harness.OpenRibbonMenu(Key.M, Key.U);
            harness.ActiveMenuItemGestureText("Average").Should().Be("A");
            harness.ActiveMenuItemGestureText("Count Numbers").Should().Be("C");
            harness.ActiveMenuItemGestureText("More…").Should().Be("F");
            harness.HandleKeyTip(Key.A);

            harness.CellFormulaText(3, 2).Should().Be("AVERAGE(B1:B2)");
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            harness.OpenRibbonMenu(Key.M, Key.O);
            harness.ActiveMenuItemGestureText("Manual").Should().Be("M");
            harness.HandleKeyTip(Key.M);

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Manual);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.M, Key.O);
            harness.ActiveMenuItemGestureText("Automatic").Should().Be("A");
            harness.HandleKeyTip(Key.A);

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private static SharedMainWindowSession? SharedSession;

        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly MethodInfo _enterKeyTipMode;
        private readonly MethodInfo _handleActiveRibbonKeyTip;
        private readonly MethodInfo _tryHandleDirectRibbonKeyTip;
        private readonly MethodInfo _tryHandleFocusedRibbonKeyboardNavigation;
        private readonly MethodInfo _isInsideRibbonSurface;
        private readonly MethodInfo _getVisibleKeyTipElements;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _updateSsRecentList;
        private readonly MethodInfo _refreshSheetProtectionUi;
        private readonly MethodInfo _hideStartScreen;
        private readonly MethodInfo _zoomCustomMenuItemClick;
        private readonly Type _scopeType;
        private readonly FieldInfo _scopeField;
        private readonly FieldInfo _activeMenuField;
        private readonly FieldInfo _recentFilesField;

        private MainWindowHarness(MainWindow window, Workbook workbook)
        {
            _window = window;
            _workbook = workbook;
            _enterKeyTipMode = typeof(MainWindow).GetMethod("EnterRibbonKeyTipMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnterRibbonKeyTipMode");
            _handleActiveRibbonKeyTip = typeof(MainWindow).GetMethod("HandleActiveRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HandleActiveRibbonKeyTip");
            _tryHandleDirectRibbonKeyTip = typeof(MainWindow).GetMethod("TryHandleDirectRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleDirectRibbonKeyTip");
            _tryHandleFocusedRibbonKeyboardNavigation = typeof(MainWindow).GetMethod("TryHandleFocusedRibbonKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedRibbonKeyboardNavigation");
            _isInsideRibbonSurface = typeof(MainWindow).GetMethod("IsInsideRibbonSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "IsInsideRibbonSurface");
            _getVisibleKeyTipElements = typeof(MainWindow).GetMethod("GetVisibleKeyTipElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetVisibleKeyTipElements");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _updateSsRecentList = typeof(MainWindow).GetMethod("UpdateSsRecentList", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateSsRecentList");
            _refreshSheetProtectionUi = typeof(MainWindow).GetMethod("RefreshSheetProtectionUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshSheetProtectionUi");
            _hideStartScreen = typeof(MainWindow).GetMethod("HideStartScreen", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HideStartScreen");
            _zoomCustomMenuItemClick = typeof(MainWindow).GetMethod("ZoomCustomMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ZoomCustomMenuItem_Click");
            _scopeType = typeof(MainWindow).GetNestedType("RibbonKeyTipScope", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonKeyTipScope");
            _scopeField = typeof(MainWindow).GetField("_ribbonKeyTipScope", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_ribbonKeyTipScope");
            _activeMenuField = typeof(MainWindow).GetField("_activeRibbonKeyTipMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_activeRibbonKeyTipMenu");
            _recentFilesField = typeof(MainWindow).GetField("_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_recentFiles");
        }

        public string? SelectedRibbonTabHeader =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem tab
                ? tab.Header?.ToString()
                : null;

        public string KeyTipScope => _scopeField.GetValue(_window)?.ToString() ?? "";

        public bool? IsToggleChecked(string name) =>
            (_window.FindName(name) as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked;

        public IReadOnlyList<string> OverlayBadgeTexts =>
            (_window.FindName("KeyTipOverlay") as Canvas)?.Children
                .OfType<Border>()
                .Select(border => (border.Child as TextBlock)?.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToList() ?? [];

        public bool ActiveMenuIsOpen => ActiveMenu?.IsOpen == true;

        public bool FocusedElementIsInsideRibbon =>
            Keyboard.FocusedElement is DependencyObject focusedElement &&
            (bool)_isInsideRibbonSurface.Invoke(_window, [focusedElement])!;

        public bool FocusedElementIsWorksheet =>
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("SheetGrid"));

        public bool StartScreenIsVisible =>
            (_window.FindName("StartScreenOverlay") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool NumberFormatDropDownIsOpen =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsDropDownOpen == true;

        public bool NumberFormatBoxHasKeyboardFocus =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsKeyboardFocusWithin == true ||
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("NumberFormatBox"));

        public bool UndoQatIsEnabled =>
            (_window.FindName("UndoQatBtn") as Button)?.IsEnabled == true;

        public bool RedoQatIsEnabled =>
            (_window.FindName("RedoQatBtn") as Button)?.IsEnabled == true;

        public bool? NamedButtonIsEnabled(string name) =>
            (_window.FindName(name) as Button)?.IsEnabled;

        public string? StatusZoomText =>
            (_window.FindName("StatusZoomText") as TextBlock)?.Text;

        public int ExpectedZoomSelectionPercent
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range } sheetGrid)
                    return 100;

                return (int)Math.Round(ZoomSelectionPlanner.CalculateFitPercent(
                    sheetGrid.ActualWidth,
                    sheetGrid.ActualHeight,
                    range.ColCount,
                    range.RowCount));
            }
        }

        public (bool ShowGridlines, bool ShowHeadings, bool ShowRulers) ActiveSheetViewOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.ShowGridlines, sheet.ShowHeadings, sheet.ShowRulers);
            }
        }

        public bool FormulaBarIsVisible =>
            (_window.FindName("FormulaBarBorder") as FrameworkElement)?.Visibility == Visibility.Visible;

        public WorksheetViewMode ActiveSheetViewMode => _workbook.Sheets[0].ViewMode;

        public IReadOnlyList<uint> ActiveSheetRowPageBreaks => _workbook.Sheets[0].RowPageBreaks.ToList();

        public IReadOnlyList<uint> ActiveSheetColumnPageBreaks => _workbook.Sheets[0].ColumnPageBreaks.ToList();

        public WorksheetPageMargins ActiveSheetPageMargins => _workbook.Sheets[0].PageMargins;

        public WorksheetPageOrientation ActiveSheetPageOrientation => _workbook.Sheets[0].PageOrientation;

        public WorksheetPaperSize ActiveSheetPaperSize => _workbook.Sheets[0].PaperSize;

        public (bool PrintGridlines, bool PrintHeadings) ActiveSheetPrintOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.PrintGridlines, sheet.PrintHeadings);
            }
        }

        public (uint StartRow, uint StartCol, uint EndRow, uint EndCol)? ActiveSheetPrintArea
        {
            get
            {
                var range = _workbook.Sheets[0].PrintArea;
                return range is { } value
                    ? (value.Start.Row, value.Start.Col, value.End.Row, value.End.Col)
                    : null;
            }
        }

        public WorkbookCalculationMode WorkbookCalculationMode => _workbook.CalculationMode;

        public (uint FrozenRows, uint FrozenCols) ActiveSheetFrozenPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.FrozenRows, sheet.FrozenCols);
            }
        }

        public (uint? SplitRow, uint? SplitColumn) ActiveSheetSplitPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.SplitRow, sheet.SplitColumn);
            }
        }

        public (uint Row, uint Col)? SelectedCellAddress
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range })
                    return null;

                return (range.Start.Row, range.Start.Col);
            }
        }

        public bool ActiveCellBold
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                var address = new CellAddress(sheet.Id, 1, 1);
                var styleId = sheet.GetCell(address)?.StyleId
                    ?? sheet.GetStyleOnly(address.Row, address.Col)
                    ?? StyleId.Default;
                return _workbook.GetStyle(styleId).Bold;
            }
        }

        public void SetNumber(uint row, uint col, double value)
        {
            var sheet = _workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
            PumpDispatcher();
        }

        public string? CellFormulaText(uint row, uint col)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.FormulaText;
        }

        public void SelectActiveCell()
        {
            var sheet = _workbook.Sheets[0];
            var address = new CellAddress(sheet.Id, 1, 1);
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
                sheetGrid.SelectedRange = new GridRange(address, address);
            PumpDispatcher();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = _workbook.Sheets[0];
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
            {
                sheetGrid.SelectedRanges = null;
                sheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheet.Id, startRow, startCol),
                    new CellAddress(sheet.Id, endRow, endCol));
            }

            PumpDispatcher();
        }

        public string? SetButtonKeyTip(string name, string keyTip)
        {
            var button = (_window.FindName(name) as ButtonBase)
                ?? throw new InvalidOperationException($"Button {name} was not found.");
            var originalKeyTip = RibbonTooltip.GetKeyTip(button);
            RibbonTooltip.SetKeyTip(button, keyTip);
            PumpDispatcher();
            return originalKeyTip;
        }

        public IDisposable AddHomeRibbonCommandButton(string keyTip, string title)
        {
            var panel = (_window.FindName("HomeRibbonPanel") as Panel)
                ?? throw new InvalidOperationException("HomeRibbonPanel was not found.");
            var button = new Button
            {
                Content = title,
                Width = 96,
                Height = 28,
                IsEnabled = true
            };
            RibbonTooltip.SetTitle(button, title);
            RibbonTooltip.SetKeyTip(button, keyTip);

            panel.Children.Add(button);
            _window.UpdateLayout();
            PumpDispatcher();

            return new DisposableAction(() =>
            {
                panel.Children.Remove(button);
                _window.UpdateLayout();
                PumpDispatcher();
            });
        }

        public void AddNote(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.Comments[new CellAddress(sheet.Id, row, col)] = text;
            PumpDispatcher();
        }

        public void AddThreadedComment(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.ThreadedComments[new CellAddress(sheet.Id, row, col)] = new ThreadedComment(text);
            PumpDispatcher();
        }

        public int RowOutlineLevel(uint row)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.RowOutlineLevels.TryGetValue(row, out var level) ? level : 0;
        }

        public int DrawingShapeCount => _workbook.Sheets[0].DrawingShapes.Count;

        public DrawingShapeKind? LastDrawingShapeKind => _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Kind;

        public (uint Row, uint Col)? LastDrawingShapeAnchor
        {
            get
            {
                var anchor = _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Anchor;
                return anchor is { } value ? (value.Row, value.Col) : null;
            }
        }

        public string? ActiveMenuItemGestureText(string header) =>
            FindActiveMenuItem(header)?.InputGestureText;

        public bool? ActiveMenuItemIsChecked(string header) =>
            FindActiveMenuItem(header)?.IsChecked;

        public bool ActiveMenuItemSubmenuIsOpen(string header) =>
            FindActiveMenuItem(header)?.IsSubmenuOpen == true;

        public WorkbookWindowArrangement WorkbookArrangement =>
            _workbook.WindowArrangement;

        public IReadOnlyList<string> VisibleCommandKeyTips(string keyTip)
        {
            var scope = Enum.Parse(_scopeType, "Commands");
            var elements = ((System.Collections.IEnumerable)_getVisibleKeyTipElements.Invoke(_window, [scope])!)
                .OfType<FrameworkElement>()
                .Where(element => string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase))
                .Select(element => RibbonTooltip.GetTitle(element) ?? element.Name ?? element.GetType().Name)
                .ToList();
            return elements;
        }

        public void ShowPivotContextualTabs()
        {
            if (_window.FindName("PivotTableAnalyzeTab") is TabItem analyzeTab)
                analyzeTab.Visibility = Visibility.Visible;
            if (_window.FindName("PivotTableDesignTab") is TabItem designTab)
                designTab.Visibility = Visibility.Visible;

            _window.UpdateLayout();
            PumpDispatcher();
        }

        private ContextMenu? ActiveMenu => _activeMenuField.GetValue(_window) as ContextMenu;

        private MenuItem? FindActiveMenuItem(string header) =>
            ActiveMenu is { } menu
                ? EnumerateMenuItems(menu).FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                : null;

        public static MainWindowHarness Create(Action<Workbook>? configureWorkbook = null)
        {
            var session = SharedSession ??= CreateSharedSession();
            var window = session.Window;
            if (!window.IsVisible)
                window.Show();

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();

            var createNewWorkbook = typeof(MainWindow).GetMethod("CreateNewWorkbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateNewWorkbook");
            createNewWorkbook.Invoke(window, null);
            configureWorkbook?.Invoke(session.WorkbookRef.Current);

            var harness = new MainWindowHarness(window, session.WorkbookRef.Current);
            harness.ResetUiState();
            return harness;
        }

        private static SharedMainWindowSession CreateSharedSession()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance);

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            window.Show();
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();
            return new SharedMainWindowSession(window, workbookRef);
        }

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedIndex = 1;
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedItem = ribbonTabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SetRecentFiles(IEnumerable<string> recentPaths, IEnumerable<string> pinnedPaths)
        {
            var store = (RecentFilesStore)_recentFilesField.GetValue(_window)!;
            store.Entries.Clear();
            store.Entries.AddRange(recentPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = false
            }));
            store.Entries.AddRange(pinnedPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = true
            }));
            _updateSsRecentList.Invoke(_window, [""]);
            PumpDispatcher();
        }

        public void RefreshSheetProtectionUi()
        {
            _refreshSheetProtectionUi.Invoke(_window, null);
            PumpDispatcher();
        }

        public void EnterKeyTipScope(string scope)
        {
            var value = Enum.Parse(_scopeType, scope);
            _enterKeyTipMode.Invoke(_window, [value]);
            PumpDispatcher();
        }

        public void HandleKeyTip(Key key)
        {
            _handleActiveRibbonKeyTip.Invoke(_window, [key]);
            PumpDispatcher();
        }

        public bool HandleDirectTopLevelKeyTip(Key key)
        {
            var handled = (bool)_tryHandleDirectRibbonKeyTip.Invoke(_window, [key])!;
            PumpDispatcher();
            return handled;
        }

        public bool FocusSelectedRibbonTab()
        {
            if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem tab })
                return false;

            var focused = tab.Focus();
            Keyboard.Focus(tab);
            PumpDispatcher();
            return focused || ReferenceEquals(Keyboard.FocusedElement, tab);
        }

        public bool HandleFocusedRibbonKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            source.Should().NotBeNull("the shared test window must be visible before routing focused-ribbon keyboard input");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            var handled = (bool)_tryHandleFocusedRibbonKeyboardNavigation.Invoke(_window, [args])!;
            PumpDispatcher();
            return handled;
        }

        public void OpenRibbonMenu(Key tabKeyTip, params Key[] commandKeyTips)
        {
            EnterKeyTipScope("TopLevel");
            HandleKeyTip(tabKeyTip);
            foreach (var keyTip in commandKeyTips)
                HandleKeyTip(keyTip);

            ActiveMenuIsOpen.Should().BeTrue(
                "the ribbon keytip sequence {0},{1} should open a menu",
                tabKeyTip,
                string.Join(",", commandKeyTips));
        }

        public void OpenCustomZoomDialogAndCancel()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var zoomDialog = _window.OwnedWindows.OfType<ZoomDialog>().Single();
                    zoomDialog.Close();
                }));

            _zoomCustomMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        private void ResetUiState()
        {
            _hideStartScreen.Invoke(_window, null);
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            _scopeField.SetValue(_window, Enum.Parse(_scopeType, "None"));
            _activeMenuField.SetValue(_window, null);
            if (_window.FindName("KeyTipOverlay") is Canvas overlay)
            {
                overlay.Children.Clear();
                overlay.Visibility = Visibility.Collapsed;
            }
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = 2400;
                ribbonTabs.SelectedIndex = 1;
            }
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            SelectActiveCell();
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void Dispose()
        {
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        private sealed record SharedMainWindowSession(MainWindow Window, WorkbookRef WorkbookRef);

        private sealed class DisposableAction(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                var disposeAction = _dispose;
                if (disposeAction is null)
                    return;

                _dispose = null;
                disposeAction();
            }
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl control)
        {
            foreach (var item in control.Items)
            {
                if (item is not MenuItem menuItem)
                    continue;

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                    yield return child;
            }
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }

    private sealed class TempRecentFiles : IDisposable
    {
        private readonly string _directory;

        private TempRecentFiles(string directory, IReadOnlyList<string> paths)
        {
            _directory = directory;
            Paths = paths;
        }

        public IReadOnlyList<string> Paths { get; }

        public static TempRecentFiles Create(int count)
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreeXRecentKeyTips", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var paths = Enumerable.Range(1, count)
                .Select(index =>
                {
                    var path = Path.Combine(directory, $"Book{index}.xlsx");
                    File.WriteAllText(path, "");
                    return path;
                })
                .ToList();
            return new TempRecentFiles(directory, paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private static void RunSta(Action action)
    {
        StaTestRunner.Run(action);
    }
}
