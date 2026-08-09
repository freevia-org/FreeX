using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Host.Tests;

public sealed class SharedWpfStartupRunnerTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.SharedWpfStartupRunnerTests", Guid.NewGuid().ToString("N"));

    public SharedWpfStartupRunnerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [StaFact]
    public void Run_InstallsIdentityLoadsOptionsAndRecordsLifecycleAroundWindowRun()
    {
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
                    (options, store) =>
                    {
                        order.Add("window");
                        options.Marker.Should().Be("loaded");
                        options.RecentFilesLimit.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
                        options.UiLanguage.Should().Be("qps-ploc");
                        store.StorePath.Should().Be(optionsPath);
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
                        EnvironmentVariableName: "DUMMY_THEME",
                        AlternateThemeValue: "midnight",
                        DefaultTheme: "default",
                        AlternateTheme: "alternate",
                        ResourceKeyPrefix: "Dummy",
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
                runtime);
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
            WpfApplicationStartupRunner.ExitEventName);
        order.Should().Equal("seams", "application", "theme", "language", "wpf-culture", "window", "run");
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
        TestWorkspaceFileLocator.Find(RepositoryFile);

    private sealed class CapturingDiagnostics(
        List<string> events,
        Action onRegisterCrashHandlers) : IWpfApplicationStartupDiagnostics
    {
        public void RegisterCrashHandlers(Action<Action<Exception>> subscribeDispatcher)
        {
            subscribeDispatcher(_ => { });
            onRegisterCrashHandlers();
        }

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
