using FluentAssertions;
using System.IO;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class ScenarioManagerDialogTests
{
    private static T GetField<T>(ScenarioManagerDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(ScenarioManagerDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void AssertAutomation(Control control, string name, string automationId, string helpText)
    {
        AutomationProperties.GetName(control).Should().Be(name);
        AutomationProperties.GetAutomationId(control).Should().Be(automationId);
        AutomationProperties.GetHelpText(control).Should().Be(helpText);
    }

    private static string ReadScenarioManagerDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ScenarioManagerDialog.Planning.cs")));
}
