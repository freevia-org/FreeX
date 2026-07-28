using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void PageLayoutScaleToFitComboKeyTips_OpenEditableCombos()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.P);

            harness.RibbonComboBoxIsEnabled("Scale Width").Should().BeTrue();
            harness.RibbonComboBoxIsEnabled("Scale Height").Should().BeTrue();
            harness.RibbonComboBoxIsEnabled("Scale Percent").Should().BeTrue();
            harness.VisibleCommandKeyTips("SW").Should().ContainSingle("Scale Width");
            harness.VisibleCommandKeyTips("SH").Should().ContainSingle("Scale Height");
            harness.VisibleCommandKeyTips("SC").Should().ContainSingle("Scale Percent");

            harness.HandleKeyTip(Key.S);
            harness.KeyTipScope.Should().Be("Commands");
            harness.HandleKeyTip(Key.W);

            harness.RibbonComboBoxDropDownIsOpen("Scale Width").Should().BeTrue();
            harness.RibbonComboBoxHasKeyboardFocus("Scale Width").Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
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

            harness.ActiveSheetPageMargins.Should().Be(WorksheetPageMargins.Normal);
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
            harness.ActiveMenuItemGestureText("Legal").Should().Be("G");
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetPaperSize.Should().Be(WorksheetPaperSize.Legal);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.P, Key.S, Key.Z);
            harness.ActiveMenuItemGestureText("B4 (JIS)").Should().Be("B4");
            harness.HandleKeyTip(Key.B);
            harness.KeyTipScope.Should().Be("Menu", "B is the shared prefix for B4 and B5 paper sizes");
            harness.HandleKeyTip(Key.D4);

            harness.ActiveSheetPaperSize.Should().Be(WorksheetPaperSize.B4);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.P, Key.S, Key.Z);
            harness.ActiveMenuItemGestureText("B5 (JIS)").Should().Be("B5");
            harness.HandleKeyTip(Key.B);
            harness.KeyTipScope.Should().Be("Menu", "B is the shared prefix for B4 and B5 paper sizes");
            harness.HandleKeyTip(Key.D5);

            harness.ActiveSheetPaperSize.Should().Be(WorksheetPaperSize.B5);
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

}
