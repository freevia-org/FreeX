using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ProtectionDialogTests
{
    [Fact]
    public void AllowEditRangeDialog_ExposesExcelLikeRangeManagerActions()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("public sealed record AllowEditRangeSelectionRequest");
        source.Should().Contain("AllowEditRangeResult Result");
        source.Should().Contain("private readonly ListBox _existingRangesBox");
        source.Should().Contain("Header = UiText.Get(\"AllowEditRange_ExistingRangesLabel\")");
        source.Should().NotContain("Header = \"Ranges unlocked by password\"");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_NewButton\")");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_ModifyButton\")");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_DeleteButton\")");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_PermissionsButton\")");
        source.Should().Contain("_permissionsButton.IsEnabled = false");
        source.Should().Contain("private void NewRange_Click");
        source.Should().Contain("private void ModifySelectedRange_Click");
        source.Should().Contain("private void DeleteSelectedRange_Click");
        source.Should().Contain("TryLoadSelectedRangeForModification");
        source.Should().Contain("CreateModifyResult");
        source.Should().Contain("CreateRemoveResult");
        source.Should().Contain("CreateClearResult");
    }

    [Fact]
    public void AllowEditRangeDialog_ExistingRangesListExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AllowEditRangeDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_existingRangesBox, \"AllowEditRangeExistingRangesList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesHelpText\"));");
        UiText.Get("AllowEditRange_ExistingRangesAutomationName").Should().Be("Ranges unlocked by password");
    }

    [Fact]
    public void AllowEditRangeDialog_RangeEditorExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AllowEditRangeDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"AllowEditRange_RangeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rangeBox, \"AllowEditRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(\"AllowEditRange_RangeHelpText\"));");
        UiText.Get("AllowEditRange_RangeAutomationName").Should().Be("Editable range");
    }

    [Fact]
    public void AllowEditRangeDialog_ActionButtonsExposeAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("AllowEditRangeDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_newRangeButton, UiText.Get(\"AllowEditRange_NewAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_newRangeButton, \"AllowEditRangeNewButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_newRangeButton, UiText.Get(\"AllowEditRange_NewHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_modifyRangeButton, UiText.Get(\"AllowEditRange_ModifyAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_modifyRangeButton, \"AllowEditRangeModifyButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_modifyRangeButton, UiText.Get(\"AllowEditRange_ModifyHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_deleteRangeButton, \"AllowEditRangeDeleteButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_permissionsButton, UiText.Get(\"AllowEditRange_PermissionsAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_permissionsButton, \"AllowEditRangePermissionsButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_permissionsButton, UiText.Get(\"AllowEditRange_PermissionsHelpText\"));");
        source.Should().NotContain("rangePicker");
    }

    [Fact]
    public void AllowEditRangesWorkflow_ExecutesAddRemoveAndClearCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("AllowEditRangePlanner.CreateCommandPlan(");
        source.Should().Contain("TryExecuteCommand(plan.Command, \"Allow Users to Edit Ranges\")");
        source.Should().Contain("UiText.Format(\"MainWindowMessage_AllowEditRangeModified\", range)");
        source.Should().NotContain("new AllowEditRangeCommand");
        source.Should().NotContain("new RemoveAllowEditRangeCommand");
        source.Should().NotContain("new ClearAllowEditRangesCommand");
        source.Should().NotContain("new SetAllowEditRangePasswordCommand");
    }

    [Fact]
    public void AllowEditRangesWorkflow_WiresRangePickerToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("request => ApplyAllowEditRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyAllowEditRangeSelection(");
        source.Should().Contain("AllowEditRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End))");
    }
}
