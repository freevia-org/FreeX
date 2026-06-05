using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var selector = xaml.ExtractElementByName("ComboBox", name);

        selector.Should().Contain("IsEditable=\"True\"");
        selector.ShouldContainInvariantCommandName(title);
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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(handler);

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("FillColorBtn_Click", "FillColorPickerBtn_Click", "HomeFillColorButton")]
    [InlineData("FontColorBtn_Click", "FontColorPickerBtn_Click", "HomeFontColorButton")]
    public void FontColorCommandButtons_AreSplitButtonsWithPickerDropdowns(
        string applyHandler,
        string pickerHandler,
        string automationId)
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(applyHandler);

        button.Should().Contain("local:RibbonMetadata.DropdownMenuButton=\"True\"");
        button.Should().Contain($"local:RibbonMetadata.DropdownClick=\"{pickerHandler}\"");
        button.Should().Contain($"AutomationProperties.AutomationId=\"{automationId}\"");
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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var toggle = xaml.ExtractElementByName("ToggleButton", name);

        toggle.ShouldContainInvariantCommandName(title);
        toggle.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        toggle.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void FontCommandHandlers_RouteThroughStyleDiffsAndPlanners()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true))");
        source.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled))");
        source.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        source.Should().Contain("ApplyFontSizeAndFitRows(FontSizePlanner.Increase(style.FontSize))");
        source.Should().Contain("ApplyFontSizeAndFitRows(FontSizePlanner.Decrease(style.FontSize))");
        source.Should().Contain("private void FontColorBtn_Click(object sender, RoutedEventArgs e) => ApplySelectedFontColor();");
        source.Should().Contain("private void FillColorBtn_Click(object sender, RoutedEventArgs e) => ApplySelectedFillColor();");
        SourceMethodExtractor.ExtractMethodSource(source, "private void FontColorPickerBtn_Click(")
            .Should().Contain("e.Handled = true;");
        SourceMethodExtractor.ExtractMethodSource(source, "private void FillColorPickerBtn_Click(")
            .Should().Contain("e.Handled = true;");
        source.Should().Contain("TryShowColorPicker(\"Font Color\", _selectedFontColor, allowNoColor: false, out var color)");
        source.Should().Contain("_selectedFontColor = selected;");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\", _selectedFillColor, allowNoColor: true, out var color)");
        source.Should().Contain("_selectedFillColor = color;");
        source.Should().Contain("new StyleDiff(FillColor: null, ClearFill: true)");
    }

    [Fact]
    public void FontColorButtons_ExposeStableAutomationMetadata()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var fillButton = xaml.ExtractButtonElementByClickHandler("FillColorBtn_Click");
        var fontButton = xaml.ExtractButtonElementByClickHandler("FontColorBtn_Click");

        fillButton.ShouldContainLocalizedAttribute("AutomationProperties.Name", "Fill Color");
        fillButton.Should().Contain("AutomationProperties.AutomationId=\"HomeFillColorButton\"");
        fillButton.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", "Color the background of the selected cells.");

        fontButton.ShouldContainLocalizedAttribute("AutomationProperties.Name", "Font Color");
        fontButton.Should().Contain("AutomationProperties.AutomationId=\"HomeFontColorButton\"");
        fontButton.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", "Change the color of the text.");
    }
}
