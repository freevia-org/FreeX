using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaAuditErrorCheckingDialogSourceTests
{
    [Fact]
    public void IgnoreSelected_RemovesAllIssuesForIgnoredCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("var sameCellIssues = _issues");
        source.Should().Contain(".Where(candidate =>");
        source.Should().Contain("candidate.SheetId == issue.SheetId");
        source.Should().Contain("candidate.Address.Equals(issue.Address)");
        source.Should().Contain("foreach (var sameCellIssue in sameCellIssues)");
        source.Should().Contain("_issues.Remove(sameCellIssue)");
    }

    [Fact]
    public void ErrorCheckingIssueList_EnterKeyNavigatesSelectedIssue()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("_listView.KeyDown += ListView_KeyDown;");
        source.Should().Contain("private void ListView_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key == Key.Enter)");
        source.Should().Contain("NavigateSelected();");
        source.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void EvaluateFormulaDialog_ExposesExcelLikeStepRestartAndHelpControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "EvaluateFormulaDialog.cs"));

        source.Should().Contain("UiText.Get(\"EvaluateFormula_EvaluationLabel\")");
        source.Should().Contain("UiText.Get(\"EvaluateFormula_StepInButton\")");
        source.Should().Contain("UiText.Get(\"EvaluateFormula_StepOutButton\")");
        source.Should().Contain("_session.StepOut()");
        source.Should().Contain("UiText.Get(\"EvaluateFormula_RestartButton\")");
        source.Should().Contain("UiText.Get(\"EvaluateFormula_HelpButton\")");
        source.Should().NotContain("_Help on this formula");
        source.Should().Contain("ShowFormulaHelp");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
        UiText.Get("EvaluateFormula_HelpButton").Should().Be("Help on this _Function");
    }

    [Fact]
    public void EvaluateFormulaDialog_UsesEvaluateAsDefaultAndCloseAsCancel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "EvaluateFormulaDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"EvaluateFormula_EvaluateButton\"), Width = 80, Height = 26, IsDefault = true");
        source.Should().Contain("Content = UiText.Get(\"EvaluateFormula_CloseButton\"), Width = 80, Height = 26, IsCancel = true");
        UiText.Get("EvaluateFormula_EvaluateButton").Should().Be("_Evaluate");
    }

    [Fact]
    public void EvaluateFormulaDialog_DisablesStepInWithEvaluateAndFocusesEnabledCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "EvaluateFormulaDialog.cs"));

        source.Should().Contain("private readonly Button _stepInButton;");
        source.Should().Contain("_stepInButton = new Button { Content = UiText.Get(\"EvaluateFormula_StepInButton\")");
        source.Should().Contain("_session.StepIn()");
        source.Should().NotContain("_stepInButton.Click += (_, _) =>\r\n        {\r\n            _session.MoveNext();");
        source.Should().Contain("_stepInButton.IsEnabled = _session.CanStepIn;");
        source.Should().Contain("FocusFirstEnabledCommand();");
        source.Should().Contain("private void FocusFirstEnabledCommand()");
        source.Should().Contain("_nextButton.IsEnabled ? _nextButton");
        source.Should().Contain("Keyboard.Focus(target);");
    }
}
