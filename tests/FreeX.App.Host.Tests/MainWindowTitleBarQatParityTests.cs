using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowTitleBarQatParityTests
{
    [Fact]
    public void TitleBarSystemButtons_ExposeStableAutomationMetadataAndDynamicMaxRestoreHelp()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute(x + "Name")?.Value is "MinimizeBtn" or "MaxRestoreBtn" or "CloseSysBtn")
            .Select(button => new
            {
                Name = button.Attribute(x + "Name")!.Value,
                Click = button.Attribute("Click")?.Value,
                AutomationName = button.Attribute("AutomationProperties.Name")?.Value,
                AutomationHelpText = button.Attribute("AutomationProperties.HelpText")?.Value,
                AutomationId = button.Attribute("AutomationProperties.AutomationId")?.Value,
                IconKind = button.Element(local + "RibbonIcon")?.Attribute("Kind")?.Value
            })
            .ToDictionary(button => button.Name, StringComparer.Ordinal);

        buttons.Should().ContainKeys("MinimizeBtn", "MaxRestoreBtn", "CloseSysBtn");
        buttons["MinimizeBtn"].Should().BeEquivalentTo(new
        {
            Name = "MinimizeBtn",
            Click = "MinimizeBtn_Click",
            AutomationName = UiText.Get("MainWindow_AutomationName_Minimize"),
            AutomationHelpText = UiText.Get("MainWindow_AutomationName_Minimize"),
            AutomationId = "MinimizeBtn",
            IconKind = "WindowMinimize"
        });
        buttons["MaxRestoreBtn"].Should().BeEquivalentTo(new
        {
            Name = "MaxRestoreBtn",
            Click = "MaxRestoreBtn_Click",
            AutomationName = UiText.Get("MainWindow_AutomationName_MaximizeOrRestore"),
            AutomationHelpText = UiText.Get("MainWindow_AutomationName_MaximizeOrRestore"),
            AutomationId = "MaxRestoreBtn",
            IconKind = "WindowMaximize"
        });
        buttons["CloseSysBtn"].Should().BeEquivalentTo(new
        {
            Name = "CloseSysBtn",
            Click = "CloseSysBtn_Click",
            AutomationName = UiText.Get("MainWindow_AutomationName_Close"),
            AutomationHelpText = UiText.Get("MainWindow_AutomationName_Close"),
            AutomationId = "CloseSysBtn",
            IconKind = "WindowClose"
        });

        source.Should().Contain("UpdateMaxRestoreButtonState()");
        source.Should().Contain("MainWindow_AutomationName_RestoreDown");
        source.Should().Contain("MainWindow_AutomationName_Maximize");
        source.Should().Contain("System.Windows.Automation.AutomationProperties.SetName(");
        source.Should().Contain("System.Windows.Automation.AutomationProperties.SetHelpText(");
    }
}
