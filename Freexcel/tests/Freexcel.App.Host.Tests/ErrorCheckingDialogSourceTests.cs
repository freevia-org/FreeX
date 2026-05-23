using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ErrorCheckingDialogSourceTests
{
    [Fact]
    public void DialogListAndHeaderUseIssueWordingForMixedAuditResults()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("issue(s) found.");
        source.Should().Contain("Header = \"Issue\"");
        source.Should().NotContain("error(s) found.");
        source.Should().NotContain("Header = \"Error\"");
    }

    [Fact]
    public void ErrorCheckingEmptyResultMessageUsesIssueWording()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.FormulaCommands.cs"));

        source.Should().Contain("No issues found.");
        source.Should().NotContain("No errors found.");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesOptionsCallbackButton()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ErrorCheckingDialog.cs"));
        var formulaSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.FormulaCommands.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        dialogSource.Should().Contain("Action? openOptions");
        dialogSource.Should().Contain("Content = \"_Options...\"");
        dialogSource.Should().Contain("_openOptions?.Invoke()");
        formulaSource.Should().Contain("ShowOptionsDialog");
        backstageSource.Should().Contain("private void ShowOptionsDialog()");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ErrorCheckingDialog.cs"));

        foreach (var content in new[]
        {
            "_Go To",
            "_Previous",
            "_Next",
            "_Ignore Error",
            "_Trace Error",
            "_Options...",
            "_Close"
        })
            source.Should().Contain($"Content = \"{content}\"");
    }

    [Fact]
    public void ErrorCheckingDialog_UsesExcelLikeErrorHelpAndActionStructure()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("Error help");
        source.Should().Contain("Content = \"_Help on this error\"");
        source.Should().Contain("ShowSelectedIssueHelp");
        source.Should().Contain("Content = \"Show _Calculation Steps\"");
        source.Should().Contain("Content = \"_Ignore Error\"");
        source.Should().Contain("Content = \"_Edit in Formula Bar\"");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
    }
}
