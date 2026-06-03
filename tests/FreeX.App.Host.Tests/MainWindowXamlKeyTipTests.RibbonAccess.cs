using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void RibbonSurface_IsReachableByKeyboardTabTraversal()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace keyboardNavigation = "clr-namespace:System.Windows.Input;assembly=PresentationFramework";

        var ribbonTabs = document
            .Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(x + "Name")?.Value == "RibbonTabs");

        ribbonTabs.Attribute("Focusable")?.Value.Should().Be("True");
        ribbonTabs.Attribute("IsTabStop")?.Value.Should().Be("True");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.TabNavigation")?.Value.Should().Be("Continue");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.ControlTabNavigation")?.Value.Should().Be("Continue");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.DirectionalNavigation")?.Value.Should().Be("Contained");
    }

    [Fact]
    public void RibbonCommandStyles_PreserveKeyboardFocusStops()
    {
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styles = resources
            .Descendants(presentation + "Style")
            .Where(style =>
                (style.Attribute(x + "Key")?.Value is "RibbonBtn" or "RibbonToggleBtn") ||
                style.Attribute("TargetType")?.Value == "TabItem")
            .ToList();

        styles.Should().HaveCount(3);
        styles.Should().OnlyContain(style =>
            style.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Focusable" &&
                (string?)setter.Attribute("Value") == "True"));
        styles.Should().OnlyContain(style =>
            style.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "IsTabStop" &&
                (string?)setter.Attribute("Value") == "True"));
    }
}
