using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AppDiagnosticsStartupTests
{
    [Fact]
    public void AppStartup_RegistersDiagnosticsAndCrashHandlers()
    {
        var sourcePath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs");
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

    [Fact]
    public void AppStartup_OpensExistingWorkbookArgument()
    {
        var appSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        appSource.Should().Contain("e.Args.FirstOrDefault(File.Exists)");
        appSource.Should().Contain("OpenStartupFileAsync(startupWorkbookPath)");
        backstageSource.Should().Contain("internal Task OpenStartupFileAsync(string path) => OpenFileAsync(path);");
    }
}
