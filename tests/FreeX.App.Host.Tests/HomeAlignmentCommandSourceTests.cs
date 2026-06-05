using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var toggle = xaml.ExtractElementByName("ToggleButton", name);

        toggle.ShouldContainInvariantCommandName(title);
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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(handler);

        button.ShouldContainInvariantCommandName(title);
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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var menuItem = xaml.ExtractMenuItemElementByClickHandler(handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void AlignmentCommandHandlers_RouteThroughStyleDiffsAndRepeatableMergeCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

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

}
