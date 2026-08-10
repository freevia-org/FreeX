using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using FreeX.App.Services;
using FreeX.Core.Calc;
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

    private static AppOptions? _startupOptions;

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

        if (TesterReleaseSmoke.TryRun(startupArgs, out var testerReleaseSmokeExitCode))
        {
            Shutdown(testerReleaseSmokeExitCode);
            return;
        }

        // Always apply the active brand theme early — before the main window loads — so that
        // DynamicResource references in the title-bar chrome pick up the correct brushes.
        // For the DEFAULT (FreeX) theme the values are BYTE-IDENTICAL to ThemeResources.xaml,
        // so the visual result is unchanged.  FREEX_THEME=midnight swaps in the alternate palette.
        FreeXApplicationStartupDescriptor.Theme.Apply(
            System.Environment.GetEnvironmentVariable,
            theme => ActiveTheme = theme,
            (theme, resourceKeyPrefix) => WpfThemeApplier.Apply(this, theme, resourceKeyPrefix));

        // Keep the SystemColors.*Brush overrides in App.Resources (see App.xaml) in sync with a
        // LIVE toggle of Windows High Contrast (or any other OS theme change) while FreeX is
        // already running. Those overrides are plain <SolidColorBrush Color="{x:Static
        // sys:SystemColors.XxxColor}"/> entries: the Color is read once at XAML-parse time and
        // then frozen, so without this handler a user who turns HC on/off after launch would keep
        // seeing the colors captured at startup instead of the OS's new palette.
        // SystemParameters.StaticPropertyChanged fires for every SystemParameters/SystemColors
        // property WPF is tracking, including HighContrast and the individual *Color properties
        // it derives from — so re-applying our brushes on every notification keeps them current
        // without needing to special-case the raised property.
        SystemParameters.StaticPropertyChanged += (_, _) => RefreshSystemColorsBrushOverrides();

        // Velopack is invoked earlier, from Program.Main, before the WPF Application is created,
        // so install/update/uninstall hooks are serviced before any UI initializes.
        var options = AppOptionsStore.Load();
        AppLocalization.Bootstrap.InstallSharedSeams();
        AppLocalization.Bootstrap.ApplyAppLanguage(options.AppLanguage);
        AppLocalization.Bootstrap.ApplyCurrentCultureToWpf();
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

        // The grid's render pass catches content-driven faults rather than letting them escape
        // OnRender (which WPF treats as fatal, and which would then recur on every repaint). Route
        // those caught faults into the same crash pipeline so they are still recorded and tracked
        // instead of silently swallowed.
        FreeX.App.UI.GridRenderFaultReporter.Handler = (exception, stage) =>
            diagnostics.RecordCrash(exception, stage);

        // Same idea for ribbon/menu commands: the shared renderer contains what a command throws
        // rather than letting it escape a Click handler, since DispatcherUnhandledException above
        // records the fault without marking it handled and the app would otherwise terminate.
        Free.Shared.Ribbon.RibbonCommandFaultReporter.Handler = (exception, commandId) =>
            diagnostics.RecordCrash(exception, "ribbon_command:" + commandId);

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

        // R118: launching FreeX.exe with more than one file argument (or dragging multiple
        // workbook files onto the taskbar icon, which Windows delivers as a single process launch
        // with multiple path arguments) used to open only the FIRST existing file and silently
        // drop every subsequent one -- the loop `break`d right after scheduling the first hit. Real
        // Excel opens every file argument, each in its own window. PlanStartupFileOpens (a pure,
        // independently-testable seam) decides which argument opens in the already-visible main
        // window and which open in their own new window; this loop just carries out that plan.
        var startupFilePlan = PlanStartupFileOpens(startupArgs, File.Exists, recoveryAccepted, out var firstMissingStartupPath);
        var openedStartupFile = startupFilePlan.Count > 0;
        foreach (var entry in startupFilePlan)
        {
            var pathToOpen = entry.Path;
            if (entry.OpenInNewWindow)
            {
                // Either the main window already holds a recovered workbook (recoveryAccepted), or
                // this is the second-or-later file argument and the main window is already spoken
                // for by the first one. Opening this argument in the same window would prompt
                // "Save changes?" on whatever it already holds, and a "No" answer would silently
                // discard it. Open the file-arg in a new window to keep every workbook alive.
                _ = mainWindow.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var newWindow = App.Services.GetRequiredService<MainWindow>();
                        newWindow.Show();
                        newWindow.Activate();
                        _ = newWindow.Dispatcher.BeginInvoke(async () =>
                            await newWindow.OpenStartupFileAsync(pathToOpen));
                    }
                    catch
                    {
                        // Best-effort: if we can't open the file-arg in a new window, skip it.
                        // The user can open it manually; we must not discard another window's workbook.
                    }
                });
            }
            else
            {
                _ = mainWindow.Dispatcher.BeginInvoke(async () =>
                    await mainWindow.OpenStartupFileAsync(pathToOpen));
            }
        }

        // No command-line argument resolved to an openable file (and none of them triggered the
        // crash-recovery / normal-open branches above) -- tell the user instead of quietly showing
        // a blank Book1 as if launched with no arguments at all.
        if (!openedStartupFile && firstMissingStartupPath is not null)
        {
            _ = mainWindow.Dispatcher.BeginInvoke(() =>
                mainWindow.ReportStartupFileNotFound(firstMissingStartupPath));
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

    /// <summary>
    /// Re-applies the SystemColors.*Brush overrides declared in App.xaml using the CURRENT OS
    /// colors, so a live toggle of Windows High Contrast (or a regular theme/accent-color change)
    /// while FreeX is already running is picked up immediately -- not just at process start.
    ///
    /// <para>
    /// App.xaml intentionally overrides these specific SystemColors brush keys (rather than
    /// leaving WPF's own dynamic resolution in place) so unstyled default control templates and
    /// FreeX dialogs that bind {DynamicResource {x:Static SystemColors.XxxBrushKey}} keep
    /// resolving through the SAME resource entry across a live change: <c>DynamicResource</c>
    /// consumers re-pull the value from the dictionary whenever the resource identified by that
    /// key is replaced, so swapping the <see cref="SolidColorBrush"/> instance in
    /// <see cref="Application.Resources"/> is enough to push the new color out to every open
    /// window/dialog without needing to touch each consumer.
    /// </para>
    /// </summary>
    private void RefreshSystemColorsBrushOverrides()
    {
        var resources = Resources;
        resources[SystemColors.WindowBrushKey] = new SolidColorBrush(SystemColors.WindowColor);
        resources[SystemColors.ControlBrushKey] = new SolidColorBrush(SystemColors.ControlColor);
        resources[SystemColors.MenuBrushKey] = new SolidColorBrush(SystemColors.MenuColor);
        resources[SystemColors.MenuBarBrushKey] = new SolidColorBrush(SystemColors.MenuBarColor);
        resources[SystemColors.ControlLightBrushKey] = new SolidColorBrush(SystemColors.ControlLightColor);
        resources[SystemColors.ControlLightLightBrushKey] = new SolidColorBrush(SystemColors.ControlLightLightColor);
        resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush(SystemColors.ControlTextColor);
        resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(SystemColors.MenuTextColor);
    }

    private static IReadOnlyList<string> GetStartupArgs(StartupEventArgs e)
    {
        if (e.Args.Length > 0)
            return e.Args;

        return Environment.GetCommandLineArgs().Skip(1).ToArray();
    }

    /// <summary>
    /// One command-line/drag startup file argument that resolved to an existing file, and where it
    /// should be opened: <c>false</c> for the single argument that can share the already-visible
    /// main window, <c>true</c> for every argument that needs its own new window.
    /// </summary>
    internal readonly record struct StartupFileOpenPlan(string Path, bool OpenInNewWindow);

    /// <summary>
    /// Decides, for a full set of startup file arguments, which existing file (if any) opens in the
    /// already-visible main window and which open in their own new window (R118: launching FreeX
    /// with multiple file arguments -- or dragging multiple files onto the taskbar icon -- must open
    /// every one of them, each in its own window, the way real Excel does, instead of silently
    /// dropping every argument after the first). Pure and side-effect free so it can be unit tested
    /// directly; <see cref="App_OnStartup"/> is the sole real caller and just carries out the plan.
    /// </summary>
    /// <param name="startupArgs">The raw startup arguments, in the order they were supplied.</param>
    /// <param name="fileExists">Existence check (injected so tests don't need real files on disk).</param>
    /// <param name="recoveryAccepted">
    /// True when the main window already hosts a just-recovered crash snapshot, so even the FIRST
    /// existing file argument must not reuse that window (it would prompt "Save changes?" on the
    /// recovered workbook, and a "No" answer would silently discard it) and instead opens in its own
    /// new window like every subsequent argument.
    /// </param>
    /// <param name="firstMissingPath">
    /// The first argument that failed <paramref name="fileExists"/> (a typo'd path, a directory, a
    /// URL, ...), or null if every argument resolved to an existing file. Used by the caller to warn
    /// the user only when NO argument opened anything at all.
    /// </param>
    internal static IReadOnlyList<StartupFileOpenPlan> PlanStartupFileOpens(
        IEnumerable<string> startupArgs,
        Func<string, bool> fileExists,
        bool recoveryAccepted,
        out string? firstMissingPath)
    {
        var plans = new List<StartupFileOpenPlan>();
        string? firstMissing = null;
        var isFirstOpenableFile = true;
        foreach (var path in startupArgs)
        {
            if (!fileExists(path))
            {
                firstMissing ??= path;
                continue;
            }

            plans.Add(new StartupFileOpenPlan(path, recoveryAccepted || !isFirstOpenableFile));
            isFirstOpenableFile = false;
        }

        firstMissingPath = firstMissing;
        return plans;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        var options = _startupOptions ?? AppOptionsStore.Load();
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

        // Message service
        services.AddSingleton<IUserMessageService, WpfUserMessageService>();

        // Multi-window registry: tracks every live workbook window in the process, across all
        // open documents. Singleton so all windows coordinate through one registry; every
        // notify/refresh/numbering decision inside it is scoped per document (H39).
        services.AddSingleton<WorkbookWindowRegistry>();

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

        // UI. Every MainWindow resolved from DI gets its own workbook document context and state,
        // so File > Open or File > New in
        // one window can never replace another window's document (H39). The only "same document,
        // several views" path is View > New Window, which bypasses this factory and constructs
        // the secondary window over the originating window's context (see ViewNewWindowBtn_Click).
        services.AddTransient(sp =>
        {
            var workbook = WorkbookFactory.CreateFromAppOptions(sp.GetRequiredService<AppOptions>());
            var documentContext = WorkbookDocumentContext.Create(workbook);
            return ActivatorUtilities.CreateInstance<MainWindow>(
                sp,
                documentContext,
                new WorkbookDocumentState());
        });
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
    /// <para>
    /// Candidates are deduplicated by document identity before being offered by
    /// <see cref="AutosaveRecoveryCandidateProcessor"/>, so a workbook that was shared across
    /// multiple "New Window" views before the crash is only ever offered — and recovered — once
    /// (K4). Candidates whose original on-disk file was saved more recently than the snapshot are
    /// then dropped by the same processor, so recovery never
    /// offers to overwrite a newer manual save with stale snapshot content (R74-4-1).
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> if the user accepted at least one recovery; <c>false</c> otherwise.
    /// The caller uses this to decide whether a command-line file argument should be
    /// opened in a new window (to avoid overwriting the recovered workbook).
    /// </returns>
    private static bool OfferStartupRecovery(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        var host = new StartupRecoveryWorkflowHost<MainWindow>(
            PrimaryTarget: mainWindow,
            OfferAsync: (offer, _) => ValueTask.FromResult(AskStartupYesNo(
                UiText.Format(offer.PromptKey, offer.PromptArguments),
                UiText.Get(offer.TitleKey))),
            CreateAdditionalTargetAsync: _ => ValueTask.FromResult(OpenRecoveryWindow()),
            RestoreAsync: async (target, candidate, _) =>
            {
                await target.OpenRecoverySnapshotAsync(candidate.SnapshotPath);
                target.SetCurrentFilePathForRecovery(candidate.Sidecar.OriginalFilePath);
                target.MarkWorkbookDirtyForRecovery();
            },
            ExecuteRestoreAsync: (operation, _) =>
            {
                mainWindow.Dispatcher.BeginInvoke(async () => await operation());
                return ValueTask.CompletedTask;
            },
            DeleteCandidate: AutosaveSnapshotStore.DeleteCandidate);

        return StartupRecoveryWorkflow.RunAsync(snapshotStore.EnumerateCandidates(), host)
            .GetAwaiter()
            .GetResult();
    }

    private static MainWindow OpenRecoveryWindow()
    {
        var newWindow = Services.GetRequiredService<MainWindow>();
        var autosaveStore = AutosaveSnapshotStore.CreateDefault(
            Services.GetRequiredService<IApplicationDataPathProvider>());
        var autosaveService = new AutosaveService(autosaveStore);
        newWindow.AttachAutosaveService(autosaveService, autosaveStore);
        newWindow.Show();
        newWindow.Activate();
        return newWindow;
    }

    private static void PromptForCrashAnalyticsConsentIfNeeded(
        AppOptions options,
        AppCrashAnalyticsOptions crashAnalyticsOptions)
    {
        if (!CrashAnalyticsConsentWorkflowPlanner.ShouldPrompt(
                options.CrashAnalyticsPrompted,
                crashAnalyticsOptions.Dsn,
                crashAnalyticsOptions.IsDisabledByEnvironment))
            return;

        var accepted = AskStartupYesNo(
            UiText.Get("Startup_CrashReportsConsentPrompt"),
            UiText.Get("Startup_CrashReportsTitle"));
        options.CrashAnalyticsEnabled = accepted;
        options.CrashAnalyticsPrompted = true;
        AppOptionsStore.Save(options);
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
