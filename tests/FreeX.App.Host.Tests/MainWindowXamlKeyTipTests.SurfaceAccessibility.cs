using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void NonRibbonTooltipClickButtons_HaveAccessibleNames()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click") is not null)
            .Where(button => button.Attribute(ribbonWpf + "RibbonTooltip.Title") is null)
            .Where(button => button.Attribute("AutomationProperties.Name") is null)
            .Select(button =>
                button.Attribute(x + "Name")?.Value ??
                LocalizedAttribute(button, "Content") ??
                button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("clickable buttons outside the ribbon-tooltip command system should still have accessible names");
    }

    [Theory]
    [InlineData("CellAddressBox", "MainWindow_AutomationName_NameBox", "MainWindow_AutomationHelpText_GoToACellOrNamedRange")]
    [InlineData("FormulaBar", "MainWindow_AutomationName_FormulaBar", "MainWindow_AutomationHelpText_EditTheActiveCellValueOrFormula")]
    public void FormulaBarTextFields_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedNameKey,
        string expectedHelpTextKey)
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var elementType = controlName == "CellAddressBox" ? "ComboBox" : "TextBox";
        var input = document
            .Descendants(presentation + elementType)
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = input.Attribute("AutomationProperties.Name");
        var helpText = input.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("formula bar text fields are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("formula bar text fields should announce their workflow role");
        LocalizedAttribute(input, "AutomationProperties.Name").Should().Be(UiText.Get(expectedNameKey));
        LocalizedAttribute(input, "AutomationProperties.HelpText").Should().Be(UiText.Get(expectedHelpTextKey));
    }

    [Fact]
    public void NameBox_CommitsTypedReferenceWithEnter()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var nameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "CellAddressBox");

        nameBox.Attribute("IsEditable")?.Value.Should().Be("True");
        nameBox.Attribute("StaysOpenOnEdit")?.Value.Should().Be("True");
        nameBox.Attribute("KeyDown")?.Value.Should().Be("CellAddressBox_KeyDown");
        source.Should().Contain("private void CellAddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)");
        // K24/K48: whole-column (A:A) / whole-row (5:5) reference parsing moved from
        // GoToDialog's own parser into the shared WorkbookReferenceNavigator so the Name Box,
        // Go To dialog, and Go To Special all resolve references identically.
        source.Should().Contain("WorkbookReferenceNavigator.TryParseReferenceRange(");
        source.Should().Contain("SetSelectionRange(selectedRange, selectedRange.Start);");
        source.Should().Contain("FocusSheetGridIfNeeded();");
        source.Should().Contain("CellAddressBox.SelectAll();");
    }

    [Fact]
    public void NameBox_EscapeCancelsTypedReferenceAndReturnsToGrid()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain("if (e.Key == Key.Escape && e.KeyboardDevice.Modifiers == ModifierKeys.None)");
        source.Should().Contain("RestoreCellAddressBoxText();");
        source.Should().Contain("FocusSheetGridIfNeeded();");
        source.Should().Contain("private void RestoreCellAddressBoxText()");
        source.Should().Contain("CellAddressBox.Text = SheetGrid.SelectedRange is { } range");
        source.Should().Contain("? FormatNameBoxSelectionText(range)");
    }

    [Fact]
    public void FormulaBarTextFields_UseReadableExcelScaleSizing()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var formulaBar = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBar");
        var nameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "CellAddressBox");
        var overlay = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBarReferenceOverlay");

        formulaBar.Attribute("FontSize")?.Value.Should().Be("18");
        formulaBar.Attribute("MinHeight")?.Value.Should().Be("30");
        formulaBar.Attribute("Padding")?.Value.Should().Be("6,3");
        nameBox.Attribute("FontSize")?.Value.Should().Be("15");
        nameBox.Attribute("MinHeight")?.Value.Should().Be("30");
        overlay.Attribute("FontSize")?.Value.Should().Be("18");
    }

    [Theory]
    [InlineData("VerticalScroll", "MainWindow_AutomationName_VerticalWorksheetScrollBar", "MainWindow_AutomationHelpText_ScrollWorksheetRows")]
    [InlineData("HorizontalScroll", "MainWindow_AutomationName_HorizontalWorksheetScrollBar", "MainWindow_AutomationHelpText_ScrollWorksheetColumns")]
    public void WorksheetScrollBars_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedNameKey,
        string expectedHelpTextKey)
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollBar = document
            .Descendants(presentation + "ScrollBar")
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = scrollBar.Attribute("AutomationProperties.Name");
        var helpText = scrollBar.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("worksheet scrollbars are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("worksheet scrollbars should announce whether they move rows or columns");
        LocalizedAttribute(scrollBar, "AutomationProperties.Name").Should().Be(UiText.Get(expectedNameKey));
        LocalizedAttribute(scrollBar, "AutomationProperties.HelpText").Should().Be(UiText.Get(expectedHelpTextKey));
    }
}
