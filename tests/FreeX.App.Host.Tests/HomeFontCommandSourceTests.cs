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
        // R142-services-theme-colors-1: a Theme Colors gallery pick's slot/tint travels alongside
        // the resolved flat color (ColorPickerDialog.SelectedThemeColor) so the applied
        // Font/Fill Color keeps tracking the workbook theme across a later theme change, mirroring
        // the Cell Styles gallery's Accent presets (CellStyleDiffPlanner.AccentDepth).
        source.Should().Contain("TryShowColorPicker(\"Font Color\", _selectedFontColor, allowNoColor: false, out var color, out var themeColor)");
        source.Should().Contain("_selectedFontColor = selected;");
        source.Should().Contain("_selectedFontThemeColor = themeColor;");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\", _selectedFillColor, allowNoColor: true, out var color, out var themeColor)");
        source.Should().Contain("_selectedFillColor = color;");
        source.Should().Contain("_selectedFillThemeColor = themeColor;");
        source.Should().Contain("new StyleDiff(FillColor: null, ClearFill: true)");
        source.Should().Contain("_selectedFontThemeColor is { } themeColor");
        source.Should().Contain("new StyleDiff(FontThemeColor: themeColor)");
        source.Should().Contain("_selectedFillThemeColor is { } themeColor");
        source.Should().Contain("new StyleDiff(FillThemeColor: themeColor)");
    }

    [Fact]
    public void KeyboardFontToggleShortcut_UsesRibbonStateStoreNotBackplaneStub()
    {
        var cells = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var selection = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        // The Ctrl+B/I/U/S keyboard path reads and writes the neutral RibbonStateStore (keyed by
        // CommandName) — the same source of truth the ribbon-click handlers use — instead of a hidden
        // backplane ToggleButton, so the rendered ribbon and keyboard stay consistent and no WPF stub
        // is needed.
        var method = SourceMethodExtractor.ExtractMethodSource(cells, "private void ApplyFontToggleShortcut(");
        method.Should().Contain("var enabled = !IsRibbonCommandChecked(commandName);");
        method.Should().Contain("_ribbonState.SetChecked(commandName, enabled);");
        method.Should().NotContain("button.IsChecked");
        cells.Should().NotContain("ApplyFontToggleShortcut(FontToggleShortcut shortcut, ToggleButton button)");

        // The caller no longer maps the shortcut to a backplane stub button.
        selection.Should().Contain("ApplyFontToggleShortcut(fontToggleShortcut);");
        selection.Should().NotContain("FontToggleShortcut.Bold => BoldButton");
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
