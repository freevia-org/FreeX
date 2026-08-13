using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void StatusBarZoomCommandButtons_HaveAltKeyTips()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "ZoomOutBtn_Click" or "ZoomInBtn_Click")
            .Where(button => button.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
            .Select(button => LocalizedAttribute(button, "Content") ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("status-bar zoom commands should participate in the visible command keytip contract");
    }

    [Fact]
    public void StatusBarZoomSlider_HasAccessibleRangeMetadata()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var zoomSlider = document
            .Descendants(presentation + "Slider")
            .Single(slider => slider.Attribute(x + "Name")?.Value == "ZoomSlider");

        var name = zoomSlider.Attribute("AutomationProperties.Name");
        var helpText = zoomSlider.Attribute("AutomationProperties.HelpText");
        var tooltip = zoomSlider.Attribute("ToolTip");

        name.Should().NotBeNull("the keyboard-focusable zoom slider needs a screen-reader name");
        helpText.Should().NotBeNull("the zoom slider should disclose the Excel-style zoom range");
        tooltip.Should().NotBeNull("the zoom slider should expose a standard pointer tooltip");

        LocalizedAttribute(zoomSlider, "AutomationProperties.Name").Should().Be(UiText.Get("MainWindow_AutomationName_ZoomSlider"));
        LocalizedAttribute(zoomSlider, "AutomationProperties.HelpText").Should().Contain("10%").And.Contain("400%");
        LocalizedAttribute(zoomSlider, "ToolTip").Should().Be(UiText.Get("MainWindow_ToolTip_Zoom"));
        zoomSlider.Attribute("Minimum").Should().BeNull("status zoom range is planned by StatusBarZoomSliderPlanner");
        zoomSlider.Attribute("Maximum").Should().BeNull("status zoom range is planned by StatusBarZoomSliderPlanner");
        zoomSlider.Attribute("Ticks").Should().BeNull("status zoom tick marks are planned by StatusBarZoomSliderPlanner");
    }

    [Fact]
    public void StatusBarCustomizeMenu_ExposesCheckableAggregatePanes()
    {
        // The status-bar customize menu is now single-sourced through the neutral
        // StatusBarCustomizeContextMenuPlanner (rendered at runtime via StatusBarRoot_Loaded) instead of being
        // hand-authored in XAML. Assert the planner still describes the checkable aggregate toggles with the
        // persisted-option Tag, stable AutomationId, and localized header the previous XAML carried.
        var paneItems = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
            .Where(command => command.OptionTag is "Average" or "Count" or "NumericalCount" or "Sum" or "Minimum" or "Maximum")
            .Select(command => (
                Name: command.AutomationId,
                Header: UiText.Get(command.ResourceKey),
                Tag: command.OptionTag,
                IsCheckable: command.IsCheckable))
            .ToArray();

        paneItems.Should().Equal(
            ("StatusBarAverageMenuItem", UiText.Get("StatusBar_Average"), "Average", true),
            ("StatusBarCountMenuItem", UiText.Get("StatusBar_Count"), "Count", true),
            ("StatusBarNumericalCountMenuItem", UiText.Get("StatusBar_NumericalCount"), "NumericalCount", true),
            ("StatusBarMinimumMenuItem", UiText.Get("StatusBar_Minimum"), "Minimum", true),
            ("StatusBarMaximumMenuItem", UiText.Get("StatusBar_Maximum"), "Maximum", true),
            ("StatusBarSumMenuItem", UiText.Get("StatusBar_Sum"), "Sum", true));
    }

    [Fact]
    public void StatusBarViewShortcutButtons_InvokeWorkbookViewCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document
            .Descendants(presentation + "ToggleButton")
            .Where(button => button.Attribute(x + "Name")?.Value is
                "StatusNormalViewButton" or
                "StatusPageLayoutViewButton" or
                "StatusPageBreakPreviewButton")
            .ToDictionary(button => button.Attribute(x + "Name")!.Value);

        buttons.Keys.Should().BeEquivalentTo(
            "StatusNormalViewButton",
            "StatusPageLayoutViewButton",
            "StatusPageBreakPreviewButton");
        buttons["StatusNormalViewButton"].Attribute("Click")?.Value.Should().Be("NormalViewBtn_Click");
        buttons["StatusPageLayoutViewButton"].Attribute("Click")?.Value.Should().Be("PageLayoutViewBtn_Click");
        buttons["StatusPageBreakPreviewButton"].Attribute("Click")?.Value.Should().Be("PageBreakPreviewBtn_Click");
        buttons.Values.All(button =>
            string.Equals(button.Attribute("Style")?.Value, "{StaticResource StatusBarViewToggleButtonStyle}", StringComparison.Ordinal) &&
            button.Attribute("AutomationProperties.AutomationId") != null &&
            button.Attribute("ToolTip") != null)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void StatusBarAggregates_AreConstrainedAwayFromZoomControls()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var statusBarGrid = document
            .Descendants(presentation + "Grid")
            .Single(grid => grid.Attribute(x + "Name")?.Value == "StatusBarGrid");

        statusBarGrid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Select(column => column.Attribute("Width")?.Value)
            .Should()
            .Equal("Auto", "*", "Auto");

        var statsViewport = statusBarGrid
            .Descendants(presentation + "Border")
            .Single(border => border.Attribute(x + "Name")?.Value == "StatusStatsViewport");

        statsViewport.Attribute("Grid.Column")?.Value.Should().Be("1");
        statsViewport.Attribute("ClipToBounds")?.Value.Should().Be("True");
        statsViewport.Attribute("Margin")?.Value.Should().NotContain("180");

        var statsPanel = statsViewport
            .Descendants(presentation + "StackPanel")
            .Single(panel => panel.Attribute(x + "Name")?.Value == "StatusStatsPanel");

        statsPanel.Attribute("HorizontalAlignment")?.Value.Should().Be("Right");
        statsPanel.Attribute("ClipToBounds")?.Value.Should().Be("True");

        var zoomControls = statusBarGrid
            .Descendants(presentation + "Grid")
            .Single(panel => panel.Attribute(x + "Name")?.Value == "StatusZoomControls");

        zoomControls.Attribute("Grid.Column")?.Value.Should().Be("2");
        zoomControls.Attribute("MinWidth")?.Value.Should().NotBeNullOrWhiteSpace();
        zoomControls.Attribute("Height")?.Value.Should().Be("24");
        // WS-G round 4: converted to DynamicResource so the status bar tracks the active theme.
        zoomControls.Attribute("Background")?.Value.Should().Be("{DynamicResource FreeXStatusSurfaceBrush}");
        zoomControls.Attribute("Panel.ZIndex")?.Value.Should().Be("1");
        zoomControls.Attribute("KeyboardNavigation.TabNavigation")?.Value.Should().Be("Cycle");
        zoomControls.Attribute("KeyboardNavigation.ControlTabNavigation")?.Value.Should().Be("Cycle");
    }

    [Theory]
    [InlineData("StatusZoomOutButton")]
    [InlineData("StatusZoomInButton")]
    public void StatusBarZoomGlyphButtons_AreReadableAtExcelScale(string buttonName)
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var button = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == buttonName);

        button.Attribute("Width")?.Value.Should().Be("22");
        button.Attribute("Height")?.Value.Should().Be("22");
        button.Attribute("FontSize")?.Value.Should().Be("18");
        var strokeDimensions = button
            .Descendants(presentation + "Rectangle")
            .Select(rectangle => (Width: rectangle.Attribute("Width")?.Value, Height: rectangle.Attribute("Height")?.Value))
            .ToArray();
        strokeDimensions.Should().Contain(("12", "2"));
        if (buttonName == "StatusZoomInButton")
        {
            strokeDimensions.Should().Contain(("2", "12"));
        }
    }
}
