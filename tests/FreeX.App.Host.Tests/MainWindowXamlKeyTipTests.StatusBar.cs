using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void StatusBarZoomCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "ZoomOutBtn_Click" or "ZoomInBtn_Click")
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button => LocalizedAttribute(button, "Content") ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("status-bar zoom commands should participate in the visible command keytip contract");
    }

    [Fact]
    public void StatusBarZoomSlider_HasAccessibleRangeMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
    }

    [Fact]
    public void StatusBarAggregates_AreConstrainedAwayFromZoomControls()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        zoomControls.Attribute("Background")?.Value.Should().Be("{StaticResource FreeXStatusSurfaceBrush}");
        zoomControls.Attribute("Panel.ZIndex")?.Value.Should().Be("1");
        zoomControls.Attribute("KeyboardNavigation.TabNavigation")?.Value.Should().Be("Cycle");
        zoomControls.Attribute("KeyboardNavigation.ControlTabNavigation")?.Value.Should().Be("Cycle");
    }

    [Theory]
    [InlineData("StatusZoomOutButton")]
    [InlineData("StatusZoomInButton")]
    public void StatusBarZoomGlyphButtons_AreReadableAtExcelScale(string buttonName)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
