using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HelpCommandSourceTests
{
    [Theory]
    [InlineData("MainWindow_Content_HelpOnline", "Help Online", "HO", "HelpOnlineBtn_Click")]
    [InlineData("MainWindow_Content_Feedback", "Feedback", "FE", "SendFeedbackBtn_Click")]
    [InlineData("MainWindow_Content_CopyDiagnostics", "Copy Diagnostics", "DG", "CopyDiagnosticsBtn_Click")]
    [InlineData("MainWindow_Content_CheckForUpdates", "Check for Updates", "UP", "CheckForUpdatesBtn_Click")]
    [InlineData("MainWindow_Content_AboutFreeX", "About FreeX", "AB", "AboutBtn_Click")]
    [InlineData("MainWindow_Content_LegalNotices", "Legal Notices", "LN", "LegalNoticesBtn_Click")]
    public void HelpEnabledCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string contentKey,
        string commandName,
        string keyTip,
        string handler)
    {
        var elementName = commandName == "Copy Diagnostics" ? "Button" : "local:AutomationInvokeButton";
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractElementByInvariantCommandName(elementName, commandName, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", UiText.Get(contentKey));
        button.ShouldContainInvariantCommandName(commandName);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Contact Support")]
    [InlineData("Show Training")]
    [InlineData("What's New")]
    public void HelpOutOfScopeCommands_AreNotSurfacedAsDisabledRibbonButtons(string title)
    {
        LocalizedXamlTestSupport.ReadMainWindowXaml()
            .Should()
            .NotContain($"local:RibbonMetadata.CommandName=\"{LocalizedXamlTestSupport.EscapeAttribute(title)}\"");
    }

    [Fact]
    public void HelpCommandHandlers_RouteThroughExpectedDiagnosticsAndExternalLinkServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get(\"MainWindowMessage_HelpOnlineTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppUpdateSource.CreateDefault().ReleasePageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get(\"MainWindowMessage_FeedbackTitle\"))");
        source.Should().Contain("AppInfo.AboutText");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_AboutFreeXTitle\")");
        source.Should().Contain("var dialog = new LegalNoticesDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        source.Should().Contain("AppIssueReporter.CreateDiagnosticsText(context)");
        source.Should().Contain("Clipboard.SetText(diagnosticsText);");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_CopyDiagnosticsTitle\")");
        source.Should().Contain("ExternalUrlLauncher.Open(");
        source.Should().Contain("ShowOwnedMessage(");
    }

    [Fact]
    public void HelpKeyboardShortcut_RoutesF1ToHelpOnlineCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHelp, HelpOnlineBtn_Click);");
    }

}
