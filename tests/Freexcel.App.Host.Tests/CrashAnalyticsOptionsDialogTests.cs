using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CrashAnalyticsOptionsDialogTests
{
    [Fact]
    public void OptionsDialog_ExposesCrashAnalyticsOptInInTrustCenter()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"OptCrashAnalytics\"");
        xaml.Should().Contain("Send opt-in crash reports");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Crash reports include app version, runtime, operating system, session ID, exception type, message, and stack trace. Workbook contents, formulas, filenames, and paths are not collected intentionally, but exception details can occasionally include sensitive values.\"");
        xaml.Should().Contain("Local tester diagnostics");
        xaml.Should().Contain("%LOCALAPPDATA%\\Freexcel\\Diagnostics");
        xaml.Should().Contain("Crash exception messages and stack traces can occasionally contain sensitive values, so review files before sharing them.");
        xaml.Should().Contain("FREEXCEL_DIAGNOSTICS=0");
        source.Should().Contain("OptCrashAnalytics.IsChecked = _opts.CrashAnalyticsEnabled");
        source.Should().Contain("CrashAnalyticsEnabled = OptCrashAnalytics.IsChecked == true");
    }
}
