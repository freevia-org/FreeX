using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HomeAlignmentCommandSourceTests
{
    [Theory]
    [InlineData("AlignTopBtn", "Top Align", "AT", "AlignTopBtn_Click")]
    [InlineData("AlignMiddleBtn", "Middle Align", "AM", "AlignMiddleBtn_Click")]
    [InlineData("AlignBottomBtn", "Bottom Align", "AB", "AlignBottomBtn_Click")]
    [InlineData("AlignLeftBtn", "Align Left", "AL", "AlignLeftBtn_Click")]
    [InlineData("AlignCenterBtn", "Center", "AC", "AlignCenterBtn_Click")]
    [InlineData("AlignRightBtn", "Align Right", "AR", "AlignRightBtn_Click")]
    [InlineData("WrapTextBtn", "Wrap Text", "W", "WrapTextBtn_Click")]
    public void AlignmentToggleButtons_ExposeExpectedKeyTipsAndHandlers(
        string name,
        string title,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var toggle = ExtractElementByName(xaml, "ToggleButton", name);

        toggle.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        toggle.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        toggle.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Orientation", "RO", "OrientationPickerBtn_Click")]
    [InlineData("Decrease Indent", "AO", "IndentDecBtn_Click")]
    [InlineData("Increase Indent", "AI", "IndentIncBtn_Click")]
    [InlineData("Merge &amp; Center", "M", "MergeCenterBtn_Click")]
    public void AlignmentCommandButtons_ExposeExpectedKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, handler);

        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Horizontal", "H", "OrientHorizMenuItem_Click")]
    [InlineData("Angle Counterclockwise", "A", "OrientAngleCCWMenuItem_Click")]
    [InlineData("Angle Clockwise", "C", "OrientAngleCWMenuItem_Click")]
    [InlineData("Vertical Text", "V", "OrientVertMenuItem_Click")]
    [InlineData("Rotate Text Up", "U", "OrientRotateUpMenuItem_Click")]
    [InlineData("Rotate Text Down", "D", "OrientRotateDownMenuItem_Click")]
    public void OrientationMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.Should().Contain($"Header=\"{header}\"");
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void AlignmentCommandHandlers_RouteThroughStyleDiffsAndRepeatableMergeCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(WrapText: WrapTextBtn.IsChecked == true))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)))");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        source.Should().Contain("\"Merge & Center\"");
        source.Should().Contain("CreateMergeAndCenterCommand");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 0))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: -45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 90))");
    }

    private static string ExtractElementByName(string xaml, string elementName, string name)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"the {name} {elementName} should be present");

        var start = xaml.LastIndexOf($"<{elementName}", nameIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {name} {elementName} should have a start tag");

        var end = xaml.IndexOf($"</{elementName}>", nameIndex, StringComparison.Ordinal);
        if (end >= nameIndex)
            return xaml.Substring(start, end - start + elementName.Length + 3);

        end = xaml.IndexOf("/>", nameIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(nameIndex, $"the {name} {elementName} should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</Button>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should be self-closing or have an end tag");
        return xaml.Substring(start, end - start + 2);
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
