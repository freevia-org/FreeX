using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AppDiagnosticsStartupTests
{
    [Fact]
    public void AppStartup_RegistersDiagnosticsAndCrashHandlers()
    {
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");
        var crashHandlersSource = WorkspaceFileLocator.ReadAllText(
            "shared",
            "Free.Shared.AppServices",
            "AppCrashHandlers.cs");

        source.Should().Contain("AddSingleton<IApplicationDataPathProvider>(PlatformApplicationDataPathProvider.Instance)");
        source.Should().Contain("AddSingleton<IAppDiagnosticsPathProvider>(PlatformAppDiagnosticsPathProvider.Instance)");
        source.Should().Contain("AppDiagnosticsOptions.CreateDefault(sp.GetRequiredService<IAppDiagnosticsPathProvider>())");
        source.Should().Contain("AddSingleton<AppDiagnosticsFileStore>()");
        source.Should().Contain("AddSingleton<IAppDiagnostics, AppDiagnostics>()");
        source.Should().Contain("AppCrashHandlers.Register(");
        source.Should().Contain("DispatcherUnhandledException");
        crashHandlersSource.Should().Contain("AppDomain.CurrentDomain.UnhandledException");
        crashHandlersSource.Should().Contain("TaskScheduler.UnobservedTaskException");
        source.Should().Contain("RecordEvent(\"app_start\")");
        source.Should().Contain("RecordEvent(\"app_ready\")");
        source.Should().Contain("RecordEvent(\"app_exit\"");
    }

    [Fact]
    public void AppStartup_OpensExistingWorkbookArgument()
    {
        var appSource = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        appSource.Should().Contain("foreach (var startupWorkbookPath in e.Args)");
        appSource.Should().Contain("if (!File.Exists(startupWorkbookPath))");
        appSource.Should().Contain("OpenStartupFileAsync(startupWorkbookPath)");
        appSource.Should().Contain("break;");
        backstageSource.Should().Contain("internal Task OpenStartupFileAsync(string path) => OpenFileAsync(path);");
    }
}
