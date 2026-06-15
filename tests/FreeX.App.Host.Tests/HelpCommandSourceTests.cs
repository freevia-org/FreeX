using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HelpCommandSourceTests
{

    [Fact]
    public void HelpCommandHandlers_RouteThroughExpectedDiagnosticsAndExternalLinkServices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get(\"MainWindowMessage_HelpOnlineTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppUpdateSource.CreateDefault().ReleasePageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get(\"MainWindowMessage_FeedbackTitle\"))");
        source.Should().Contain("var dialog = new AboutDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
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
