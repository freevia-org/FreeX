using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class ProtectionDialogTests
{
    [Fact]
    public void AllowEditRangeDialog_ExposesExcelLikeRangeManagerActions()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("public enum AllowEditRangeDialogAction");
        source.Should().Contain("public sealed record AllowEditRangeDialogResult");
        source.Should().Contain("private readonly ListBox _existingRangesBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"AllowEditRange_ExistingRangesLabel\"), Target = _existingRangesBox");
        source.Should().NotContain("Header = \"Ranges unlocked by password\"");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_DeleteButton\")");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_ClearAllButton\")");
        source.Should().Contain("private void DeleteSelectedRange_Click");
        source.Should().Contain("private void ClearAllRanges_Click");
        source.Should().Contain("CreateRemoveResult");
        source.Should().Contain("CreateClearResult");
    }

    [Fact]
    public void AllowEditRangeDialog_ExistingRangesListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_existingRangesBox, \"AllowEditRangeExistingRangesList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesHelpText\"));");
        UiText.Get("AllowEditRange_ExistingRangesAutomationName").Should().Be("Ranges unlocked by password");
    }

    [Fact]
    public void AllowEditRangeDialog_RangeEditorExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"AllowEditRange_RangeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rangeBox, \"AllowEditRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(\"AllowEditRange_RangeHelpText\"));");
        UiText.Get("AllowEditRange_RangeAutomationName").Should().Be("Editable range");
    }

    [Fact]
    public void AllowEditRangeDialog_ActionButtonsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_deleteRangeButton, \"AllowEditRangeDeleteButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_clearRangesButton, UiText.Get(\"AllowEditRange_ClearAllAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_clearRangesButton, \"AllowEditRangeClearAllButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_clearRangesButton, UiText.Get(\"AllowEditRange_ClearAllHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(rangePicker, UiText.Get(\"AllowEditRange_PickerAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(rangePicker, \"AllowEditRangePickerButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(");
        source.Should().Contain("UiText.Get(\"AllowEditRange_PickerHelpText\"));");
    }

    [Fact]
    public void AllowEditRangesWorkflow_ExecutesAddRemoveAndClearCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("AllowEditRangeDialogAction.Add");
        source.Should().Contain("new AllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Remove");
        source.Should().Contain("new RemoveAllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Clear");
        source.Should().Contain("new ClearAllowEditRangesCommand(_currentSheetId)");
    }

    [Fact]
    public void AllowEditRangesWorkflow_WiresRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("request => ApplyAllowEditRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyAllowEditRangeSelection(");
        source.Should().Contain("AllowEditRangeSelectionRequest request");
        source.Should().Contain("dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End));");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }
}
