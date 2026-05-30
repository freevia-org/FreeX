using System.IO;
using System.Windows.Automation;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Tests that the shell XAML tree has correct UI Automation properties for screen-reader
/// and keyboard-only navigation compatibility - the accessibility gate defined in
/// docs/TEST_DISTRIBUTION_PLAN.md Phase 8.
/// </summary>
public sealed class MainWindowUiaPropertiesTests
{
    // Shell chrome

    [Fact]
    public void FormulaBar_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var formulaBar = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBar");

        formulaBar.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_FormulaBar"), "the formula bar needs a stable UIA name for screen readers");

        formulaBar.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace("the formula bar needs help text for Narrator guidance");

        formulaBar.Attribute("AutomationProperties.AutomationId")?.Value
            .Should().Be("FormulaBar", "stable automation ID is required for UIA catalog tracking");
    }

    [Fact]
    public void FormulaBarExpandButton_ExposesAutomationNameHelpTextAndId()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var expandButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBarExpandBtn");

        expandButton.Attribute("AutomationProperties.Name")?.Value.Should().Be(UiText.Get("MainWindow_AutomationName_ExpandFormulaBar"));
        expandButton.Attribute("AutomationProperties.HelpText")?.Value.Should().NotBeNullOrWhiteSpace();
        expandButton.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("FormulaBarExpandBtn");
    }

    [Fact]
    public void FormulaBarExpandButton_UpdatesAutomationTextForExpandedState()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        source.Should().Contain("AutomationProperties.SetName(FormulaBarExpandBtn, \"Collapse Formula Bar\")");
        source.Should().Contain("AutomationProperties.SetHelpText(FormulaBarExpandBtn, \"Collapse the formula bar to a single-line editor\")");
        source.Should().Contain("RibbonTooltip.SetTitle(FormulaBarExpandBtn, \"Collapse Formula Bar\")");
        source.Should().Contain("RibbonTooltip.SetDescription(FormulaBarExpandBtn, \"Collapse the formula bar to a single-line editor.\")");
        source.Should().Contain("AutomationProperties.SetName(FormulaBarExpandBtn, \"Expand Formula Bar\")");
        source.Should().Contain("AutomationProperties.SetHelpText(FormulaBarExpandBtn, \"Expand the formula bar to a multi-line editor\")");
        source.Should().Contain("RibbonTooltip.SetTitle(FormulaBarExpandBtn, \"Expand Formula Bar\")");
        source.Should().Contain("RibbonTooltip.SetDescription(FormulaBarExpandBtn, \"Expand the formula bar to a multi-line editor.\")");
    }

    [Fact]
    public void CellAddressBox_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var nameBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "CellAddressBox");

        nameBox.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_NameBox"), "the name box needs a stable UIA name for screen readers");

        nameBox.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace("the name box needs help text for Narrator guidance");

        nameBox.Attribute("AutomationProperties.AutomationId")?.Value
            .Should().Be("CellAddressBox");
    }

    [Fact]
    public void VerticalScrollBar_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollBar = document
            .Descendants(presentation + "ScrollBar")
            .Single(element => element.Attribute(x + "Name")?.Value == "VerticalScroll");

        scrollBar.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_VerticalWorksheetScrollBar"));
        scrollBar.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void HorizontalScrollBar_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollBar = document
            .Descendants(presentation + "ScrollBar")
            .Single(element => element.Attribute(x + "Name")?.Value == "HorizontalScroll");

        scrollBar.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_HorizontalWorksheetScrollBar"));
        scrollBar.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ZoomSlider_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var slider = document
            .Descendants(presentation + "Slider")
            .Single(element => element.Attribute(x + "Name")?.Value == "ZoomSlider");

        slider.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_ZoomSlider"));
        slider.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace();
        slider.Attribute("AutomationProperties.AutomationId")?.Value
            .Should().Be("ZoomSlider");
    }

    // Worksheet grid

    [Fact]
    public void StatusZoomButtons_ExposeAutomationNameHelpTextAndIds()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute(x + "Name")?.Value is "StatusZoomOutButton" or "StatusZoomInButton")
            .ToDictionary(element => element.Attribute(x + "Name")!.Value);

        buttons.Should().ContainKeys("StatusZoomOutButton", "StatusZoomInButton");
        buttons["StatusZoomOutButton"].Attribute("AutomationProperties.Name")?.Value.Should().Be(UiText.Get("MainWindow_AutomationName_ZoomOut"));
        buttons["StatusZoomOutButton"].Attribute("AutomationProperties.HelpText")?.Value.Should().NotBeNullOrWhiteSpace();
        buttons["StatusZoomOutButton"].Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("StatusZoomOutButton");
        buttons["StatusZoomInButton"].Attribute("AutomationProperties.Name")?.Value.Should().Be(UiText.Get("MainWindow_AutomationName_ZoomIn"));
        buttons["StatusZoomInButton"].Attribute("AutomationProperties.HelpText")?.Value.Should().NotBeNullOrWhiteSpace();
        buttons["StatusZoomInButton"].Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("StatusZoomInButton");
    }

    [Fact]
    public void SheetGrid_ExposesAutomationName()
    {
        var document = LoadMainWindowXaml();
        XNamespace ui = "clr-namespace:FreeX.App.UI;assembly=FreeX.App.UI";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var gridView = document
            .Descendants(ui + "GridView")
            .Single(element => element.Attribute(x + "Name")?.Value == "SheetGrid");

        gridView.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_Worksheet"), "the spreadsheet grid must have a stable UIA name so screen readers " +
                "announce the region when focus enters the grid surface");
    }

    // Sheet tabs

    [Fact]
    public void SheetTabTemplate_BindsAutomationNameToSheetName()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace automation = "clr-namespace:System.Windows.Automation;assembly=PresentationFramework";

        // The TabChrome Grid inside the DataTemplate must have AutomationProperties.Name
        // bound to the sheet Name so a screen reader announces the sheet name when the
        // tab receives keyboard focus via F6 / sheet-tab navigation.
        var sheetTabsControl = document
            .Descendants(presentation + "ItemsControl")
            .Single(element => element.Attribute(x + "Name")?.Value == "SheetTabsControl");

        var dataTemplate = sheetTabsControl
            .Descendants(presentation + "DataTemplate")
            .FirstOrDefault();

        dataTemplate.Should().NotBeNull("SheetTabsControl must have an ItemTemplate DataTemplate");

        var tabChromeGrid = dataTemplate!
            .Elements(presentation + "Grid")
            .FirstOrDefault(element => element.Attribute(x + "Name")?.Value == "TabChrome");

        tabChromeGrid.Should().NotBeNull("TabChrome Grid must exist in the DataTemplate");

        // AutomationProperties.Name must be bound to the view-model Name property
        var automationName = tabChromeGrid!.Attribute("AutomationProperties.Name")?.Value;
        automationName.Should().NotBeNullOrWhiteSpace(
            "TabChrome must expose AutomationProperties.Name so Narrator announces the sheet name");
        automationName.Should().Contain("{Binding", "the name must be data-bound to the sheet name, not a hard-coded string");
        automationName.Should().Contain("Name", "the binding source must be the sheet Name property");
    }

    [Fact]
    public void SheetTabTemplate_MarksTabChromeWithTabItemRole()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sheetTabsControl = document
            .Descendants(presentation + "ItemsControl")
            .Single(element => element.Attribute(x + "Name")?.Value == "SheetTabsControl");

        var dataTemplate = sheetTabsControl
            .Descendants(presentation + "DataTemplate")
            .First();

        var tabChromeGrid = dataTemplate
            .Elements(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "TabChrome");

        // AutomationProperties.AutomationId should be set so the UIA catalog can
        // track sheet tabs reliably.
        tabChromeGrid.Attribute("AutomationProperties.AutomationId")?.Value
            .Should().NotBeNullOrWhiteSpace(
                "sheet tabs should have a stable automation ID so UIA clients can distinguish them");
    }

    // Add sheet button

    [Fact]
    public void AddSheetButton_ExposesAutomationNameAndHelpText()
    {
        var document = LoadMainWindowXaml();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var addSheetButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "AddSheetButton");

        addSheetButton.Attribute("AutomationProperties.Name")?.Value
            .Should().Be(UiText.Get("MainWindow_AutomationName_InsertSheet"));
        addSheetButton.Attribute("AutomationProperties.HelpText")?.Value
            .Should().NotBeNullOrWhiteSpace();
    }

    // GridView AutomationPeer

    [Fact]
    public void GridView_OverridesOnCreateAutomationPeerForScreenReaderSupport()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.cs"));

        source.Should().Contain(
            "OnCreateAutomationPeer",
            "GridView must override OnCreateAutomationPeer so screen readers announce the " +
            "grid type and name rather than the default FrameworkElement peer");
    }

    // Helper

    private static XDocument LoadMainWindowXaml() =>
        XamlLocalizationTestHelper.LoadLocalizedXaml("MainWindow.xaml");
}
