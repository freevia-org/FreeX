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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("No issues found.");
        source.Should().NotContain("No errors found.");
    }
}
