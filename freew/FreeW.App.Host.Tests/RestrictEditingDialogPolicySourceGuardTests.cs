using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RestrictEditingDialogPolicySourceGuardTests
{
    [Fact]
    public void RestrictEditingDialog_DelegatesProtectionPolicyToPresentationPlanner()
    {
        var source = ReadHostSource("RestrictEditingDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("RestrictEditingDialogPlanner.BuildPlan(current)");
        source.Should().Contain("RestrictEditingDialogPlanner.ModeOptions");
        source.Should().Contain("RestrictEditingDialogPlanner.TryCreateStartSettings(");
        source.Should().Contain("RestrictEditingDialogPlanner.TryCreateStopSettings(");
        source.Should().Contain("RestrictEditingDialogPlanner.StartButtonText");
        source.Should().Contain("RestrictEditingDialogPlanner.StopButtonText");
        source.Should().NotContain("ProtectionPasswordHelper.CreateWithPassword");
        source.Should().NotContain("ProtectionPasswordHelper.VerifyPassword");
    }

    [Fact]
    public void MainWindow_ProjectsProtectGroupToggleStateThroughSharedPlanner()
    {
        var source = ReadHostSource("MainWindow.cs");

        source.Should().Contain("ReviewProtectionStatePlanner.Build(_editor.Model.Protection, _editor.IsMarkedAsFinal)");
        source.Should().Contain("foreach (var commandState in protectionStatePlan.Commands)");
        source.Should().Contain("_stateStore.SetChecked(commandState.CommandId, commandState.IsChecked)");
        source.Should().NotContain("_stateStore.SetChecked(\"freew.mark-as-final\", _editor.IsMarkedAsFinal)");
        source.Should().NotContain("_stateStore.SetChecked(\"freew.restrict-editing\", _editor.IsProtected)");
    }

    private static string ReadHostSource(string fileName) =>
        TestWorkspaceFileLocator.ReadAllTextFromBaseDirectory(
            "freew", "FreeW.App.Host", fileName);
}
