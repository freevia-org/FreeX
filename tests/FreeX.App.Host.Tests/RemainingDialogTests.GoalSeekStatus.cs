using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void GoalSeekStatusDialog_CreateMessage_DescribesSolvedAndUnsolvedResults()
    {
        GoalSeekStatusDialog.CreateMessage(new(true, 42.25, 100, 4), targetValue: 100)
            .Should()
            .Contain("Goal Seek found a solution")
            .And.Contain("Target value: 100")
            .And.Contain("Current value: 100")
            .And.Contain("Changing cell value: 42.25");

        GoalSeekStatusDialog.CreateMessage(new(false, 11, 98.5, 32), targetValue: 100)
            .Should()
            .Contain("could not find a solution")
            .And.Contain("Target value: 100")
            .And.Contain("Current value: 98.5")
            .And.Contain("Changing cell value: 11");
    }

    [Fact]
    public void GoalSeekStatusDialog_ExposesKeyboardAccessKeysForButtons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekStatusDialog.cs");

        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_KeepResult\")");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_RestoreOriginalValues\")");
        source.Should().Contain("DialogButtonRowFactory.Create(keepButton, restoreButton)");
        // R43-commands-goalseek-datatable-3-2: the non-converged branch now offers Excel's real
        // OK/Cancel pair (OK keeps the closest approximate value, Cancel restores the original)
        // instead of a single OK-only button that could never accept the result.
        source.Should().Contain("DialogButtonRowFactory.Create(() =>");
        source.Should().NotContain("DialogButtonRowFactory.CreateOkOnly");
    }

    [Fact]
    public void GoalSeekStatusDialog_ExposesAutomationMetadataForStatusAndActions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekStatusDialog.cs");

        source.Should().Contain("AutomationProperties.SetAutomationId(statusBlock, \"GoalSeekStatusSummary\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepButton, \"GoalSeekKeepResultButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(restoreButton, \"GoalSeekRestoreOriginalValuesButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"GoalSeekStatusOkButton\");");
        source.Should().Contain("UiText.Get(\"GoalSeekStatus_ReportsWhetherGoalSeekReachedTheTargetValue\")");
    }

    [Fact]
    public void GoalSeekStatusDialogOpenedFromKeyboard_FocusesDefaultButton()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekStatusDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
    }

    [Fact]
    public void GoalSeekStatusDialog_ReceivesRequestedTargetValueFromWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new GoalSeekStatusDialog(result, targetValue)");
    }

    [Fact]
    public void StatusDialogs_ExposeClearExcelLikeStatusLabelsAndButtons()
    {
        var source = ReadStatusDialogSources();

        source.Should().Contain("GoalSeekStatusDialogPlanner.DescribeStatus(");
        source.Should().NotContain("GoalSeekPresentationProfile");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_KeepResult\")");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_RestoreOriginalValues\")");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }
}
