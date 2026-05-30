using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaDialogAccessKeyTests
{
    [Fact]
    public void CreateNamesFromSelectionDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = UiText.Get(\"CreateNamesFromSelection_TopRow\")",
            "Content = UiText.Get(\"CreateNamesFromSelection_LeftColumn\")",
            "Content = UiText.Get(\"CreateNamesFromSelection_BottomRow\")",
            "Content = UiText.Get(\"CreateNamesFromSelection_RightColumn\")",
            "DialogButtonRowFactory.Create"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Header = UiText.Get(\"CreateNamesFromSelection_GroupHeader\")");
        source.Should().Contain("UiText.Get(\"CreateNamesFromSelection_BodyText\")");
        UiText.Get("CreateNamesFromSelection_TopRow").Should().Be("_Top row");
        UiText.Get("CreateNamesFromSelection_GroupHeader").Should().Be("Create names from");
    }

    [Fact]
    public void CreateNamesFromSelectionDialog_ExposesNamedOptionsGroupHelpText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("using System.Windows.Automation;");
        source.Should().Contain("AutomationProperties.SetName(group, UiText.Get(\"CreateNamesFromSelection_GroupAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(group, UiText.Get(\"CreateNamesFromSelection_GroupHelpText\"));");
        UiText.Get("CreateNamesFromSelection_GroupAutomationName").Should().Be("Create names from selected labels");
    }

    [Fact]
    public void CreateNamesFromSelectionDialog_OptionCheckboxesExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("SetOptionAutomationMetadata(");
        source.Should().Contain("AutomationProperties.SetAutomationId(checkBox, automationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(checkBox, helpText);");
        foreach (var expected in new[]
        {
            "CreateNamesTopRowCheckBox",
            "CreateNamesLeftColumnCheckBox",
            "CreateNamesBottomRowCheckBox",
            "CreateNamesRightColumnCheckBox",
            "UiText.Get(\"CreateNamesFromSelection_TopRowHelpText\")",
            "UiText.Get(\"CreateNamesFromSelection_LeftColumnHelpText\")",
            "UiText.Get(\"CreateNamesFromSelection_BottomRowHelpText\")",
            "UiText.Get(\"CreateNamesFromSelection_RightColumnHelpText\")"
        })
            source.Should().Contain(expected);

        UiText.Get("CreateNamesFromSelection_TopRowHelpText").Should().Be("Use the top row of the selection as names.");
    }

    [Fact]
    public void CreateNamesFromSelectionDialogOpenedFromKeyboard_FocusesTopRowChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_topRow.Focus();");
        source.Should().Contain("Keyboard.Focus(_topRow);");
    }

    [Fact]
    public void CreateNamesFromSelectionDialog_TryCreateResult_RejectsEmptyLabelSelection()
    {
        CreateNamesFromSelectionDialog.TryCreateResult(
                useTopRow: false,
                useLeftColumn: false,
                useBottomRow: false,
                useRightColumn: false,
                out _,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Select at least one row or column label position.");
    }

    [Fact]
    public void CreateNamesFromSelectionDialog_TryCreateResult_CapturesSelectedLabelPositions()
    {
        CreateNamesFromSelectionDialog.TryCreateResult(
                useTopRow: false,
                useLeftColumn: true,
                useBottomRow: false,
                useRightColumn: true,
                out var result,
                out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new CreateNamesFromSelectionDialogResult(
            UseTopRow: false,
            UseLeftColumn: true,
            UseBottomRow: false,
            UseRightColumn: true));
    }

    [Fact]
    public void CreateNamesFromSelectionDialogInvalidSelection_WarnsAndRefocusesTopRowChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("DialogButtonRowFactory.Create(Accept");
        source.Should().Contain("if (!TryCreateResult(");
        source.Should().Contain("MessageBox.Show(");
        source.Should().Contain("this,");
        source.Should().Contain("MessageBoxImage.Warning");
        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("DialogResult = true;");
    }

    [Fact]
    public void EvaluateFormulaDialog_ExposesKeyboardAccessKeysForActions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "EvaluateFormulaDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = UiText.Get(\"EvaluateFormula_HelpButton\")",
            "Content = UiText.Get(\"EvaluateFormula_EvaluateButton\")",
            "Content = UiText.Get(\"EvaluateFormula_StepInButton\")",
            "Content = UiText.Get(\"EvaluateFormula_StepOutButton\")",
            "Content = UiText.Get(\"EvaluateFormula_RestartButton\")",
            "Content = UiText.Get(\"EvaluateFormula_CloseButton\")"
        })
            source.Should().Contain(expected);

        source.Should().NotContain("_Help on this formula");
        source.Should().NotContain("Content = \"_Previous\"");
        UiText.Get("EvaluateFormula_StepInButton").Should().Be("Step _In");
    }

    [Fact]
    public void EvaluateFormulaDialogOpenedFromKeyboard_FocusesFirstEnabledCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "EvaluateFormulaDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusFirstEnabledCommand();");
        source.Should().Contain("var target = _nextButton.IsEnabled ? _nextButton : _closeButton;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("Keyboard.Focus(target);");
    }
}
