using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeBorderCommandSourceTests
{
    [Fact]
    public void BordersRibbonButton_ExposesMenuWithExpectedKeyTip()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler("BorderPickerBtn_Click");

        button.ShouldContainInvariantCommandName("Borders");
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"B\"");
        button.Should().Contain("<Button.ContextMenu>");
    }

    [Fact]
    public void BordersRibbonMenu_ListsNoBorderOptionAndRoutesItToClearBorders()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var menuItem = xaml.ExtractMenuItemElementByClickHandler("BorderNoneMenuItem_Click");

        menuItem.ShouldContainLocalizedAttribute("Header", "No Border");
        menuItem.ShouldContainInvariantCommandName("No Border");
        menuItem.Should().Contain("local:RibbonTooltip.KeyTip=\"N\"");
        menuItem.Should().Contain("<local:BorderMenuIcon Kind=\"None\"/>");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BorderNoneMenuItem_Click(")
            .Should().Contain("ApplyBorderPreset(RibbonBorderPreset.None)");
        source.Should().Contain("case RibbonBorderPreset.None:");
        source.Should().Contain("ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());");
    }

    [Theory]
    [InlineData("All Borders", "A", "BorderAllMenuItem_Click", "All")]
    [InlineData("Outside Borders", "O", "BorderOutsideMenuItem_Click", "Outside")]
    [InlineData("Inside Borders", "I", "BorderInsideMenuItem_Click", "Inside")]
    [InlineData("No Border", "N", "BorderNoneMenuItem_Click", "None")]
    [InlineData("Bottom Border", "B", "BorderBottomMenuItem_Click", "Bottom")]
    [InlineData("Top Border", "T", "BorderTopMenuItem_Click", "Top")]
    [InlineData("Left Border", "L", "BorderLeftMenuItem_Click", "Left")]
    [InlineData("Right Border", "R", "BorderRightMenuItem_Click", "Right")]
    [InlineData("Thick Bottom Border", "K", "BorderThickBottomMenuItem_Click", "ThickBottom")]
    [InlineData("Bottom Double Border", "D", "BorderBottomDoubleMenuItem_Click", "BottomDouble")]
    [InlineData("Thick Outside Borders", "X", "BorderThickBoxMenuItem_Click", "ThickBox")]
    [InlineData("Top and Bottom Border", "U", "BorderTopAndBottomMenuItem_Click", "TopAndBottom")]
    [InlineData("Top and Thick Bottom Border", "H", "BorderTopAndThickBottomMenuItem_Click", "TopAndThickBottom")]
    [InlineData("Top and Double Bottom Border", "J", "BorderTopAndDoubleBottomMenuItem_Click", "TopAndDoubleBottom")]
    [InlineData("Draw Border", "W", "BorderDrawMenuItem_Click", "Outside")]
    [InlineData("Draw Border Grid", "G", "BorderDrawGridMenuItem_Click", "All")]
    [InlineData("Erase Border", "E", "BorderEraseMenuItem_Click", "None")]
    [InlineData("More Borders...", "M", "BorderMoreMenuItem_Click", "More")]
    public void BorderMenuItems_ExposeExpectedKeyTipsAndIcons(
        string header,
        string keyTip,
        string handler,
        string iconKind)
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var menuItem = xaml.ExtractMenuItemElementByClickHandler(handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
        menuItem.Should().Contain($"<local:BorderMenuIcon Kind=\"{iconKind}\"/>");
    }

    [Theory]
    [InlineData("Black", "K", "BorderLineColorBlackMenuItem_Click", "ColorBlack")]
    [InlineData("Gray", "G", "BorderLineColorGrayMenuItem_Click", "ColorGray")]
    [InlineData("Accent 1", "1", "BorderLineColorAccent1MenuItem_Click", "ColorAccent1")]
    [InlineData("Accent 2", "2", "BorderLineColorAccent2MenuItem_Click", "ColorAccent2")]
    [InlineData("Thin", "T", "BorderLineStyleThinMenuItem_Click", "StyleThin")]
    [InlineData("Medium", "M", "BorderLineStyleMediumMenuItem_Click", "StyleMedium")]
    [InlineData("Thick", "K", "BorderLineStyleThickMenuItem_Click", "StyleThick")]
    [InlineData("Dashed", "D", "BorderLineStyleDashedMenuItem_Click", "StyleDashed")]
    [InlineData("Dotted", "O", "BorderLineStyleDottedMenuItem_Click", "StyleDotted")]
    [InlineData("Double", "U", "BorderLineStyleDoubleMenuItem_Click", "StyleDouble")]
    public void BorderLineMenuItems_ExposeExpectedKeyTipsAndIcons(
        string header,
        string keyTip,
        string handler,
        string iconKind)
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var menuItem = xaml.ExtractMenuItemElementByClickHandler(handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
        menuItem.Should().Contain($"<local:BorderMenuIcon Kind=\"{iconKind}\"/>");
    }

    [Fact]
    public void BorderMenuHandlers_RouteThroughBorderServicesAndFormatCells()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        SourceMethodExtractor.ExtractMethodSource(source, "private void BorderPickerBtn_Click(")
            .Should().Contain("ApplySelectedBorderPreset();");
        source.Should().Contain("private enum RibbonBorderPreset");
        source.Should().Contain("_selectedBorderPreset = preset;");
        source.Should().Contain("BorderShortcutService.GetAllBorderDiff(_borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetClearBorderDiff()");
        source.Should().Contain("BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetInsideBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("SelectionStyleCommandPlanner.CreatePerCellStyleCommand(");
        source.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Draw)");
        source.Should().Contain("BorderDrawPlanner.CommandTitle(mode)");
        source.Should().Contain("BorderDrawPlanner.CreateCommand(");
        source.Should().Contain("OpenFormatCellsDialog(FormatCellsDialogTab.Border)");
    }
}
