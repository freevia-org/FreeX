using Avalonia;
using Avalonia.Controls;
using Avalonia.Fonts.Inter;
using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAvaloniaLocalizationStartupDescriptor(Action Install)
{
    internal void Apply()
    {
        ArgumentNullException.ThrowIfNull(Install);
        Install();
    }
}

public interface ISisterAvaloniaThemeStartupDescriptor
{
    void Apply(Application application, Func<string, string?> getEnvironmentVariable);
}

public sealed record SisterAvaloniaThemeStartupDescriptor<TTheme>(
    ApplicationThemeStartupPlan<TTheme> Plan,
    Action<TTheme> SetActiveTheme,
    Action<Application, TTheme, string> ApplyResources)
    : ISisterAvaloniaThemeStartupDescriptor
    where TTheme : notnull
{
    public void Apply(Application application, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(Plan);
        ArgumentNullException.ThrowIfNull(SetActiveTheme);
        ArgumentNullException.ThrowIfNull(ApplyResources);

        Plan.Apply(
            getEnvironmentVariable,
            SetActiveTheme,
            (theme, resourceKeyPrefix) => ApplyResources(application, theme, resourceKeyPrefix));
    }
}

public sealed record SisterAvaloniaOptionsStartupDescriptor<TOptions>(
    Func<IApplicationOptionsStore<TOptions>> CreateStore)
    where TOptions : class, INormalizableApplicationOptions, new()
{
    internal (TOptions Options, IApplicationOptionsStore<TOptions> Store) Load()
    {
        ArgumentNullException.ThrowIfNull(CreateStore);
        var store = CreateStore()
            ?? throw new InvalidOperationException("Avalonia options startup returned no store.");
        return (store.Load(), store);
    }
}

public sealed record SisterAvaloniaWindowStartupDescriptor<TWindow, TOptions>(
    Func<IReadOnlyList<string>, TOptions, IApplicationOptionsStore<TOptions>, TWindow> Create,
    Action<TWindow>? AfterCreated = null)
    where TWindow : Window
    where TOptions : class, INormalizableApplicationOptions, new();

public sealed record SisterAvaloniaStandardDesktopLaunch<TWindow>(
    IReadOnlyList<string> StartupArguments,
    Action<TWindow>? AfterMainWindowCreated = null)
    where TWindow : Window;

/// <summary>
/// Product-owned inputs for the standard FreeW/FreeP Avalonia desktop composition root.
/// </summary>
public sealed class SisterAvaloniaStandardDesktopProfile<TApplication, TWindow, TOptions>
    where TApplication : Application, new()
    where TWindow : Window
    where TOptions : class, INormalizableApplicationOptions, new()
{
    private SisterAvaloniaStandardDesktopLaunch<TWindow>? _pendingLaunch;

    public SisterAvaloniaStandardDesktopProfile(
        AppProductIdentity productIdentity,
        SisterAvaloniaLocalizationStartupDescriptor localization,
        ISisterAvaloniaThemeStartupDescriptor theme,
        SisterAvaloniaOptionsStartupDescriptor<TOptions> options,
        SisterAvaloniaWindowStartupDescriptor<TWindow, TOptions> window,
        Action? onEmergencySnapshot = null)
    {
        ProductIdentity = productIdentity ?? throw new ArgumentNullException(nameof(productIdentity));
        Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Window = window ?? throw new ArgumentNullException(nameof(window));
        OnEmergencySnapshot = onEmergencySnapshot;
    }

    public AppProductIdentity ProductIdentity { get; }

    public SisterAvaloniaLocalizationStartupDescriptor Localization { get; }

    public ISisterAvaloniaThemeStartupDescriptor Theme { get; }

    public SisterAvaloniaOptionsStartupDescriptor<TOptions> Options { get; }

    public SisterAvaloniaWindowStartupDescriptor<TWindow, TOptions> Window { get; }

    /// <summary>
    /// Optional best-effort hook run immediately after a crash is recorded, threaded through to
    /// <see cref="SisterAvaloniaProgramSpec.OnEmergencySnapshot"/> (R138). A host with no autosave
    /// snapshot to take (e.g. FreeP, which has no autosave feature at all yet) simply leaves this
    /// null. Must never throw.
    /// </summary>
    public Action? OnEmergencySnapshot { get; }

    internal void SetPendingLaunch(SisterAvaloniaStandardDesktopLaunch<TWindow> launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(launch.StartupArguments);
        if (_pendingLaunch is not null)
            throw new InvalidOperationException("An Avalonia desktop launch is already in progress.");

        _pendingLaunch = launch;
    }

    internal SisterAvaloniaStandardDesktopLaunch<TWindow> GetPendingLaunch() =>
        _pendingLaunch ?? new SisterAvaloniaStandardDesktopLaunch<TWindow>([]);

    internal void ClearPendingLaunch(SisterAvaloniaStandardDesktopLaunch<TWindow> launch)
    {
        if (ReferenceEquals(_pendingLaunch, launch))
            _pendingLaunch = null;
    }
}

/// <summary>
/// Owns the standard sister-app desktop builder, program runner wiring, and application initialization.
/// Specialized hosts such as FreeX can continue to compose <see cref="SisterAvaloniaProgramRunner"/>
/// directly when they need updater, recovery, or custom diagnostics lifecycle work.
/// </summary>
public static class SisterAvaloniaStandardDesktopFactory
{
    public static int Run<TApplication, TWindow, TOptions>(
        string[] arguments,
        SisterAvaloniaStandardDesktopProfile<TApplication, TWindow, TOptions> profile)
        where TApplication : Application, new()
        where TWindow : Window
        where TOptions : class, INormalizableApplicationOptions, new() =>
        Run(
            arguments,
            profile,
            new SisterAvaloniaStandardDesktopLaunch<TWindow>(arguments));

    public static int Run<TApplication, TWindow, TOptions>(
        string[] arguments,
        SisterAvaloniaStandardDesktopProfile<TApplication, TWindow, TOptions> profile,
        SisterAvaloniaStandardDesktopLaunch<TWindow> launch)
        where TApplication : Application, new()
        where TWindow : Window
        where TOptions : class, INormalizableApplicationOptions, new() =>
        Run(
            arguments,
            profile,
            launch,
            startupArguments => CreateAppBuilder<TApplication>()
                .StartWithClassicDesktopLifetime(startupArguments),
            SisterAvaloniaProgramRuntime.Default);

    internal static int Run<TApplication, TWindow, TOptions>(
        string[] arguments,
        SisterAvaloniaStandardDesktopProfile<TApplication, TWindow, TOptions> profile,
        SisterAvaloniaStandardDesktopLaunch<TWindow> launch,
        Func<string[], int> startApplication,
        SisterAvaloniaProgramRuntime runtime)
        where TApplication : Application, new()
        where TWindow : Window
        where TOptions : class, INormalizableApplicationOptions, new()
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(startApplication);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            return SisterAvaloniaProgramRunner.Run(
                arguments,
                new SisterAvaloniaProgramSpec(
                    profile.ProductIdentity,
                    preparedArguments =>
                    {
                        profile.SetPendingLaunch(launch);
                        return SisterAvaloniaLaunchPreparation.Continue(preparedArguments);
                    },
                    startApplication)
                {
                    OnEmergencySnapshot = profile.OnEmergencySnapshot
                },
                runtime);
        }
        finally
        {
            profile.ClearPendingLaunch(launch);
        }
    }

    public static AppBuilder CreateAppBuilder<TApplication>()
        where TApplication : Application, new() =>
        AppBuilder.Configure<TApplication>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void Initialize<TApplication, TWindow, TOptions>(
        TApplication application,
        SisterAvaloniaStandardDesktopProfile<TApplication, TWindow, TOptions> profile)
        where TApplication : Application, new()
        where TWindow : Window
        where TOptions : class, INormalizableApplicationOptions, new()
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(profile);

        profile.Localization.Apply();
        profile.Theme.Apply(application, Environment.GetEnvironmentVariable);
        var (options, optionsStore) = profile.Options.Load();
        var launch = profile.GetPendingLaunch();
        var afterCreated = Combine(profile.Window.AfterCreated, launch.AfterMainWindowCreated);

        SisterAvaloniaAppBootstrap.Initialize(
            application,
            new SisterAvaloniaAppBootstrapSpec<TWindow>(
                launch.StartupArguments,
                startupArguments =>
                {
                    ArgumentNullException.ThrowIfNull(profile.Window.Create);
                    return profile.Window.Create(startupArguments, options, optionsStore);
                },
                afterCreated));
    }

    private static Action<TWindow>? Combine<TWindow>(Action<TWindow>? first, Action<TWindow>? second)
        where TWindow : Window
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        return window =>
        {
            first(window);
            second(window);
        };
    }
}
