using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FreeX.Core.IO;
using FreeX.App.UI;

namespace FreeX.App.Host;

/// <summary>
/// Application entry point and composition root.
/// Configures DI, Serilog, and shows the main window.
/// </summary>
public partial class App : Application
{
    private static FreeXOptions? _startupOptions;

    public static ServiceProvider Services { get; private set; } = null!;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var options = FreeXOptions.Load();
        AppLocalization.ApplyAppLanguage(options.AppLanguage);
        AppLocalization.ApplyCurrentCultureToWpf();
        DialogSizing.RegisterAppDialogSizing();

        // Configure Serilog — resolve the log directory under LocalApplicationData so that
        // file-association launches (which may use System32 or a read-only install dir as cwd)
        // always write to a stable, writable location.  PlatformApplicationDataPathProvider.LocalInstance
        // is a static singleton with no DI dependency, safe to call this early in startup.
        var logDirectory = Path.Combine(
            PlatformApplicationDataPathProvider.LocalInstance.GetApplicationDataDirectory(),
            AppStoragePathPlanner.ProductDirectoryName,
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, "FreeX-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("FreeX starting up");

        // Configure DI
        var serviceCollection = new ServiceCollection();
        _startupOptions = options;
        try
        {
            ConfigureServices(serviceCollection);
        }
        finally
        {
            _startupOptions = null;
        }
        Services = serviceCollection.BuildServiceProvider();
        var crashAnalytics = Services.GetRequiredService<ICrashAnalytics>();
        var crashAnalyticsOptions = Services.GetRequiredService<AppCrashAnalyticsOptions>();
        var diagnosticsMetadata = Services.GetRequiredService<AppDiagnosticsMetadata>();
        PromptForCrashAnalyticsConsentIfNeeded(options, crashAnalyticsOptions);
        if (options.CrashAnalyticsEnabled != crashAnalyticsOptions.IsEnabled)
        {
            crashAnalyticsOptions = AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled);
        }

        crashAnalytics.Initialize(crashAnalyticsOptions, diagnosticsMetadata);
        var diagnostics = Services.GetRequiredService<IAppDiagnostics>();
        var snapshotStore = AutosaveSnapshotStore.CreateDefault(
            Services.GetRequiredService<IApplicationDataPathProvider>());
        RegisterCrashHandlers(diagnostics, snapshotStore);
        diagnostics.RecordEvent("app_start");

        // Show main window
        var mainWindow = Services.GetRequiredService<MainWindow>();

        // Wire autosave before showing the window.
        var autosaveService = new AutosaveService(snapshotStore);
        mainWindow.AttachAutosaveService(autosaveService, snapshotStore);

        mainWindow.Show();

        // Startup recovery: offer to restore any snapshots from previous crashed sessions.
        // This runs after Show() so the window is visible as the host for any dialogs.
        OfferStartupRecovery(mainWindow, snapshotStore);

        foreach (var startupWorkbookPath in e.Args)
        {
            if (!File.Exists(startupWorkbookPath))
                continue;

            _ = mainWindow.Dispatcher.BeginInvoke(async () => await mainWindow.OpenStartupFileAsync(startupWorkbookPath));
            break;
        }

        diagnostics.RecordEvent("app_ready");
        Log.Information("FreeX ready");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        var options = _startupOptions ?? FreeXOptions.Load();
        services.AddSingleton(options);

        services.AddSingleton<IApplicationDataPathProvider>(PlatformApplicationDataPathProvider.Instance);
        services.AddSingleton<IAppDiagnosticsPathProvider>(PlatformAppDiagnosticsPathProvider.Instance);

        // Local tester diagnostics. No network upload; files stay under the platform diagnostics root.
        services.AddSingleton(sp =>
            AppDiagnosticsOptions.CreateDefault(sp.GetRequiredService<IAppDiagnosticsPathProvider>()));
        services.AddSingleton(AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled));
        services.AddSingleton(AppDiagnosticsMetadata.Create(AppInfo.VersionText));
        services.AddSingleton<AppDiagnosticsFileStore>();
        services.AddSingleton<ICrashAnalytics, SentryCrashAnalytics>();
        services.AddSingleton<IAppDiagnostics, AppDiagnostics>();

        // Core services
        services.AddSingleton<DependencyGraph>();
        services.AddSingleton<FormulaEvaluator>();
        services.AddSingleton<RecalcEngine>();
        services.AddSingleton<IViewportService, ViewportService>();
        foreach (var adapter in WorkbookFileAdapterCatalog.CreateDefaultAdapters())
            services.AddSingleton<IFileAdapter>(adapter);

        // Workbook (single workbook for now, will expand later)
        services.AddSingleton(_ => NewWorkbookFactory.Create(options));

        // Mutable reference wrapper — updated whenever a new file is loaded.
        services.AddSingleton(sp =>
            new WorkbookRef { Current = sp.GetRequiredService<Workbook>() });

        // Command bus always resolves through WorkbookRef so it sees the current workbook.
        services.AddSingleton<ICommandBus>(sp =>
        {
            var wbRef = sp.GetRequiredService<WorkbookRef>();
            return new CommandBus(
                _ => new WorkbookCommandContext(wbRef.Current),
                (id, ctx) => XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(ctx.Workbook, out _));
        });

        // Message service
        services.AddSingleton<IUserMessageService, WpfUserMessageService>();

        // Multi-window registry: tracks every live window viewing the single shared workbook
        // (Excel "New Window"). Singleton so all windows coordinate through one registry.
        services.AddSingleton<WorkbookWindowRegistry>();

        // UI
        services.AddTransient<MainWindow>();
    }

    private static void RegisterCrashHandlers(IAppDiagnostics diagnostics, AutosaveSnapshotStore snapshotStore)
    {
        Current.DispatcherUnhandledException += (_, args) =>
        {
            diagnostics.RecordCrash(args.Exception, "dispatcher");
            TryEmergencySaveAllWindows(snapshotStore);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                diagnostics.RecordCrash(exception, "appdomain");
            TryEmergencySaveAllWindows(snapshotStore);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnostics.RecordCrash(args.Exception, "task");
        };
    }

    /// <summary>
    /// Best-effort emergency snapshot of all open dirty windows. Called from crash handlers.
    /// Must never throw.
    /// </summary>
    private static void TryEmergencySaveAllWindows(AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            foreach (Window window in Current.Windows)
            {
                if (window is not MainWindow mainWindow)
                    continue;

                try
                {
                    var svc = mainWindow.AutosaveServiceForCrashHandler;
                    if (svc is null)
                        continue;

                    svc.TryEmergencySnapshot(mainWindow);
                }
                catch
                {
                    // A crash handler must never throw.
                }
            }
        }
        catch
        {
            // Outer guard — crash handlers must never throw.
        }
    }

    /// <summary>
    /// Checks for recovery snapshots from previous crashed sessions and offers restore/discard.
    /// Must be called after the main window is shown (it owns any dialogs).
    /// Stale or corrupt snapshots are silently deleted.
    /// </summary>
    private static void OfferStartupRecovery(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            var candidates = snapshotStore.EnumerateCandidates();
            if (candidates.Count == 0)
                return;

            // For this first implementation we handle the first candidate (single-workbook app).
            var candidate = candidates[0];

            var displayName = candidate.Sidecar.DisplayName;
            var prompt = string.IsNullOrWhiteSpace(displayName)
                ? UiText.Get("Startup_RecoveryPrompt")
                : UiText.Format("Startup_RecoveryPromptNamed", displayName);

            var result = MessageBox.Show(
                prompt,
                UiText.Get("Startup_RecoveryTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _ = mainWindow.Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            await mainWindow.OpenStartupFileAsync(candidate.SnapshotPath);
                            mainWindow.SetCurrentFilePathForRecovery(candidate.Sidecar.OriginalFilePath);
                            // Mark dirty so the user knows this came from a recovery.
                            mainWindow.MarkWorkbookDirtyForRecovery();
                            AutosaveSnapshotStore.DeleteCandidate(candidate);
                        }
                        catch
                        {
                            // If recovery load fails, clean up the bad snapshot.
                            AutosaveSnapshotStore.DeleteCandidate(candidate);
                        }
                    });
                }
                catch
                {
                    AutosaveSnapshotStore.DeleteCandidate(candidate);
                }
            }
            else
            {
                // User chose to discard — delete all candidates.
                foreach (var c in candidates)
                {
                    try { AutosaveSnapshotStore.DeleteCandidate(c); } catch { /* best-effort */ }
                }
            }
        }
        catch
        {
            // Startup recovery must never affect normal startup.
        }
    }

    private static void PromptForCrashAnalyticsConsentIfNeeded(
        FreeXOptions options,
        AppCrashAnalyticsOptions crashAnalyticsOptions)
    {
        if (!CrashAnalyticsConsentPlanner.ShouldPrompt(options, crashAnalyticsOptions))
            return;

        // Use MessageBox directly here: IUserMessageService is not yet available at this early
        // startup point (before the main window is shown), so we fall back to a raw call.
        var result = MessageBox.Show(
            UiText.Get("Startup_CrashReportsConsentPrompt"),
            UiText.Get("Startup_CrashReportsTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        CrashAnalyticsConsentPlanner.ApplyConsent(options, result == MessageBoxResult.Yes);
        options.Save();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.GetService<IAppDiagnostics>()?.RecordEvent("app_exit", new Dictionary<string, string?>
        {
            ["status"] = e.ApplicationExitCode.ToString()
        });
        Log.Information("FreeX shutting down");
        Log.CloseAndFlush();
        Services.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Mutable holder for the active workbook, updated on file open.</summary>
public sealed class WorkbookRef
{
    public Workbook Current { get; set; } = null!;
}

/// <summary>Simple command context that provides access to the workbook.</summary>
internal sealed class WorkbookCommandContext : ICommandContext
{
    public Workbook Workbook { get; }

    public WorkbookCommandContext(Workbook workbook) => Workbook = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
