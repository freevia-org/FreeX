using FluentAssertions;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    private static T GetField<T>(ScenarioManagerDialog dialog, string fieldName)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, fieldName);

    private static void AssertAutomation(Control control, string name, string automationId, string helpText)
    {
        AutomationProperties.GetName(control).Should().Be(name);
        AutomationProperties.GetAutomationId(control).Should().Be(automationId);
        AutomationProperties.GetHelpText(control).Should().Be(helpText);
    }

    private static string ReadScenarioManagerDialogSources() =>
        DialogSourceTestSupport.ReadHostSources("ScenarioManagerDialog.cs");

    private static string ReadScenarioManagerDialogSource() =>
        DialogSourceTestSupport.ReadHostSources("ScenarioManagerDialog.cs");

    private static string ReadMainWindowScenarioCommandsSource() =>
        DialogSourceTestSupport.ReadHostSources("MainWindow.ScenarioCommands.cs");
}
