using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HelpCommandSourceTests
{
    [Theory]
    [InlineData("Help Online", "HO", "HelpOnlineBtn_Click")]
    [InlineData("Feedback", "FE", "SendFeedbackBtn_Click")]
    [InlineData("Copy Diagnostics", "DG", "CopyDiagnosticsBtn_Click")]
    [InlineData("Check for Updates", "UP", "CheckForUpdatesBtn_Click")]
    [InlineData("About FreeX", "AB", "AboutBtn_Click")]
    public void HelpEnabledCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Contact Support", "CS")]
    [InlineData("Show Training", "TR")]
    [InlineData("What's New", "WN")]
    public void HelpDeferredCommands_RemainDisabledWithoutClickHandlers(string title, string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Fact]
    public void HelpCommandHandlers_RouteThroughExpectedDiagnosticsAndExternalLinkServices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, \"Help Online\")");
        source.Should().Contain("OpenExternalHelpLink(AppUpdateSource.CreateDefault().ReleasePageUrl, \"Check for Updates\")");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), \"Feedback\")");
        source.Should().Contain("AppInfo.AboutText");
        source.Should().Contain("AppIssueReporter.CreateDiagnosticsText(context)");
        source.Should().Contain("Clipboard.SetText(diagnosticsText);");
        source.Should().Contain("UseShellExecute = true");
        source.Should().Contain("ShowOwnedMessage(");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} help command should be present");

        var start = xaml.LastIndexOf('<', titleIndex);
        while (start >= 0 && !xaml[start..].StartsWith("<Button", StringComparison.Ordinal) && !xaml[start..].StartsWith("<local:AutomationInvokeButton", StringComparison.Ordinal))
            start = xaml.LastIndexOf('<', start - 1);

        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} help command should be a Button or AutomationInvokeButton");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} help button should have a closing marker");
        return xaml[start..end];
    }
}
