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
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host;

/// <summary>
/// Application entry point and composition root.
/// Configures DI, Serilog, and shows the main window.
/// </summary>
public partial class App : Application
{
    private static readonly IUserMessageService StartupMessageService = new WpfUserMessageService();

    private static FreeXOptions? _startupOptions;

    private static ServiceProvider? _services;

    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeX"/>).
    /// Stored so that new windows can apply it to their own resource dictionaries.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeX;

    public static ServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Application services are not initialized.");

    public static bool TryGetServices([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ServiceProvider? services)
    {
        services = _services;
        return services is not null;
    }

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var startupArgs = GetStartupArgs(e);

        // Always apply the active brand theme early — before the main window loads — so that
        // DynamicResource references in the title-bar chrome pick up the correct brushes.
        // For the DEFAULT (FreeX) theme the values are BYTE-IDENTICAL to ThemeResources.xaml,
        // so the visual result is unchanged.  FREEX_THEME=midnight swaps in the alternate palette.
        var theme = string.Equals(
            System.Environment.GetEnvironmentVariable("FREEX_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeX;
        ActiveTheme = theme;
        WpfThemeApplier.Apply(this, theme, "FreeX");

        // Velopack is invoked earlier, from Program.Main, before the WPF Application is created,
        // so install/update/uninstall hooks are serviced before any UI initializes.
        var options = FreeXOptions.Load();
        AppLocalization.ApplyAppLanguage(options.AppLanguage);
        AppLocalization.ApplyCurrentCultureToWpf();
        ShellStrings.Current = new ResourceShellStrings(
            () => UiText.Ok,
            () => UiText.Cancel,
            () => UiText.ErrorTitle,
            () => UiText.WarningTitle,
            () => UiText.InformationTitle,
            () => UiText.ConfirmTitle,
            UiText.CreateAutomationName);
        BackstageStrings.Current = new ResourceBackstageStrings(UiText.Get, UiText.Format);
        DialogSizing.RegisterAppDialogSizing();

        // Let the SHARED ribbon-icon factory (used by shared chrome — the BackstageFrame rail, QAT, …)
        // resolve FreeX's branded Office SVGs, falling back to shared geometry when FreeX has no art. Without
        // this the shared BackstageFrame rendered generic RibbonCommandIconKind glyphs instead of FreeX's
        // command icons once the backstage rail moved onto the shared frame (unification P1).
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconElementResolver =
            RibbonIconFactory.TryCreateCommandIconElement;

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
        _services = serviceCollection.BuildServiceProvider();

        // Headless cross-platform visual-parity capture: render each app surface to a PNG and exit,
        // without launching the interactive app. Additive and isolated — only engaged by the
        // --parity-capture <outDir> switch (used by the WPF<->Avalonia visual-parity runner).
        if (ParityCapture.TryGetOutputDirectory(startupArgs) is { } parityOutDir)
        {
            try
            {
                ParityCapture.Run(
                    parityOutDir,
                    () => Services.GetRequiredService<MainWindow>(),
                    ParityCapture.TryGetTargetSurfaceId(startupArgs));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Parity capture failed");
            }

            Shutdown();
            return;
        }

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
        // Returns true if the user accepted at least one recovery, in which case the main
        // window is already hosting the recovered workbook (and is dirty).
        var recoveryAccepted = OfferStartupRecovery(mainWindow, snapshotStore);

        var openedStartupFile = false;
        foreach (var startupWorkbookPath in startupArgs)
        {
            if (!File.Exists(startupWorkbookPath))
                continue;

            openedStartupFile = true;
            if (recoveryAccepted)
            {
                // The main window already holds a recovered workbook. Opening the command-line
                // argument in the same window would prompt "Save changes?" on the just-recovered
                // workbook, and a "No" answer would silently discard it.  Open the file-arg in
                // a new window to keep both workbooks alive and safe.
                _ = mainWindow.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var newWindow = App.Services.GetRequiredService<MainWindow>();
                        newWindow.Show();
                        newWindow.Activate();
                        _ = newWindow.Dispatcher.BeginInvoke(async () =>
                            await newWindow.OpenStartupFileAsync(startupWorkbookPath));
                    }
                    catch
                    {
                        // Best-effort: if we can't open the file-arg in a new window, skip it.
                        // The user can open it manually; we must not discard the recovered workbook.
                    }
                });
            }
            else
            {
                _ = mainWindow.Dispatcher.BeginInvoke(async () =>
                    await mainWindow.OpenStartupFileAsync(startupWorkbookPath));
            }

            break;
        }

        // Warm the XLSX open/save pipeline off the UI thread so the user's first real file open does
        // not pay the cold-process JIT / static-init / assembly-load cost (~6-7s).  Skip it when a
        // startup file-arg or crash-recovery open is already underway — that open is itself the
        // warmup, and a concurrent prewarm would only contend for CPU.
        if (!openedStartupFile && !recoveryAccepted)
            StartupPipelinePrewarmer.StartBackgroundPrewarm();

        diagnostics.RecordEvent("app_ready");
        Log.Information("FreeX ready");

        // Background self-update check. Best-effort: any failure resolves to Unavailable and is
        // swallowed so a flaky network or non-Velopack dev build never disrupts startup.
        _ = Task.Run(async () =>
        {
            try
            {
                var updates = Services.GetRequiredService<FreeX.App.Services.Updates.IUpdateService>();
                var result = await updates.CheckAndDownloadAsync();
                if (result.State == FreeX.App.Services.Updates.UpdateState.ReadyToApply)
                {
                    await mainWindow.Dispatcher.InvokeAsync(() => mainWindow.ShowUpdateReady(result.AvailableVersion));
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Background update check failed."); }
        });
    }

    private static IReadOnlyList<string> GetStartupArgs(StartupEventArgs e)
    {
        if (e.Args.Length > 0)
            return e.Args;

        return Environment.GetCommandLineArgs().Skip(1).ToArray();
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

        // Document state (dirty flag, generation, file path, close-prompt flag).
        // Singleton — the workbook is shared across all windows in the multi-window
        // ("New Window") model, so dirty/clean state is a document property, not a
        // per-view property.  All windows share this one instance; title-bar refresh
        // after a dirty/saved transition is broadcast via WorkbookWindowRegistry.
        services.AddSingleton<WorkbookDocumentState>();

        // New-workbook name sequence (Book1, Book2, …) shared across the session so File > New
        // keeps advancing the default name instead of repeatedly producing Book1 (Issue 121).
        services.AddSingleton<NewWorkbookNameSequence>();

        // Self-update + file associations.
        services.AddSingleton<Free.Shared.AppServices.IFileAssociationService>(
            new Free.Shared.AppServices.Windows.WindowsFileAssociationService(
                FreeX.App.Services.FileAssociations.FreeXFileAssociations.All, logger: null));
        services.AddSingleton<FreeX.App.Services.Updates.IUpdateService>(sp =>
        {
            var channel = AppInfo.ReleaseChannel;
            return FreeX.App.Services.Updates.VelopackUpdateService.CreateForGitHub(
                repoUrl: FreeX.App.Services.Updates.UpdateFeed.GitHubRepoUrl,
                prerelease: FreeX.App.Services.Updates.UpdateFeed.AllowPrereleases(channel),
                releasesPageUrl: AppInfo.LatestReleaseUrl,
                logger: sp.GetService<ILoggerFactory>()?.CreateLogger<FreeX.App.Services.Updates.VelopackUpdateService>());
        });

        // UI
        services.AddTransient<MainWindow>();
    }

    private static void RegisterCrashHandlers(IAppDiagnostics diagnostics, AutosaveSnapshotStore snapshotStore)
    {
        Free.Shared.AppServices.AppCrashHandlers.Register(
            recordCrash: (exception, source) => diagnostics.RecordCrash(exception, source),
            subscribeDispatcher: handler =>
                Current.DispatcherUnhandledException += (_, args) => handler(args.Exception),
            onAfterFault: () => TryEmergencySaveAllWindows(snapshotStore));
    }

    /// <summary>
    /// Best-effort emergency snapshot of all open dirty windows. Called from crash handlers.
    /// Must never throw.
    ///
    /// <para>
    /// AppDomain.UnhandledException fires on the faulting thread, which may not be the dispatcher
    /// thread. <c>Current.Windows</c> and the autosave service are UI-thread-affine and will throw
    /// from any other thread. We therefore marshal the work via <c>Dispatcher.Invoke</c> with a
    /// short bounded timeout. If the UI thread is itself wedged the marshal times out and the save
    /// silently does not happen — that is the correct best-effort outcome rather than crashing the
    /// crash handler.
    /// </para>
    /// </summary>
    private static void TryEmergencySaveAllWindows(AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            var dispatcher = Current?.Dispatcher;
            if (dispatcher is null)
                return;

            // If we are already on the dispatcher thread, execute inline; otherwise marshal with a
            // bounded timeout so a wedged UI thread does not block the faulting thread forever.
            if (dispatcher.CheckAccess())
            {
                TryEmergencySaveAllWindowsOnDispatcher(snapshotStore);
            }
            else
            {
                dispatcher.Invoke(
                    () => TryEmergencySaveAllWindowsOnDispatcher(snapshotStore),
                    System.Windows.Threading.DispatcherPriority.Send,
                    System.Threading.CancellationToken.None,
                    TimeSpan.FromSeconds(8));
            }
        }
        catch
        {
            // Outer guard — crash handlers must never throw.
        }
    }

    private static void TryEmergencySaveAllWindowsOnDispatcher(AutosaveSnapshotStore snapshotStore)
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
    ///
    /// <para>
    /// Multi-candidate behavior: each candidate is offered individually. The first accepted
    /// candidate is restored into <paramref name="mainWindow"/>. Subsequent accepted candidates
    /// are restored into new windows (one per candidate). Candidates that the user declines are
    /// deleted. The method never re-offers a declined candidate: once dismissed it is gone.
    /// This guarantees the loop always terminates and no snapshot is silently lost.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> if the user accepted at least one recovery; <c>false</c> otherwise.
    /// The caller uses this to decide whether a command-line file argument should be
    /// opened in a new window (to avoid overwriting the recovered workbook).
    /// </returns>
    private static bool OfferStartupRecovery(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            var candidates = snapshotStore.EnumerateCandidates();
            if (candidates.Count == 0)
                return false;

            var anyAccepted = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var displayName = candidate.Sidecar.DisplayName;

                // Build the prompt. When multiple candidates remain we mention how many are
                // outstanding so the user is not surprised by repeated dialogs.
                string prompt;
                var remaining = candidates.Count - i;
                if (remaining > 1)
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? UiText.Format("Startup_RecoveryPromptMultiple", remaining)
                        : UiText.Format("Startup_RecoveryPromptNamedMultiple", displayName, remaining);
                }
                else
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? UiText.Get("Startup_RecoveryPrompt")
                        : UiText.Format("Startup_RecoveryPromptNamed", displayName);
                }

                var accepted = AskStartupYesNo(
                    prompt,
                    UiText.Get("Startup_RecoveryTitle"));

                if (accepted)
                {
                    var capturedCandidate = candidate;
                    var restoreIntoMainWindow = !anyAccepted;
                    anyAccepted = true;

                    if (restoreIntoMainWindow)
                    {
                        // First accepted candidate: restore into the existing main window.
                        _ = mainWindow.Dispatcher.BeginInvoke(async () =>
                        {
                            try
                            {
                                await mainWindow.OpenRecoverySnapshotAsync(capturedCandidate.SnapshotPath);
                                mainWindow.SetCurrentFilePathForRecovery(capturedCandidate.Sidecar.OriginalFilePath);
                                mainWindow.MarkWorkbookDirtyForRecovery();
                                AutosaveSnapshotStore.DeleteCandidate(capturedCandidate);
                            }
                            catch
                            {
                                // If recovery load fails, clean up the bad snapshot.
                                AutosaveSnapshotStore.DeleteCandidate(capturedCandidate);
                            }
                        });
                    }
                    else
                    {
                        // Subsequent accepted candidates: open in new windows so we never
                        // overwrite an already-recovered workbook.
                        _ = mainWindow.Dispatcher.BeginInvoke(async () =>
                        {
                            try
                            {
                                var newWindow = App.Services.GetRequiredService<MainWindow>();
                                var autosaveStore = AutosaveSnapshotStore.CreateDefault(
                                    App.Services.GetRequiredService<IApplicationDataPathProvider>());
                                var autosaveSvc = new AutosaveService(autosaveStore);
                                newWindow.AttachAutosaveService(autosaveSvc, autosaveStore);
                                newWindow.Show();
                                newWindow.Activate();

                                await newWindow.OpenRecoverySnapshotAsync(capturedCandidate.SnapshotPath);
                                newWindow.SetCurrentFilePathForRecovery(capturedCandidate.Sidecar.OriginalFilePath);
                                newWindow.MarkWorkbookDirtyForRecovery();
                                AutosaveSnapshotStore.DeleteCandidate(capturedCandidate);
                            }
                            catch
                            {
                                AutosaveSnapshotStore.DeleteCandidate(capturedCandidate);
                            }
                        });
                    }
                }
                else
                {
                    // User declined this candidate — delete it so it is not re-offered on next launch.
                    try { AutosaveSnapshotStore.DeleteCandidate(candidate); } catch { /* best-effort */ }

                    // If there are more candidates ahead and this was not the last one, we will
                    // loop around and ask again. Each declined candidate is deleted immediately.
                }
            }

            return anyAccepted;
        }
        catch
        {
            // Startup recovery must never affect normal startup.
            return false;
        }
    }

    private static void PromptForCrashAnalyticsConsentIfNeeded(
        FreeXOptions options,
        AppCrashAnalyticsOptions crashAnalyticsOptions)
    {
        if (!CrashAnalyticsConsentPlanner.ShouldPrompt(options, crashAnalyticsOptions))
            return;

        var accepted = AskStartupYesNo(
            UiText.Get("Startup_CrashReportsConsentPrompt"),
            UiText.Get("Startup_CrashReportsTitle"));
        CrashAnalyticsConsentPlanner.ApplyConsent(options, accepted);
        options.Save();
    }

    private static bool AskStartupYesNo(string message, string title) =>
        StartupMessageService.ShowMessage(
            message,
            title,
            UserMessageButtons.YesNo,
            UserMessageIcon.Question) == UserMessageResult.Yes;

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.GetService<IAppDiagnostics>()?.RecordEvent("app_exit", new Dictionary<string, string?>
            {
                ["status"] = e.ApplicationExitCode.ToString()
            });
            Log.Information("FreeX shutting down");
            Log.CloseAndFlush();
            _services?.Dispose();
        }
        finally
        {
            _services = null;
            base.OnExit(e);
        }
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
