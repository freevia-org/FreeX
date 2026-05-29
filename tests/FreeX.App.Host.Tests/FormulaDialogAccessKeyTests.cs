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
            "Content = \"_Top row\"",
            "Content = \"_Left column\"",
            "Content = \"_Bottom row\"",
            "Content = \"_Right column\"",
            "DialogButtonRowFactory.Create"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Header = \"Create names from\"");
        source.Should().Contain("Excel creates named ranges");
    }

    [Fact]
    public void CreateNamesFromSelectionDialog_ExposesNamedOptionsGroupHelpText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("using System.Windows.Automation;");
        source.Should().Contain("AutomationProperties.SetName(group, \"Create names from selected labels\");");
        source.Should().Contain("AutomationProperties.SetHelpText(group, \"Choose which row or column labels Excel uses to create named ranges.\");");
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
            "Use the top row of the selection as names.",
            "Use the left column of the selection as names.",
            "Use the bottom row of the selection as names.",
            "Use the right column of the selection as names."
        })
            source.Should().Contain(expected);
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
            "Content = \"Help on this _Function\"",
            "Content = \"_Evaluate\"",
            "Content = \"Step _In\"",
            "Content = \"Step _Out\"",
            "Content = \"_Restart\"",
            "Content = \"_Close\""
        })
            source.Should().Contain(expected);

        source.Should().NotContain("_Help on this formula");
        source.Should().NotContain("Content = \"_Previous\"");
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
