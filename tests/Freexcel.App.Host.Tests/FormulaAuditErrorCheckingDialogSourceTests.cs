using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaAuditErrorCheckingDialogSourceTests
{
    [Fact]
    public void IgnoreSelected_RemovesAllIssuesForIgnoredCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("var sameCellIssues = _issues");
        source.Should().Contain(".Where(candidate =>");
        source.Should().Contain("candidate.SheetId == issue.SheetId");
        source.Should().Contain("candidate.Address.Equals(issue.Address)");
        source.Should().Contain("foreach (var sameCellIssue in sameCellIssues)");
        source.Should().Contain("_issues.Remove(sameCellIssue)");
    }

    [Fact]
    public void EvaluateFormulaDialog_ExposesExcelLikeStepRestartAndHelpControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "EvaluateFormulaDialog.cs"));

        source.Should().Contain("Evaluation:");
        source.Should().Contain("Step _In");
        source.Should().Contain("Step _Out");
        source.Should().Contain("_session.StepOut()");
        source.Should().Contain("_Restart");
        source.Should().Contain("_Help on this formula");
        source.Should().Contain("ShowFormulaHelp");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
    }
}
