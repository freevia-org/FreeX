using FluentAssertions;
using System.IO;

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoalSeekStatusDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_KeepResult\")");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_RestoreOriginalValues\")");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("IsCancel = true");
    }

    [Fact]
    public void GoalSeekStatusDialog_ExposesAutomationMetadataForStatusAndActions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoalSeekStatusDialog.cs"));

        source.Should().Contain("AutomationProperties.SetAutomationId(statusBlock, \"GoalSeekStatusSummary\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepButton, \"GoalSeekKeepResultButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(restoreButton, \"GoalSeekRestoreOriginalValuesButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"GoalSeekStatusOkButton\");");
        source.Should().Contain("UiText.Get(\"GoalSeekStatus_ReportsWhetherGoalSeekReachedTheTargetValue\")");
    }

    [Fact]
    public void GoalSeekStatusDialogOpenedFromKeyboard_FocusesDefaultButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoalSeekStatusDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
    }

    [Fact]
    public void GoalSeekStatusDialog_ReceivesRequestedTargetValueFromWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new GoalSeekStatusDialog(result, targetValue)");
    }

    [Fact]
    public void StatusDialogs_ExposeClearExcelLikeStatusLabelsAndButtons()
    {
        var source = ReadStatusDialogSources();

        source.Should().Contain("UiText.Format(\"GoalSeekStatus_SuccessSummary\"");
        source.Should().Contain("UiText.Format(\"GoalSeekStatus_FailureSummary\"");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_KeepResult\")");
        source.Should().Contain("Content = UiText.Get(\"GoalSeekStatus_RestoreOriginalValues\")");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }
}
