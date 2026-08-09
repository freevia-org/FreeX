using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// App-specific WPF startup inputs. The runner owns the common application/diagnostics ceremony; hosts
/// provide identity, optional shell seam installation, and their focused workarea window factory.
/// </summary>
public sealed record WpfApplicationStartupSpec<TOptions>(
    AppProductIdentity ProductIdentity,
    Func<TOptions, ApplicationOptionsStore<TOptions>, Window> CreateWindow)
    where TOptions : class, INormalizableApplicationOptions, new()
{
    public Action? InstallSharedSeams { get; init; }

    public IWpfApplicationThemeStartupSpec? Theme { get; init; }

    public WpfApplicationLocalizationStartupSpec<TOptions>? Localization { get; init; }

    public IApplicationDataPathProvider? OptionsPathProvider { get; init; }

    public string? OptionsOverridePath { get; init; }

    public string OptionsFileName { get; init; } = ApplicationOptionsStore<TOptions>.DefaultFileName;

    public IAppDiagnosticsPathProvider? DiagnosticsPathProvider { get; init; }
}

public interface IWpfApplicationThemeStartupSpec
{
    void Apply(Application application, Func<string, string?> getEnvironmentVariable);
}

public sealed record WpfApplicationThemeStartupSpec<TTheme>(
    string EnvironmentVariableName,
    string AlternateThemeValue,
    TTheme DefaultTheme,
    TTheme AlternateTheme,
    string ResourceKeyPrefix,
    Action<Application, TTheme, string> ApplyTheme)
    : IWpfApplicationThemeStartupSpec
{
    public Action<TTheme>? SetActiveTheme { get; init; }

    public void Apply(Application application, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentException.ThrowIfNullOrEmpty(EnvironmentVariableName);
        ArgumentException.ThrowIfNullOrEmpty(AlternateThemeValue);
        ArgumentException.ThrowIfNullOrEmpty(ResourceKeyPrefix);
        ArgumentNullException.ThrowIfNull(ApplyTheme);

        var theme = string.Equals(
            getEnvironmentVariable(EnvironmentVariableName),
            AlternateThemeValue,
            StringComparison.OrdinalIgnoreCase)
            ? AlternateTheme
            : DefaultTheme;

        SetActiveTheme?.Invoke(theme);
        ApplyTheme(application, theme, ResourceKeyPrefix);
    }
}

public sealed record WpfApplicationLocalizationStartupSpec<TOptions>(
    Func<TOptions, string?> SelectUiLanguage,
    Action<string?> ApplyUiLanguage,
    Action ApplyCurrentCultureToWpf)
    where TOptions : class, INormalizableApplicationOptions, new()
{
    public void Apply(TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(SelectUiLanguage);
        ArgumentNullException.ThrowIfNull(ApplyUiLanguage);
        ArgumentNullException.ThrowIfNull(ApplyCurrentCultureToWpf);

        ApplyUiLanguage(SelectUiLanguage(options));
        ApplyCurrentCultureToWpf();
    }
}

/// <summary>
/// Runs the shared WPF application startup lifecycle used by the sister WPF apps.
/// </summary>
public static class WpfApplicationStartupRunner
{
    public const string StartupEventName = "app_start";
    public const string ExitEventName = "app_exit";

    public static void Run<TOptions>(WpfApplicationStartupSpec<TOptions> spec)
        where TOptions : class, INormalizableApplicationOptions, new() =>
        Run(spec, WpfApplicationStartupRuntime.Default);

    internal static void Run<TOptions>(
        WpfApplicationStartupSpec<TOptions> spec,
        WpfApplicationStartupRuntime runtime)
        where TOptions : class, INormalizableApplicationOptions, new()
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(runtime);

        AppProduct.Current = spec.ProductIdentity;
        spec.InstallSharedSeams?.Invoke();

        var optionsStore = ApplicationOptionsStore<TOptions>.Create(
            spec.OptionsPathProvider,
            spec.OptionsOverridePath,
            spec.OptionsFileName);
        var options = optionsStore.Load();
        var diagnostics = runtime.CreateDiagnostics(runtime.ResolveVersion(), spec.DiagnosticsPathProvider);
        var app = runtime.CreateApplication();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;

        diagnostics.RegisterCrashHandlers(
            handler => app.DispatcherUnhandledException += (_, args) => handler(args.Exception));

        // The handler above records a dispatcher fault but does not mark it handled, so an exception
        // escaping a ribbon Click handler would still terminate the app. The shared ribbon renderer
        // contains those instead and reports them here, so they are still tracked.
        Free.Shared.Ribbon.RibbonCommandFaultReporter.Handler = (exception, commandId) =>
            diagnostics.RecordCrash(exception, "ribbon_command:" + commandId);

        diagnostics.RecordEvent(StartupEventName);

        spec.Theme?.Apply(app, runtime.GetEnvironmentVariable);
        spec.Localization?.Apply(options);
        runtime.RunApplication(app, spec.CreateWindow(options, optionsStore));

        diagnostics.RecordEvent(ExitEventName);
    }
}

internal sealed class WpfApplicationStartupRuntime
{
    public static WpfApplicationStartupRuntime Default { get; } = new();

    public Func<Application> CreateApplication { get; init; } = () => new Application();

    public Func<string> ResolveVersion { get; init; } = EntryAssemblyVersion.Resolve;

    public Func<string, string?> GetEnvironmentVariable { get; init; } =
        name => Environment.GetEnvironmentVariable(name);

    public Func<string, IAppDiagnosticsPathProvider?, IWpfApplicationStartupDiagnostics> CreateDiagnostics { get; init; } =
        (version, provider) => new LocalWpfApplicationStartupDiagnostics(
            LocalAppDiagnostics.CreateDefault(version, provider));

    public Action<Application, Window> RunApplication { get; init; } = (app, window) => app.Run(window);
}

internal interface IWpfApplicationStartupDiagnostics
{
    void RegisterCrashHandlers(Action<Action<Exception>> subscribeDispatcher);

    void RecordCrash(Exception exception, string source);

    void RecordEvent(string eventName);
}

internal sealed class LocalWpfApplicationStartupDiagnostics(LocalAppDiagnostics diagnostics)
    : IWpfApplicationStartupDiagnostics
{
    public void RegisterCrashHandlers(Action<Action<Exception>> subscribeDispatcher) =>
        diagnostics.RegisterCrashHandlers(subscribeDispatcher);

    public void RecordCrash(Exception exception, string source) =>
        diagnostics.RecordCrash(exception, source);

    public void RecordEvent(string eventName) => diagnostics.RecordEvent(eventName);
}
