using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HomeFontCommandSourceTests
{
    [Theory]
    [InlineData("FontNameBox", "Font", "FF", "FontNameBox_SelectionChanged", "FontNameBox_KeyDown", "FontNameBox_LostKeyboardFocus")]
    [InlineData("FontSizeBox", "Font Size", "FS", "FontSizeBox_SelectionChanged", "FontSizeBox_KeyDown", "FontSizeBox_LostKeyboardFocus")]
    public void FontEditableSelectors_ExposeExpectedKeyTipsAndCommitHandlers(
        string name,
        string title,
        string keyTip,
        string selectionHandler,
        string keyHandler,
        string lostFocusHandler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var selector = ExtractElementByName(xaml, "ComboBox", name);

        selector.Should().Contain("IsEditable=\"True\"");
        selector.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        selector.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        selector.Should().Contain($"SelectionChanged=\"{selectionHandler}\"");
        selector.Should().Contain($"KeyDown=\"{keyHandler}\"");
        selector.Should().Contain($"LostKeyboardFocus=\"{lostFocusHandler}\"");
    }

    [Theory]
    [InlineData("Increase Font Size", "FG", "IncreaseFontSizeBtn_Click")]
    [InlineData("Decrease Font Size", "FK", "DecreaseFontSizeBtn_Click")]
    [InlineData("Fill Color", "H", "FillColorBtn_Click")]
    [InlineData("Font Color", "FC", "FontColorBtn_Click")]
    public void FontCommandButtons_ExposeExpectedKeyTipsAndHandlers(
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
    [InlineData("BoldButton", "Bold", "1", "BoldButton_Click")]
    [InlineData("ItalicButton", "Italic", "2", "ItalicButton_Click")]
    [InlineData("UnderlineButton", "Underline", "3", "UnderlineButton_Click")]
    [InlineData("StrikeButton", "Strikethrough", "4", "StrikeButton_Click")]
    public void FontToggleButtons_ExposeExpectedKeyTipsAndHandlers(
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

    [Fact]
    public void FontCommandHandlers_RouteThroughStyleDiffsAndPlanners()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true))");
        source.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled))");
        source.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        source.Should().Contain("ApplyFontSizeAndFitRows(FontSizePlanner.Increase(style.FontSize))");
        source.Should().Contain("ApplyFontSizeAndFitRows(FontSizePlanner.Decrease(style.FontSize))");
        source.Should().Contain("TryShowColorPicker(\"Font Color\", initial, allowNoColor: false, out var color)");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\", initial, allowNoColor: true, out var color)");
        source.Should().Contain("new StyleDiff(FillColor: null, ClearFill: true)");
    }

    private static string ExtractElementByName(string xaml, string elementName, string name)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"the {name} {elementName} should be present");

        var start = xaml.LastIndexOf($"<{elementName}", nameIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {name} {elementName} should have a start tag");

        var selfClosing = xaml.IndexOf("/>", nameIndex, StringComparison.Ordinal);
        var closing = xaml.IndexOf($"</{elementName}>", nameIndex, StringComparison.Ordinal);
        if (closing >= nameIndex && (selfClosing < 0 || closing < selfClosing))
            return xaml.Substring(start, closing - start + elementName.Length + 3);

        selfClosing.Should().BeGreaterThanOrEqualTo(nameIndex, $"the {name} {elementName} should have an end tag or be self-closing");
        return xaml.Substring(start, selfClosing - start + 2);
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
}
