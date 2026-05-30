using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeBorderCommandSourceTests
{
    [Fact]
    public void BordersRibbonButton_ExposesMenuWithExpectedKeyTip()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, "BorderPickerBtn_Click");

        button.ShouldContainInvariantCommandName("Borders");
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"B\"");
        button.Should().Contain("<Button.ContextMenu>");
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
    [InlineData("Draw Border Grid", "G", "BorderDrawGridMenuItem_Click", "All")]
    [InlineData("Erase Border", "E", "BorderEraseMenuItem_Click", "None")]
    [InlineData("More Borders...", "M", "BorderMoreMenuItem_Click", "More")]
    public void BorderMenuItems_ExposeExpectedKeyTipsAndIcons(
        string header,
        string keyTip,
        string handler,
        string iconKind)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
        menuItem.Should().Contain($"<local:BorderMenuIcon Kind=\"{iconKind}\"/>");
    }

    [Fact]
    public void BorderMenuHandlers_RouteThroughBorderServicesAndFormatCells()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("BorderShortcutService.GetAllBorderDiff(_borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetClearBorderDiff()");
        source.Should().Contain("BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetInsideBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderDrawPlanner.CreateDiff(mode, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("OpenFormatCellsDialog(FormatCellsDialogTab.Border)");
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should have an end tag");
        return xaml.Substring(start, end - start + "</Button>".Length);
    }

    private static string ExtractMenuItemElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should be present");

        var start = xaml.LastIndexOf("<MenuItem", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should have a start tag");

        var end = xaml.IndexOf("</MenuItem>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</MenuItem>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} menu item should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }
}
