using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Theme;

namespace FreeW.App.Host.Tests;

public sealed class SharedWpfStartupRunnerTests : IDisposable
{
    private const string IsolatedRunEnvironmentVariable = "FREEW_SHARED_WPF_STARTUP_TEST_CHILD";
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.SharedWpfStartupRunnerTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [StaFact]
    public void Run_InstallsIdentityLoadsOptionsAndRecordsLifecycleAroundWindowRun()
    {
        if (IsolatedTestProcess.RunIfNeeded(
                IsolatedRunEnvironmentVariable,
                "FreeW.App.Host.Tests.SharedWpfStartupRunnerTests.Run_InstallsIdentityLoadsOptionsAndRecordsLifecycleAroundWindowRun"))
        {
            return;
        }

        var optionsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(optionsPath, """{"Marker":" loaded ","RecentFilesLimit":999,"UiLanguage":" qps-ploc "}""");
        var originalProduct = AppProduct.Current;
        var events = new List<string>();
        var order = new List<string>();
        string? activeTheme = null;
        string? appliedTheme = null;
        string? appliedCulture = null;
        var dispatcherCrashHookRegistered = false;
        var windowWasRun = false;
        var seamsInstalledAfterIdentity = false;
        IReadOnlyList<string>? receivedStartupArgs = null;
        var createdOwnApplication = false;
        Application? createdApplication = null;
        ShutdownMode? originalShutdownMode = null;

        var runtime = new WpfApplicationStartupRuntime
        {
            CreateApplication = () =>
            {
                order.Add("application");
                if (Application.Current is { } current)
                {
                    createdApplication = current;
                    originalShutdownMode = current.ShutdownMode;
                    return current;
                }

                createdOwnApplication = true;
                createdApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                return createdApplication;
            },
            ResolveVersion = () => "test-version",
            GetEnvironmentVariable = name => name == "DUMMY_THEME" ? "midnight" : null,
            CreateDiagnostics = (_, _) => new CapturingDiagnostics(events, () => dispatcherCrashHookRegistered = true),
            RunApplication = (app, window) =>
            {
                order.Add("run");
                app.ShutdownMode.Should().Be(ShutdownMode.OnMainWindowClose);
                window.Should().BeOfType<Window>();
                windowWasRun = true;
            }
        };

        try
        {
            WpfApplicationStartupRunner.Run(
                new WpfApplicationStartupSpec<DummyOptions>(
                    new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW"),
                    (options, store, startupArgs) =>
                    {
                        order.Add("window");
                        options.Marker.Should().Be("loaded");
                        options.RecentFilesLimit.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
                        options.UiLanguage.Should().Be("qps-ploc");
                        store.StorePath.Should().Be(optionsPath);
                        receivedStartupArgs = startupArgs;
                        return new Window();
                    })
                {
                    OptionsOverridePath = optionsPath,
                    InstallSharedSeams = () =>
                    {
                        order.Add("seams");
                        seamsInstalledAfterIdentity =
                            AppProduct.Current.ProductDirectoryName == "FreeW";
                    },
                    Theme = new WpfApplicationThemeStartupSpec<string>(
                        Plan: new ApplicationThemeStartupPlan<string>(
                            EnvironmentVariableName: "DUMMY_THEME",
                            AlternateThemeValue: "midnight",
                            DefaultTheme: "default",
                            AlternateTheme: "alternate",
                            ResourceKeyPrefix: "Dummy"),
                        ApplyTheme: (app, theme, prefix) =>
                        {
                            order.Add("theme");
                            app.Should().BeSameAs(createdApplication);
                            theme.Should().Be("alternate");
                            prefix.Should().Be("Dummy");
                            appliedTheme = theme;
                        })
                    {
                        SetActiveTheme = theme => activeTheme = theme
                    },
                    Localization = new WpfApplicationLocalizationStartupSpec<DummyOptions>(
                        SelectUiLanguage: options => options.UiLanguage,
                        ApplyUiLanguage: culture =>
                        {
                            order.Add("language");
                            appliedCulture = culture;
                        },
                        ApplyCurrentCultureToWpf: () => order.Add("wpf-culture"))
                },
                runtime,
                ["C:\\Documents\\Quarterly Report.freew"]);

            // R133-wpf-startup-file-args: when the caller omits startupArgs entirely (the public Run
            // overload defaults it to null), CreateWindow must still see an empty (never-null) list
            // rather than some fallback that silently reads the process's real command line -- a
            // caller with no startup files must not have some OTHER launch's arguments leak in.
            // Reuses the SAME `runtime` (and therefore the SAME already-created Application, on this
            // same STA thread) as the call above rather than standing up a second, independent
            // Application: WPF's Application is process-global and tied to the dispatcher thread that
            // created it, so a second [StaFact] test method creating/shutting down its own Application
            // on a different thread races the Shutdown() this test's `finally` block below performs
            // and intermittently throws "the calling thread cannot access this object" in whichever
            // test runs next.
            IReadOnlyList<string>? receivedStartupArgsWithNoneSupplied = null;
            WpfApplicationStartupRunner.Run(
                new WpfApplicationStartupSpec<DummyOptions>(
                    new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW"),
                    (_, _, startupArgs) =>
                    {
                        order.Add("window");
                        receivedStartupArgsWithNoneSupplied = startupArgs;
                        return new Window();
                    })
                {
                    OptionsOverridePath = optionsPath
                },
                runtime);
            receivedStartupArgsWithNoneSupplied.Should().NotBeNull();
            receivedStartupArgsWithNoneSupplied!.Should().BeEmpty();
        }
        finally
        {
            AppProduct.Current = originalProduct;
            if (createdOwnApplication)
                createdApplication?.Shutdown();
            else if (createdApplication is not null && originalShutdownMode is { } shutdownMode)
                createdApplication.ShutdownMode = shutdownMode;
        }

        seamsInstalledAfterIdentity.Should().BeTrue();
        activeTheme.Should().Be("alternate");
        appliedTheme.Should().Be("alternate");
        appliedCulture.Should().Be("qps-ploc");
        dispatcherCrashHookRegistered.Should().BeTrue();
        windowWasRun.Should().BeTrue();
        events.Should().Equal(
            WpfApplicationStartupRunner.StartupEventName,
            WpfApplicationStartupRunner.ExitEventName,
            WpfApplicationStartupRunner.StartupEventName,
            WpfApplicationStartupRunner.ExitEventName);
        order.Should().Equal(
            "seams", "application", "theme", "language", "wpf-culture", "window", "run",
            "application", "window", "run");
        // R133-wpf-startup-file-args: the runner used to hand CreateWindow no way to see the process's
        // command-line/file-association arguments at all, so a host had no seam to open the requested
        // file even if it wanted to -- CreateWindow must receive exactly what the caller passed in.
        receivedStartupArgs.Should().Equal("C:\\Documents\\Quarterly Report.freew");
    }

    // The shared ribbon renderer contains what a command throws rather than letting it escape a WPF
    // Click handler: the dispatcher hook the runner registers records the fault but does not mark it
    // handled, so an escaping exception would end the process. That containment is only useful if
    // the caught fault still reaches diagnostics, so the runner must also wire the reporter — this
    // pins that wiring for both WPF sister apps, which share this runner.
    [Fact]
    public void SharedWpfStartupRunner_RoutesContainedRibbonCommandFaultsIntoDiagnostics()
    {
        var runner = File.ReadAllText(
            RepositoryFile("shared", "Free.Shared.Shell.Wpf", "WpfApplicationStartupRunner.cs"));

        runner.Should().Contain("Free.Shared.Ribbon.RibbonCommandFaultReporter.Handler =");
        runner.Should().Contain("diagnostics.RecordCrash(exception, \"ribbon_command:\" + commandId)");
    }

    [Fact]
    public void SisterAppPrograms_UseSharedWpfStartupRunner()
    {
        var freeWProgram = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Program.cs"));
        var freePProgram = File.ReadAllText(RepositoryFile("freep", "FreeP.App.Host", "Program.cs"));

        freeWProgram.Should().Contain("WpfApplicationStartupRunner.Run");
        freePProgram.Should().Contain("WpfApplicationStartupRunner.Run");
        freeWProgram.Should().Contain("Theme = new WpfApplicationThemeStartupSpec<Theme>");
        freePProgram.Should().Contain("Theme = new WpfApplicationThemeStartupSpec<Theme>");
        freeWProgram.Should().Contain("Localization = new WpfApplicationLocalizationStartupSpec<FreeWOptions>");
        freePProgram.Should().Contain("Localization = new WpfApplicationLocalizationStartupSpec<FreePOptions>");
        freeWProgram.Should().NotContain("new Application");
        freePProgram.Should().NotContain("new Application");
        freeWProgram.Should().NotContain("System.Environment.GetEnvironmentVariable");
        freePProgram.Should().NotContain("System.Environment.GetEnvironmentVariable");
        freeWProgram.Should().NotContain("Application.Current");
        freePProgram.Should().NotContain("Application.Current");
        freeWProgram.Should().NotContain("AppLocalization.Bootstrap.ApplyAppLanguage(options.UiLanguage)");
        freePProgram.Should().NotContain("AppLocalization.Bootstrap.ApplyAppLanguage(options.UiLanguage)");
        freeWProgram.Should().NotContain("AppLocalization.Bootstrap.ApplyCurrentCultureToWpf();");
        freePProgram.Should().NotContain("AppLocalization.Bootstrap.ApplyCurrentCultureToWpf();");
        freeWProgram.Should().NotContain("RegisterCrashHandlers");
        freePProgram.Should().NotContain("RegisterCrashHandlers");
        freeWProgram.Should().NotContain("RecordEvent(\"app_start\")");
        freePProgram.Should().NotContain("RecordEvent(\"app_start\")");
        freeWProgram.Should().NotContain("RecordEvent(\"app_exit\")");
        freePProgram.Should().NotContain("RecordEvent(\"app_exit\")");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);

    private sealed class CapturingDiagnostics(
        List<string> events,
        Action onRegisterCrashHandlers) : IWpfApplicationStartupDiagnostics
    {
        public void RegisterCrashHandlers(Action<Action<Exception>> subscribeDispatcher)
        {
            subscribeDispatcher(_ => { });
            onRegisterCrashHandlers();
        }

        public List<(Exception Exception, string Source)> Crashes { get; } = [];

        public void RecordCrash(Exception exception, string source) => Crashes.Add((exception, source));

        public void RecordEvent(string eventName) => events.Add(eventName);
    }

    private sealed class DummyOptions : INormalizableApplicationOptions
    {
        public string Marker { get; set; } = "";

        public string UiLanguage { get; set; } = "";

        public int RecentFilesLimit { get; set; } = ApplicationOptionsNormalizer.DefaultRecentFilesCap;

        public void Normalize()
        {
            Marker = Marker.Trim();
            UiLanguage = UiLanguage.Trim();
            RecentFilesLimit = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesLimit);
        }
    }
}
