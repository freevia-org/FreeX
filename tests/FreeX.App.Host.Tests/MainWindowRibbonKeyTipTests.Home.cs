using System.IO;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
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
    public void KeyTipOverlay_PlacesComboBoxBadgesBelowSelectorFrame()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            var selectorBounds = harness.ElementBounds("NumberFormatBox");
            var badgeBounds = harness.OverlayBadgeBounds("N");

            badgeBounds.Top.Should().BeGreaterThan(selectorBounds.Bottom);
            badgeBounds.Top.Should().Be(
                Math.Round(selectorBounds.Bottom + 2, MidpointRounding.AwayFromZero));
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
            harness.ActiveMenuItemGestureText("3 Arrows").Should().Be("3");

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

}
