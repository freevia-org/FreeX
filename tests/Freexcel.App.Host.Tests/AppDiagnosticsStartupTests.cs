using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class AppDiagnosticsStartupTests
{
    [Fact]
    public void AppStartup_RegistersDiagnosticsAndCrashHandlers()
    {
        var sourcePath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("AddSingleton(AppDiagnosticsOptions.CreateDefault())");
        source.Should().Contain("AddSingleton<AppDiagnosticsFileStore>()");
        source.Should().Contain("AddSingleton<IAppDiagnostics, AppDiagnostics>()");
        source.Should().Contain("DispatcherUnhandledException");
        source.Should().Contain("AppDomain.CurrentDomain.UnhandledException");
        source.Should().Contain("TaskScheduler.UnobservedTaskException");
        source.Should().Contain("RecordEvent(\"app_start\")");
        source.Should().Contain("RecordEvent(\"app_ready\")");
        source.Should().Contain("RecordEvent(\"app_exit\"");
    }
}
