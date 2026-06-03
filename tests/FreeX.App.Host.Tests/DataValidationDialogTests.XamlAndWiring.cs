using System.IO;
using FluentAssertions;

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
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");
        var expectedOrder = new[]
        {
            "Content=\"Any Value\"",
            "Content=\"Whole Number\"",
            "Content=\"Decimal\"",
            "Content=\"List\"",
            "Content=\"Date\"",
            "Content=\"Time\"",
            "Content=\"Text Length\"",
            "Content=\"Custom\""
        };

        var positions = expectedOrder
            .Select(marker => xaml.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
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
            "_Ignore blank",
            "Apply these changes to all other cells with the _same settings",
            "Show _input message when cell is selected",
            "Show error _alert after invalid data is entered",
            "C_lear All",
            "_OK",
            "_Cancel"
        })
            xaml.ShouldContainLocalizedAttribute("Content", content);
    }

    [Fact]
    public void DataValidationDialogOpenedFromKeyboard_FocusesAllowTypeSelector()
    {
        var codeBehind = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataValidationDialog.xaml.cs"));

        codeBehind.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        codeBehind.Should().Contain("private void FocusInitialKeyboardTarget()");
        codeBehind.Should().Contain("TypeCombo.Focus();");
        codeBehind.Should().Contain("Keyboard.Focus(TypeCombo);");
    }

    [Fact]
    public void DataValidationDialogInvalidCriteria_ReturnsToSettingsTabAndKeyboardFocusesInvalidFormula()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("DataValidationDialog.xaml");
        var codeBehind = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataValidationDialog.xaml.cs"));

        xaml.Should().Contain("<TabControl x:Name=\"ValidationTabs\"");
        xaml.Should().Contain("<TabItem x:Name=\"SettingsTab\" Header=\"_Settings\"");
        codeBehind.Should().Contain("FocusInvalidCriteriaInput(typeTag, opTag);");
        codeBehind.Should().Contain("private void FocusInvalidCriteriaInput(string typeTag, string opTag)");
        codeBehind.Should().Contain("ValidationTabs.SelectedItem = SettingsTab;");
        codeBehind.Should().Contain("Keyboard.Focus(target);");
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
        var codeBehind = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataValidationDialog.xaml.cs"));

        codeBehind.Should().Contain("Formula1Label.Content = UiText.Get(\"DataValidation_Source\")");
        codeBehind.Should().Contain("Formula1Label.Content = UiText.Get(\"DataValidation_Formula\")");
        codeBehind.Should().Contain("UiText.Get(\"DataValidation_Minimum\")");
        codeBehind.Should().Contain("UiText.Get(\"DataValidation_Value\")");
        codeBehind.Should().NotContain("Formula1Label.Text =");
    }

    [Fact]
    public void DataValidationViolationMessages_UseOwnedMainWindowMessageHelper()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        var method = source[
            source.IndexOf("private bool TryCreateCellFromEntryText(", StringComparison.Ordinal)..
            source.IndexOf("private bool CommitPreparedEdits(", StringComparison.Ordinal)];

        method.Should().Contain("ShowOwnedMessage(violationMsg");
        method.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void MainWindow_AppliesDataValidationToMatchingSettingsWhenRequested()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("new DataValidationDialog(existingRule, request => ApplyDataValidationRangeSelection(dlg, request))");
        source.Should().Contain("dlg.ApplyToSameSettings");
        source.Should().Contain("HasSameDataValidationSettings");
        source.Should().Contain("CompositeWorkbookCommand(\"Data Validation\", commands)");
    }

    [Fact]
    public void MainWindow_WiresDataValidationRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("private void ApplyDataValidationRangeSelection(");
        source.Should().Contain("DataValidationRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("DataValidationService.FormatListSourceRange(");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, formulaText);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }
}
