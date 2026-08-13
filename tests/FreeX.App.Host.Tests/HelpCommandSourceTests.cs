using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HelpCommandSourceTests
{

    [Fact]
    public void HelpCommandHandlers_RouteThroughExpectedDiagnosticsAndExternalLinkServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var appSource = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get(\"MainWindowMessage_HelpOnlineTitle\"))");
        source.Should().Contain("if (!App.TryGetServices(out var services))");
        source.Should().Contain("OpenExternalHelpLink(updates.ReleasesPageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");
        appSource.Should().Contain("public static bool TryGetServices(");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get(\"MainWindowMessage_FeedbackTitle\"))");
        source.Should().Contain("var dialog = new AboutDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        source.Should().Contain("var dialog = new LegalNoticesDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        source.Should().Contain("AppIssueReporter.CreateDiagnosticsText(context)");
        source.Should().Contain("_platformClipboard.WriteAsync(");
        source.Should().Contain("new PlatformClipboardContent(Text: diagnosticsText)");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_CopyDiagnosticsTitle\")");
        source.Should().Contain("DesktopExternalUriLauncher.Open(");
        source.Should().Contain("ShowOwnedMessage(");
    }

    [Fact]
    public void HelpKeyboardShortcut_RoutesF1ToHelpOnlineCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHelp, HelpOnlineBtn_Click);");
    }

    [Fact]
    public void HelpRibbonFeedbackCommand_HasMatchingLiveHandler()
    {
        var ribbonDefinition = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var reviewCommands = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        ribbonDefinition.Should().Contain(".Large(FreeXRibbonCommandIds.HelpFeedback, \"Feedback\"");
        reviewCommands.Should().Contain("private void FeedbackBtn_Click(");
        reviewCommands.Should().NotContain("private void SendFeedbackBtn_Click(");
    }

}
