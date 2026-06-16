using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeFontCommandSourceTests
{

    [Fact]
    public void FontCommandHandlers_RouteThroughStyleDiffsAndPlanners()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Bold: IsRibbonCommandChecked(\"Bold\")))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Italic: IsRibbonCommandChecked(\"Italic\")))");
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
    public void SharedComboBoxDropdownStyle_ProvidesWheelHoverAndSelectionBehavior()
    {
        var appXaml = DialogSourceTestSupport.ReadHostSources("App.xaml");
        var resources = DialogSourceTestSupport.ReadHostSources("Resources\\MainWindowResources.xaml");
        var behaviorSource = DialogSourceTestSupport.ReadHostSources("ComboBoxDropDownWheelBehavior.cs");

        appXaml.Should().Contain("local:ComboBoxDropDownWheelBehavior.IsEnabled");
        resources.Should().Contain("local:ComboBoxDropDownWheelBehavior.IsEnabled");
        resources.Should().Contain("Property=\"IsHighlighted\" Value=\"True\"");
        resources.Should().Contain("Property=\"IsSelected\" Value=\"True\"");
        resources.Should().Contain("Property=\"FontWeight\" Value=\"SemiBold\"");
        behaviorSource.Should().Contain("InputManager.Current.PreProcessInput += InputManager_PreProcessInput");
        behaviorSource.Should().Contain("DropDownOpened += ComboBox_DropDownOpened");
        behaviorSource.Should().Contain("ScrollDropDown(scrollViewer, wheelArgs)");
    }

}
