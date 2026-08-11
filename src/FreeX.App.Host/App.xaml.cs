using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
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

        if (TesterReleaseSmoke.TryRun(startupArgs, out var testerReleaseSmokeExitCode))
        {
            Shutdown(testerReleaseSmokeExitCode);
            return;
        }

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
        var options = FreeXOptions.Load();
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

        // UI. Every MainWindow resolved from DI gets its OWN document context — workbook,
        // WorkbookRef, command bus, and WorkbookDocumentState — so File > Open or File > New in
        // one window can never replace another window's document (H39). The only "same document,
        // several views" path is View > New Window, which bypasses this factory and constructs
        // the secondary window over the originating window's context (see ViewNewWindowBtn_Click).
        services.AddTransient(sp =>
        {
            var workbook = NewWorkbookFactory.Create(sp.GetRequiredService<FreeXOptions>());
            var workbookRef = new WorkbookRef { Current = workbook };
            return ActivatorUtilities.CreateInstance<MainWindow>(
                sp,
                CreateWorkbookCommandBus(workbookRef),
                workbookRef,
                workbook,
                new WorkbookDocumentState());
        });
    }

    /// <summary>
    /// Builds the command bus for one document context. The bus resolves its command context
    /// through <paramref name="workbookRef"/> on every dispatch, so it always targets that
    /// document's current workbook — and only that document's.
    /// </summary>
    internal static ICommandBus CreateWorkbookCommandBus(WorkbookRef workbookRef)
    {
        ArgumentNullException.ThrowIfNull(workbookRef);
        return new CommandBus(
            _ => new WorkbookCommandContext(workbookRef.Current),
            (id, ctx) => XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(ctx.Workbook, out _));
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
    /// Collapses recovery candidates that all belong to the same underlying document down to a
    /// single one, deleting the rest. A workbook shared across "New Window" views (View &gt; New
    /// Window) gets one autosave snapshot PER WINDOW (see MainWindow.MultiWindow.cs's
    /// ViewNewWindowBtn_Click / AttachAutosaveService — autosave ownership is per-window, not
    /// per-document, so crash-recovery coverage survives closing any single sibling). If the
    /// process crashes, that leaves multiple snapshot files with the same
    /// <see cref="AutosaveSidecar.OriginalFilePath"/>/<see cref="AutosaveSidecar.DisplayName"/> on
    /// disk for what was really one shared, dirtied document. Without this step,
    /// <see cref="OfferStartupRecovery"/> would offer each one individually, and accepting more
    /// than one would load the same document into two independent windows with disconnected
    /// WorkbookRefs (see MainWindow.MultiWindow.cs's AdoptSharedWorkbook, which only reconnects a
    /// secondary window constructed directly over an existing WorkbookRef — never a freshly
    /// deserialized recovery snapshot) — edits in one would silently stop reaching the other.
    /// Recovery only ever needs to restore ONE copy of a document, so we keep just the newest
    /// snapshot per document identity (by <see cref="AutosaveSidecar.TimestampUtc"/>, falling back
    /// to the file's last-write time when the sidecar timestamp is missing/unparseable) and delete
    /// its siblings up front — mirroring how File &gt; Open already scopes one document per window.
    /// </summary>
    private static IReadOnlyList<AutosaveRecoveryCandidate> DeduplicateCandidatesByDocument(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        if (candidates.Count <= 1)
            return candidates;

        var newestByDocument = new Dictionary<string, AutosaveRecoveryCandidate>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var candidate in candidates)
        {
            var documentKey = GetDocumentIdentityKey(candidate);
            if (!newestByDocument.TryGetValue(documentKey, out var existing))
            {
                newestByDocument[documentKey] = candidate;
                ordered.Add(documentKey);
                continue;
            }

            if (GetCandidateTimestamp(candidate) > GetCandidateTimestamp(existing))
            {
                AutosaveSnapshotStore.DeleteCandidate(existing);
                newestByDocument[documentKey] = candidate;
            }
            else
            {
                AutosaveSnapshotStore.DeleteCandidate(candidate);
            }
        }

        return ordered.Select(key => newestByDocument[key]).ToList();
    }

    /// <summary>
    /// Identity key grouping candidates that are recovery snapshots of the same document.
    /// A saved workbook is keyed by its original file path (case-insensitive, matching Windows
    /// path semantics) — a real path is a stable identity for the document itself. But the same
    /// path can legitimately have unrecovered snapshots from two DIFFERENT crashed sessions (e.g.
    /// the file was open with unsaved edits when session A crashed, then reopened and edited
    /// differently before session B also crashed, and neither snapshot was ever offered/recovered
    /// in between) — those hold different unsaved edits and must not be silently collapsed to
    /// "keep the newer, delete the older" (R16: that would destroy session A's edits with zero
    /// content comparison). We therefore scope the path-based key to the originating session the
    /// same way the name-based key already is below (see M9), by also keying on the
    /// "recovery-{processId}-{launchTag}-" prefix embedded in every snapshot's file name (see
    /// MainWindow.Autosave.cs's AttachAutosaveService).
    /// <para>
    /// Same launch scope + same path is still NOT sufficient proof of a genuine "New Window"
    /// sibling pair, though: File &gt; Open has no "already open elsewhere" check (see
    /// MainWindow.Backstage.cs's OpenFileAsync), so the SAME running process can just as easily
    /// have two ordinary, fully INDEPENDENT windows over the same saved path (opened via two
    /// separate File &gt; Open actions), each with its own unrelated unsaved edits. Blindly merging
    /// on launch scope + path alone would silently destroy one of those windows' edits with zero
    /// content comparison — R82-services-autosave-recovery-5-1. We therefore also require the
    /// sidecar's <see cref="AutosaveSidecar.DocumentId"/> (populated from
    /// <c>IAutosaveWorkbookSource.DocumentId</c>, i.e. the in-memory <c>Workbook.Id</c>) to match:
    /// genuine "New Window" siblings share the exact same Workbook instance and therefore the same
    /// DocumentId, while two independent windows each get their own freshly created/deserialized
    /// Workbook and therefore different DocumentIds. Candidates whose DocumentId is missing (e.g. a
    /// snapshot written by a build that predates this field) are never treated as provably the same
    /// document — see <see cref="GetDocumentIdentityComponent"/>.
    /// </para>
    /// <para>
    /// An unsaved workbook has no file path. Its <see cref="AutosaveSidecar.DisplayName"/> is
    /// almost always the compile-time constant <c>WorkbookFactory.DefaultWorkbookName</c>
    /// ("Book1") for every never-touched fresh launch, so display name alone is NOT a reliable
    /// document identity — two unrelated crashed processes that both still had their untouched
    /// default workbook would otherwise collide on "name:Book1" and one would be silently deleted
    /// (see M9). We therefore scope the name-based key to the originating session and DocumentId
    /// the same way as the path-based key above.
    /// </para>
    /// <para>
    /// Candidates that have neither a path nor a name (should not normally happen) each get their
    /// own unique key so they are never incorrectly merged with an unrelated candidate.
    /// </para>
    /// </summary>
    private static string GetDocumentIdentityKey(AutosaveRecoveryCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Sidecar.OriginalFilePath))
        {
            return "path:" + GetLaunchScope(candidate) + ":" + candidate.Sidecar.OriginalFilePath
                + ":" + GetDocumentIdentityComponent(candidate);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Sidecar.DisplayName))
        {
            return "name:" + GetLaunchScope(candidate) + ":" + candidate.Sidecar.DisplayName
                + ":" + GetDocumentIdentityComponent(candidate);
        }

        return "snapshot:" + candidate.SnapshotPath;
    }

    /// <summary>
    /// The DocumentId contribution to <see cref="GetDocumentIdentityKey"/>. When the sidecar
    /// carries a <see cref="AutosaveSidecar.DocumentId"/> (every snapshot written by this build
    /// does), it is authoritative proof of whether two candidates share the same underlying
    /// Workbook instance. When it is missing (a snapshot from a build predating this field), the
    /// candidate's own snapshot path is used instead so it is NEVER treated as provably the same
    /// document as another candidate and silently merged/deleted — better to keep (and offer) an
    /// extra candidate than to silently destroy an unrelated window's unsaved edits.
    /// </summary>
    private static string GetDocumentIdentityComponent(AutosaveRecoveryCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Sidecar.DocumentId)
            ? "unknown:" + candidate.SnapshotPath
            : candidate.Sidecar.DocumentId;

    /// <summary>
    /// Extracts the "{processId}-{launchTag}" scope from a snapshot's file name, e.g.
    /// "recovery-12345-a1b2c3d4-e5f6a7b8.fxl" -&gt; "12345-a1b2c3d4". This identifies the
    /// originating process launch (not the individual window within it), so sibling windows of
    /// the same crashed session still share a scope and can be deduplicated, while two different
    /// processes/launches never do. Also accepts the shorter "recovery-{processId}-{windowTag}"
    /// form (no separate launch tag segment) and scopes by process id alone in that case — this
    /// keeps same-process "New Window" siblings mergeable even when a snapshot name omits the
    /// launch tag. Falls back to the full snapshot path when the file name does not match either
    /// expected pattern, so a truly unrecognized name is always treated as its own distinct scope
    /// rather than accidentally merged with anything else.
    /// </summary>
    private static string GetLaunchScope(AutosaveRecoveryCandidate candidate)
    {
        var baseName = Path.GetFileNameWithoutExtension(candidate.SnapshotPath);
        var parts = baseName.Split('-');
        if (!string.Equals(parts.Length > 0 ? parts[0] : null, "recovery", StringComparison.OrdinalIgnoreCase))
            return candidate.SnapshotPath;

        if (parts.Length >= 4)
            return parts[1] + "-" + parts[2];
        if (parts.Length == 3)
            return parts[1];

        return candidate.SnapshotPath;
    }

    private static DateTimeOffset GetCandidateTimestamp(AutosaveRecoveryCandidate candidate)
    {
        if (DateTimeOffset.TryParse(candidate.Sidecar.TimestampUtc, out var parsed))
            return parsed;

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(candidate.SnapshotPath), TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Formats a candidate's autosave timestamp for display in the startup recovery prompt
    /// (R82-services-autosave-recovery-5-3: Excel's Document Recovery pane always shows "last
    /// autosaved at HH:MM" next to each recovered file; FreeX's prompt previously never surfaced
    /// this at all, leaving the user unable to judge how fresh/stale an offered snapshot is, or to
    /// tell two same-named candidates apart by recency). Reuses
    /// <see cref="GetCandidateTimestamp"/>'s parse-with-fallback-to-file-mtime logic so the
    /// displayed value matches exactly what dedup/ordering already use, converts it to local time
    /// (the sidecar stores UTC), and formats it with the current UI culture so the punctuation and
    /// ordering match the rest of the localized prompt.
    /// </summary>
    private static string FormatRecoveryTimestampForDisplay(AutosaveRecoveryCandidate candidate) =>
        GetCandidateTimestamp(candidate).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Drops any candidate whose ORIGINAL on-disk file was saved more recently than the crash
    /// snapshot itself (R74-services-autosave-recovery-4-1). This happens when the user saved the
    /// document normally after the crash that produced the snapshot (e.g. reopened the file by
    /// hand and saved over it, or another window/session saved it) — offering that snapshot would
    /// let the user unknowingly clobber their own newer manual save with stale recovered content.
    /// Excel never offers recovery in this situation. A candidate whose original file is missing
    /// (never saved, moved, or deleted) or whose on-disk timestamp is older than or equal to the
    /// snapshot is unaffected and still offered exactly as before. Filtered candidates are deleted
    /// outright rather than left on disk to be silently re-checked (and re-skipped) forever —
    /// their content is already superseded by the newer file on disk, so nothing of value would be
    /// recovered by keeping them.
    /// </summary>
    private static IReadOnlyList<AutosaveRecoveryCandidate> FilterCandidatesWithNewerOriginal(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        if (candidates.Count == 0)
            return candidates;

        List<AutosaveRecoveryCandidate>? kept = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (IsOriginalNewerThanSnapshot(candidate))
            {
                kept ??= new List<AutosaveRecoveryCandidate>(candidates.Take(i));
                try { AutosaveSnapshotStore.DeleteCandidate(candidate); } catch { /* best-effort */ }
                continue;
            }

            kept?.Add(candidate);
        }

        return kept ?? candidates;
    }

    private static bool IsOriginalNewerThanSnapshot(AutosaveRecoveryCandidate candidate)
    {
        var originalPath = candidate.Sidecar.OriginalFilePath;
        if (string.IsNullOrWhiteSpace(originalPath))
            return false;

        try
        {
            if (!File.Exists(originalPath))
                return false;

            var originalWriteTimeUtc = File.GetLastWriteTimeUtc(originalPath);
            return originalWriteTimeUtc > GetCandidateTimestamp(candidate).UtcDateTime;
        }
        catch
        {
            // If the original's timestamp cannot be determined, do not block recovery on it.
            return false;
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
    /// Candidates are deduplicated by document identity before being offered (see
    /// <see cref="DeduplicateCandidatesByDocument"/>), so a workbook that was shared across
    /// multiple "New Window" views before the crash is only ever offered — and recovered — once
    /// (K4). Candidates whose original on-disk file was saved more recently than the snapshot are
    /// then dropped (see <see cref="FilterCandidatesWithNewerOriginal"/>), so recovery never
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
        try
        {
            // Round134-remediation (family fix): this process's OWN just-started coordinator has
            // no snapshot file to protect yet at this point, but a SECOND FreeX.exe process
            // launched while a FIRST one is still open with unsaved edits would otherwise see and
            // could offer/delete the first process's still-live snapshot — the same "list/delete
            // another live window's snapshot" defect the WPF host's AutosaveCoordinator had.
            // ExcludeLiveOwned filters those out via the shared OS-lock liveness check.
            var candidates = FilterCandidatesWithNewerOriginal(
                DeduplicateCandidatesByDocument(snapshotStore.ExcludeLiveOwned(snapshotStore.EnumerateCandidates())));
            if (candidates.Count == 0)
                return false;

            var anyAccepted = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var displayName = candidate.Sidecar.DisplayName;
                var timestampText = FormatRecoveryTimestampForDisplay(candidate);

                // Build the prompt. When multiple candidates remain we mention how many are
                // outstanding so the user is not surprised by repeated dialogs. Every variant
                // also carries the autosave timestamp (R82-services-autosave-recovery-5-3) so the
                // user can judge how fresh/stale the offered snapshot is before deciding — the
                // extra format argument is appended after the existing ones, so a satellite
                // translation that has not yet picked up the new placeholder still formats
                // correctly (unused trailing args are not an error for string.Format).
                string prompt;
                var remaining = candidates.Count - i;
                if (remaining > 1)
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? UiText.Format("Startup_RecoveryPromptMultiple", remaining, timestampText)
                        : UiText.Format("Startup_RecoveryPromptNamedMultiple", displayName, remaining, timestampText);
                }
                else
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? UiText.Format("Startup_RecoveryPrompt", timestampText)
                        : UiText.Format("Startup_RecoveryPromptNamed", displayName, timestampText);
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

/// <summary>
/// Mutable holder for one document context's active workbook, updated on file open. Per window —
/// except that the views of one document created via View &gt; New Window share one instance —
/// so repointing it never affects windows over other documents (H39).
/// </summary>
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
