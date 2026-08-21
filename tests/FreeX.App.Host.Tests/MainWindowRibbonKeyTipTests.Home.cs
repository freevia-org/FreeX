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
            harness.ActiveMenuItemGestureText("Keep Source Column Widths").Should().Be("W");
            harness.ActiveMenuItemGestureText("Values & Source Formatting").Should().Be("A");
            harness.ActiveMenuItemGestureText("Transpose").Should().Be("T");
            harness.ActiveMenuItemGestureText("Paste Link").Should().Be("L");
            harness.ActiveMenuItemGestureText("Picture").Should().Be("I");
            harness.ActiveMenuItemGestureText("Linked Picture").Should().Be("K");
            harness.ActiveMenuItemGestureText("Paste Special...").Should().Be("S");
        });
    }

    [Fact]
    public void HomeNumberFormatKeyTip_OpensDropdownAndFocusesComboBox()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.RibbonComboBoxKeyTip("Number Format").Should().Be("N",
                "the Number Format combo still carries its authored keytip");
            harness.RibbonComboBoxIsEnabled("Number Format").Should().BeTrue(
                "editable declarative combos are live through their input event wiring");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            harness.VisibleCommandKeyTips("N").Should().ContainSingle("Number Format",
                "the enabled Number Format combo should be a routable keytip target");
            harness.HandleKeyTip(Key.N);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.NumberFormatGalleryIsOpen.Should().BeTrue();
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

            harness.OverlayBadgeTexts.Should().NotBeEmpty();
            harness.RibbonComboBoxIsEnabled("Number Format").Should().BeTrue();
            harness.OverlayBadgeTexts.Should().Contain("N",
                "the enabled Number Format combo should surface its authored keytip badge");
        });
    }

    [Fact]
    public void KeyTipOverlay_PlacesDropdownCommandBadgesBelowControlFrame()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            var commandBounds = harness.RibbonButtonBoundsByTitle("Paste");
            var badgeBounds = harness.OverlayBadgeBounds("V");

            badgeBounds.Top.Should().BeGreaterThan(commandBounds.Bottom);
            badgeBounds.Top.Should().Be(
                Math.Round(commandBounds.Bottom + 2, MidpointRounding.AwayFromZero));
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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");

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
    public void DeclarativeHomeMenuChoices_AreEnabledAcrossFormattingFamilies()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.L);
            harness.ActiveMenuItemIsEnabled("Data Bars...").Should().BeTrue();
            harness.ActiveMenuItemIsEnabled("Color Scales...").Should().BeTrue();
            harness.ActiveMenuItemIsEnabled("Greater Than...").Should().BeTrue();
            harness.HandleKeyTip(Key.I);
            harness.ActiveMenuItemIsEnabled("3 Arrows").Should().BeTrue();

            harness.OpenRibbonMenu(Key.H, Key.B);
            harness.ActiveMenuItemIsEnabled("Black").Should().BeTrue();
            harness.ActiveMenuItemIsEnabled("Dashed").Should().BeTrue();

            harness.OpenRibbonMenu(Key.H, Key.A, Key.N);
            harness.ActiveMenuItemIsEnabled("US Dollar ($)").Should().BeTrue();
            harness.ActiveMenuItemIsEnabled("Euro (€)").Should().BeTrue();
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
    public void InsertChartsKeyTip_RoutesThroughVisibleChartCommand()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            // Charts is a primary Insert group and must stay visible through its overflow at this width.
            harness.SelectRibbonTab("Insert", 800);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.N);

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.RibbonGroupIsCollapsed("Charts").Should().BeTrue(
                "the wide Charts group may compact, but the overflow button must be visible");
            harness.CollapsedRibbonGroupOverflowWidth("Charts").Should().BeGreaterThan(0,
                "the Charts overflow button must paint so chart commands are reachable");
            harness.VisibleCommandKeyTips("CH").Should().ContainSingle("Charts");

            // C is the first character of the collapsed Charts group keytip CH.
            harness.HandleKeyTip(Key.C);
            harness.KeyTipScope.Should().Be("Commands");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            // H resolves CH -> opens the Charts overflow group and routes into its command menu.
            harness.HandleKeyTip(Key.H);
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Column").Should().Be("CC");
        });
    }

}
