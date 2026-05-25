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
        xaml.Should().Contain("AutomationProperties.HelpText=\"Crash reports include app version, runtime, operating system, session ID, and exception details. Workbook contents, formulas, filenames, and paths are not collected by default.\"");
        source.Should().Contain("OptCrashAnalytics.IsChecked = _opts.CrashAnalyticsEnabled");
        source.Should().Contain("CrashAnalyticsEnabled = OptCrashAnalytics.IsChecked == true");
    }
}
