using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataValidationDialogTests
{
    [Fact]
    public void DataValidationDialog_ContainsRangePickerButtonsForBothFormulaFields()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        xaml.Should().Contain("x:Name=\"UseSelectionButton\"");
        xaml.Should().Contain("x:Name=\"UseSelection2Button\"");
        xaml.Should().Contain("x:Name=\"SourcePickerButton\"");
        xaml.Should().Contain("x:Name=\"SourcePicker2Button\"");
        xaml.Should().Contain("Click=\"UseSelectionButton_Click\"");
        xaml.Should().Contain("Click=\"UseSelection2Button_Click\"");
        xaml.Should().Contain("Click=\"SourcePickerButton_Click\"");
        xaml.Should().Contain("Click=\"SourcePicker2Button_Click\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select source range\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select maximum range\"");
        xaml.Should().Contain("Collapse dialog and select source range");
        xaml.Should().Contain("Collapse dialog and select maximum range");
    }

    [Fact]
    public void DataValidationDialog_UsesExcelStyleSettingsInputAndErrorTabs()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        xaml.Should().Contain("<TabControl");
        xaml.ShouldContainLocalizedAttribute("Header", "_Settings");
        xaml.ShouldContainLocalizedAttribute("Header", "_Input Message");
        xaml.ShouldContainLocalizedAttribute("Header", "_Error Alert");
    }

    [Fact]
    public void DataValidationDialog_OrdersAllowTypesLikeExcel()
    {
        var choices = DataValidationDialogPlanner.CreateTypeChoices(UiText.Get);
        var expectedOrder = new[]
        {
            DvType.Any,
            DvType.WholeNumber,
            DvType.Decimal,
            DvType.List,
            DvType.Date,
            DvType.Time,
            DvType.TextLength,
            DvType.Custom
        };

        choices.Select(choice => choice.Type).Should().Equal(expectedOrder);
        choices.Select(choice => choice.Label).Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label));
    }

    [Fact]
    public void DataValidationDialog_ExposesKeyboardAccessKeysForOptionsAndButtons()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        foreach (var content in new[]
        {
            "_Allow:",
            "_Data:",
            "_Minimum:",
            "Ma_ximum:",
            "Input _title:",
            "Input _message:",
            "_Alert style:",
            "Error _title:",
            "Error _message:",
            "_Use Selection",
            "Use _Selection",
            "_In-cell dropdown",
            "Ignore _blank",
            "Apply these changes to all other cells _with the same settings",
            "Show _input message when cell is selected",
            "Show error _alert after invalid data is entered",
            "C_lear All",
            "_OK",
            "_Cancel"
        })
            xaml.ShouldContainLocalizedAttribute("Content", content);
    }

    [Fact]
    public void DataValidationDialog_SettingsTabAccessKeysAvoidVisibleEnglishCollisions()
    {
        AssertUniqueAccessKeys(
            "DataValidation_Allow",
            "DataValidation_IgnoreBlank",
            "DataValidation_ApplyTheseChangesToAllOtherCellsWithTheSameSettings",
            "DataValidation_ClearAll",
            "Common_Ok",
            "Common_Cancel");

        AssertUniqueAccessKeys(
            "DataValidation_Allow",
            "DataValidation_Source",
            "DataValidation_UseSelection",
            "DataValidation_InCellDropdown",
            "DataValidation_IgnoreBlank",
            "DataValidation_ApplyTheseChangesToAllOtherCellsWithTheSameSettings",
            "DataValidation_ClearAll",
            "Common_Ok",
            "Common_Cancel");

        AssertUniqueAccessKeys(
            "DataValidation_Allow",
            "DataValidation_Data",
            "DataValidation_Minimum",
            "DataValidation_Maximum",
            "DataValidation_UseSelection",
            "DataValidation_UseSelection2",
            "DataValidation_IgnoreBlank",
            "DataValidation_ApplyTheseChangesToAllOtherCellsWithTheSameSettings",
            "DataValidation_ClearAll",
            "Common_Ok",
            "Common_Cancel");

        AssertUniqueAccessKeys(
            "DataValidation_Allow",
            "DataValidation_Data",
            "DataValidation_Value",
            "DataValidation_UseSelection",
            "DataValidation_IgnoreBlank",
            "DataValidation_ApplyTheseChangesToAllOtherCellsWithTheSameSettings",
            "DataValidation_ClearAll",
            "Common_Ok",
            "Common_Cancel");

        AssertUniqueAccessKeys(
            "DataValidation_Allow",
            "DataValidation_Formula",
            "DataValidation_UseSelection",
            "DataValidation_IgnoreBlank",
            "DataValidation_ApplyTheseChangesToAllOtherCellsWithTheSameSettings",
            "DataValidation_ClearAll",
            "Common_Ok",
            "Common_Cancel");
    }

    [Fact]
    public void DataValidationDialog_ExposesStableAutomationIdsForFocusableFieldsAndActions()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        foreach (var automationId in new[]
        {
            "DataValidationAllowTypeCombo",
            "DataValidationOperatorCombo",
            "DataValidationFormula1Box",
            "DataValidationFormula2Box",
            "DataValidationSourcePickerButton",
            "DataValidationUseSelectionButton",
            "DataValidationSourcePicker2Button",
            "DataValidationUseSelection2Button",
            "DataValidationInCellDropdownCheckBox",
            "DataValidationIgnoreBlankCheckBox",
            "DataValidationSameSettingsCheckBox",
            "DataValidationShowInputMessageCheckBox",
            "DataValidationPromptTitleBox",
            "DataValidationPromptMessageBox",
            "DataValidationShowErrorMessageCheckBox",
            "DataValidationAlertStyleCombo",
            "DataValidationErrorTitleBox",
            "DataValidationErrorMessageBox",
            "DataValidationClearAllButton",
            "DataValidationOkButton",
            "DataValidationCancelButton"
        })
            xaml.Should().Contain($"AutomationProperties.AutomationId=\"{automationId}\"");

        xaml.Should().Contain("IsDefault=\"True\"");
        xaml.Should().Contain("IsCancel=\"True\"");
    }

    [Fact]
    public void DataValidationDialogOpenedFromKeyboard_FocusesAllowTypeSelector()
    {
        var codeBehind = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.xaml.cs");

        codeBehind.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        codeBehind.Should().Contain("private void FocusInitialKeyboardTarget()");
        codeBehind.Should().Contain("TypeCombo.Focus();");
        codeBehind.Should().Contain("Keyboard.Focus(TypeCombo);");
    }

    [Fact]
    public void DataValidationDialogInvalidCriteria_ReturnsToSettingsTabAndKeyboardFocusesInvalidFormula()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");
        var codeBehind = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.xaml.cs");

        xaml.Should().Contain("<TabControl x:Name=\"ValidationTabs\"");
        xaml.Should().Contain("<TabItem x:Name=\"SettingsTab\" Header=\"_Settings\"");
        codeBehind.Should().Contain("DialogFocus.ShowWarningAndFocus(this, criteriaError, Title, ResolveInvalidCriteriaInput(type, op));");
        codeBehind.Should().Contain("private TextBox ResolveInvalidCriteriaInput(DvType type, DvOperator op)");
        codeBehind.Should().Contain("ValidationTabs.SelectedItem = SettingsTab;");
    }

    [Fact]
    public void DataValidationDialog_UsesExcelLikeSectionLabelsAndListSourcePicker()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        xaml.Should().Contain("Validation criteria");
        xaml.Should().Contain("When selecting cell, show this input message");
        xaml.Should().Contain("When user enters invalid data, show this error alert");
        xaml.Should().Contain("x:Name=\"SourcePickerButton\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select source range\"");
        xaml.Should().Contain("Click=\"SourcePickerButton_Click\"");
    }

    [Fact]
    public void DataValidationDialog_EditableCaptionsAreAccessKeyLabelsWithTargets()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");

        foreach (var expected in new[]
        {
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"_Allow:\" Target=\"{Binding ElementName=TypeCombo}\"",
            "<Label x:Name=\"OperatorLabel\" Grid.Row=\"2\" Grid.Column=\"0\" Content=\"_Data:\" Target=\"{Binding ElementName=OperatorCombo}\"",
            "<Label x:Name=\"Formula1Label\" Grid.Row=\"3\" Grid.Column=\"0\" Content=\"_Minimum:\" Target=\"{Binding ElementName=Formula1Box}\"",
            "<Label x:Name=\"Formula2Label\" Grid.Row=\"4\" Grid.Column=\"0\" Content=\"Ma_ximum:\" Target=\"{Binding ElementName=Formula2Box}\"",
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"Input _title:\" Target=\"{Binding ElementName=PromptTitleBox}\"",
            "<Label Grid.Row=\"2\" Grid.Column=\"0\" Content=\"Input _message:\" Target=\"{Binding ElementName=PromptMessageBox}\"",
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"_Alert style:\" Target=\"{Binding ElementName=AlertStyleCombo}\"",
            "<Label Grid.Row=\"2\" Grid.Column=\"0\" Content=\"Error _title:\" Target=\"{Binding ElementName=ErrorTitleBox}\"",
            "<Label Grid.Row=\"3\" Grid.Column=\"0\" Content=\"Error _message:\" Target=\"{Binding ElementName=ErrorMessageBox}\""
        })
            xaml.Should().Contain(expected);

        xaml.Should().NotContain("Text=\"Allow:\"");
        xaml.Should().NotContain("Text=\"Data:\"");
        xaml.Should().NotContain("Text=\"Minimum:\"");
        xaml.Should().NotContain("Text=\"Maximum:\"");
    }

    [Fact]
    public void DataValidationDialog_UpdatesDynamicCaptionContent()
    {
        var codeBehind = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.xaml.cs");
        var planner = DialogSourceTestSupport.ReadPresentationSources("Dialogs", "DataValidationDialogPlanner.cs");

        codeBehind.Should().Contain("DataValidationDialogPlanner.CreateVisibilityPlan(");
        codeBehind.Should().Contain("DataValidationDialogPlanner.GetFormula1FieldDescriptor(plan.Formula1Label)");
        codeBehind.Should().Contain("Formula1Label.Content = UiText.Get(formula1Descriptor.LabelResourceKey);");
        planner.Should().Contain("DvFormula1Label.Source => new(");
        planner.Should().Contain("\"DataValidation_Source\"");
        planner.Should().Contain("DvFormula1Label.Formula => new(");
        planner.Should().Contain("\"DataValidation_Formula\"");
        planner.Should().Contain("DvFormula1Label.Value => new(");
        planner.Should().Contain("\"DataValidation_Value\"");
        codeBehind.Should().NotContain("Formula1Label.Text =");
    }

    [Fact]
    public void DataValidationDialogPlanning_DelegatesPortableLogicToPresentation()
    {
        var planningSource = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.Planning.cs");
        var codeBehind = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.xaml.cs");

        planningSource.Should().Contain("DataValidationDialogPlanner.ValidateCriteria(");
        planningSource.Should().Contain("DataValidationDialogPlanner.FocusTargetForInvalidCriteria(");
        planningSource.Should().Contain("DataValidationDialogPlanner.CreateRangeSelectionRequest(");
        planningSource.Should().NotContain("new Parser");
        planningSource.Should().NotContain("TryParseInlineListCriteria");
        codeBehind.Should().Contain("DataValidationDialogPlanner.CreateRule(input)");
        codeBehind.Should().Contain("DataValidationDialogPlanner.IsClearAllState(input)");
    }

    [Fact]
    public void DataValidationViolationMessages_UseOwnedMainWindowMessageHelper()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain("_session.DataValidationPromptResolver = ResolveDataValidationPrompt;");
        source.Should().Contain("private UserMessageResult ResolveDataValidationPrompt(");
        source.Should().Contain("_messageService.ShowMessage(");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void MainWindow_AppliesDataValidationToMatchingSettingsWhenRequested()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("new DataValidationDialog(existingRule, request => ApplyDataValidationRangeSelection(dlg, request))");
        source.Should().Contain("dlg.ApplyToSameSettings");
        source.Should().Contain("candidate.HasSameSettings(existingRule)");
        source.Should().Contain("CompositeWorkbookCommand(\"Data Validation\", commands)");
    }

    [Fact]
    public void MainWindow_WiresDataValidationRangePickerToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("private void ApplyDataValidationRangeSelection(");
        source.Should().Contain("DataValidationRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("DataValidationService.FormatListSourceRange(");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, formulaText);");
    }
}
