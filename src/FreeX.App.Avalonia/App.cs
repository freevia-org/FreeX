using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using FreeX.App.Services;
using FreeX.App.Services.Updates;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.Shared.AppServices;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia;

public sealed class App : Application
{
    private const string ApplicationTitle = "FreeX";

    public static IReadOnlyList<string> StartupArguments { get; set; } = [];

    internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }

    internal static ParityCaptureOptions? ParityCaptureOptions { get; set; }

    internal static GridCaptureOptions? GridCaptureOptions { get; set; }

    internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }

    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeX"/>).
    /// Stored so that tests and diagnostics can verify the selected palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeX;

    public override void OnFrameworkInitializationCompleted()
    {
        // Install FreeX's product identity before any shared storage helper resolves a path
        // (settings, recent files, autosave, diagnostics) — mirrors the WPF host/Program.cs's
        // Main(), which sets this before WPF's App runs. Without it, AppProduct.Current stays at
        // its neutral "FreeApp" default and autosave/recovery below would read/write under the
        // wrong app-data folder. This must run before `new MainWindow(...)` below, since
        // MainWindow's field initializers (e.g. RecentFilesStore.Load()) already read storage
        // paths derived from AppProduct.Current.
        AppProduct.Current = new AppProductIdentity("FreeX", "FREEX_DIAGNOSTICS", "FreeX");

        Name = ApplicationTitle;
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());

        // App-wide compact dialog control styles: override Avalonia Fluent's oversized defaults for
        // CheckBox, RadioButton, TabItem, and ListBox/ListBoxItem to match the WPF shell's compact look.
        // Applied AFTER FluentTheme so they override its defaults.  The ribbon's own TabControl.Styles
        // block (AvaloniaRibbonRenderer.ApplyRibbonTheme) is a local style collection with higher
        // priority than application-level styles, so it is unaffected by this block.
        foreach (var style in DialogControlStyles.Build())
            Styles.Add(style);

        // Select the active brand theme and register token brushes into Application.Resources
        // so that chrome surfaces can look them up by key.  FREEX_THEME=midnight swaps in the
        // alternate palette; otherwise the default FreeX palette applies (values are identical
        // to the existing inline colors so the default appearance is unchanged).
        var theme = string.Equals(
            System.Environment.GetEnvironmentVariable("FREEX_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeX;
        ActiveTheme = theme;
        var themeResources = AvaloniaThemeApplier.BuildResources(theme, "FreeX");
        Resources.MergedDictionaries.Add(themeResources);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(StartupArguments);
            desktop.MainWindow = mainWindow;
            Diagnostics?.RecordEvent("app_ready", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "app",
                ["status"] = "ready"
            });

            // Self-update is best-effort: when the app is not Velopack-installed the service
            // degrades to Unavailable and the indicator simply never appears.
            var updateService = VelopackUpdateService.CreateForGitHub(
                repoUrl: UpdateFeed.GitHubRepoUrl,
                prerelease: UpdateFeed.AllowPrereleases(AppHelpInfo.ReleaseChannel),
                releasesPageUrl: AppHelpInfo.LatestReleaseUrl);
            mainWindow.AttachUpdateService(updateService);

            // Autosave / crash-recovery: mirrors the WPF host/App.xaml.cs's snapshot-store +
            // crash-handler + periodic-timer wiring so a Linux/macOS session gets the same
            // data-loss protection as Windows: periodic best-effort snapshotting of the live
            // session while dirty, an emergency snapshot on crash, startup recovery of any
            // snapshot left behind by a crashed previous launch, and snapshot cleanup on both a
            // clean save and a normal window close.
            var snapshotStore = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
            var autosaveCoordinator = new AvaloniaAutosaveCoordinator(mainWindow, snapshotStore);
            mainWindow.AttachAutosaveCoordinator(autosaveCoordinator);
            AppCrashHandlers.Register(
                recordCrash: (exception, source) => Diagnostics?.RecordCrash(exception, source),
                subscribeDispatcher: null,
                onAfterFault: autosaveCoordinator.TryEmergencySnapshot);
            autosaveCoordinator.Start();
            mainWindow.Closed += (_, _) => autosaveCoordinator.OnWindowClosed();

            // Startup recovery must run after the window is visible (it hosts the confirmation
            // dialog), so defer it to the next UI-thread dispatch rather than running inline here.
            Dispatcher.UIThread.Post(() => _ = OfferStartupRecoveryAsync(mainWindow, snapshotStore));

            if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
                activatableLifetime.Activated += (_, args) => _ = OnActivatedAsync(mainWindow, args);

            if (LaunchSmokeOptions is { } launchSmokeOptions)
                MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);

            if (ParityCaptureOptions is { } parityCaptureOptions)
                ParityCaptureCoordinator.Start(mainWindow, parityCaptureOptions, Diagnostics);

            if (GridCaptureOptions is { } gridCaptureOptions)
                GridCaptureCoordinator.Start(mainWindow, gridCaptureOptions, Diagnostics);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Wraps the fire-and-forget activation handler so an exception cannot escape an async-void
    // event handler and tear the process down; failures are routed to diagnostics instead.
    private static async Task OnActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
    {
        try
        {
            await MainWindow_ActivatedAsync(mainWindow, args);
        }
        catch (Exception ex)
        {
            Diagnostics?.RecordEvent("activation_failed", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "activation",
                ["status"] = "error",
                ["reason"] = ex.GetType().Name
            });
        }
    }

    private static async Task MainWindow_ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
    {
        if (args is not FileActivatedEventArgs fileArgs ||
            fileArgs.Kind != ActivationKind.File)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
        await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
    }

    /// <summary>
    /// Checks for a crash-recovery snapshot from a previous (now-dead) session and, if the user
    /// accepts, loads it into <paramref name="mainWindow"/>. Mirrors
    /// <c>the WPF host.App.xaml.cs</c>'s <c>OfferStartupRecovery</c>, simplified for this shell's
    /// single-MainWindow-at-startup shape: only the single newest candidate is offered into the
    /// already-shown main window (WPF's multi-candidate/"open extra candidates in new windows"
    /// behavior is not replicated here — Avalonia's <c>MainWindow.WindowManagement.cs</c>, which
    /// would host that, is owned by a different change in this pass). Declined or unreadable
    /// candidates are deleted so they are never re-offered. Must never throw — startup recovery is
    /// best-effort and must not affect normal startup.
    /// </summary>
    private static async Task OfferStartupRecoveryAsync(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            var candidates = snapshotStore.EnumerateCandidates();
            if (candidates.Count == 0)
                return;

            // Newest first (by sidecar timestamp, falling back to file write time), matching the
            // WPF host's dedup-then-offer-newest-first ordering.
            var ordered = candidates
                .OrderByDescending(GetCandidateTimestamp)
                .ToList();

            // This process's OWN just-started coordinator has not written a snapshot yet at this
            // point in startup, so every candidate here necessarily belongs to a previous launch —
            // no self-recovery filtering is needed the way WPF's multi-window dedup requires.
            var newest = ordered[0];
            for (var i = 1; i < ordered.Count; i++)
            {
                try { AutosaveSnapshotStore.DeleteCandidate(ordered[i]); } catch { /* best-effort */ }
            }

            var displayName = newest.Sidecar.DisplayName;
            var prompt = string.IsNullOrWhiteSpace(displayName)
                ? "FreeX found unsaved changes from a previous session. Recover them?"
                : $"FreeX found unsaved changes to \"{displayName}\" from a previous session. Recover them?";

            var accepted = await mainWindow.ShowRecoveryPromptAsync(prompt, "Recover Unsaved Workbook");
            if (accepted)
            {
                // A load failure (corrupt/unreadable snapshot) leaves the just-shown default
                // workbook untouched — there is nothing else to do; the bad snapshot is deleted
                // below either way so it is never re-offered.
                await mainWindow.LoadRecoverySnapshotAsync(newest.SnapshotPath, newest.Sidecar.OriginalFilePath);
            }

            try { AutosaveSnapshotStore.DeleteCandidate(newest); } catch { /* best-effort */ }
        }
        catch
        {
            // Startup recovery must never affect normal startup.
        }
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
}

/// <summary>
/// Avalonia-shell autosave / crash-recovery wiring, built from <see cref="App"/> against
/// <see cref="MainWindow"/>'s public <c>Session</c> accessor plus its
/// <c>LoadRecoverySnapshotAsync</c>/<c>ShowRecoveryPromptAsync</c> hooks — mirrors the neutral
/// orchestration <see cref="AutosaveSnapshotCoordinator"/> that <c>the WPF host.App.xaml.cs</c>
/// and <c>the WPF host.MainWindow.Autosave.cs</c> already wire on Windows, and the pattern
/// <c>FreeW.App.Avalonia.AutosaveAdapter</c> uses for FreeW's Avalonia shell. Periodic snapshotting
/// while dirty, an emergency snapshot on crash, startup recovery (via
/// <see cref="App.OfferStartupRecoveryAsync"/>), and snapshot cleanup on both a clean save
/// (<see cref="NotifyAutosaveSaved"/>) and a normal window close (<see cref="OnWindowClosed"/>) are
/// all wired.
///
/// <para>
/// <b>Known gap:</b> <c>LoadRecoverySnapshotAsync</c> loads the recovered workbook into a fresh
/// <c>WorkbookSession</c> (via <c>WorkbookSessionFactory.Create</c>), which starts
/// <c>IsDirty == false</c> — <c>WorkbookSession</c> has no public API to force it dirty, and
/// <c>WorkbookSession.cs</c> is out of scope for this change (owned by a different pass). The
/// recovered content is fully loaded and saveable either way; the only user-visible gap is that
/// the modified indicator does not light up and closing the window without an explicit Save will
/// not prompt, until a small public hook (analogous to WPF's <c>MarkWorkbookDirtyForRecovery</c>)
/// lands on <c>WorkbookSession</c>.
/// </para>
/// </summary>
internal sealed class AvaloniaAutosaveCoordinator
{
    // Matches the WPF host's DispatcherTimer interval (see AutosaveService.DefaultInterval).
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly MainWindow _mainWindow;
    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly NativeJsonAdapter _adapter = new();
    private DispatcherTimer? _timer;

    public AvaloniaAutosaveCoordinator(MainWindow mainWindow, AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(store);

        _mainWindow = mainWindow;
        _store = store;

        // One snapshot per process launch — this shell only ever has a single MainWindow, so
        // (unlike WPF's per-window registry) there is no need for a per-window discriminator.
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        _coordinator = new AutosaveSnapshotCoordinator(_store, $"recovery-{Environment.ProcessId}-{launchTag}");
    }

    /// <summary>Starts the periodic autosave timer. Safe to call once, on the UI thread.</summary>
    public void Start()
    {
        if (_timer is not null)
            return;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += (_, _) => _coordinator.Snapshot(new SessionSnapshotSource(_mainWindow, _adapter));
        _timer.Start();
    }

    /// <summary>
    /// Best-effort emergency snapshot, invoked from <see cref="AppCrashHandlers"/>'s
    /// <c>onAfterFault</c> callback. Must never throw — delegates to the coordinator, which
    /// already guards every step.
    /// </summary>
    public void TryEmergencySnapshot() =>
        _coordinator.TryEmergencySnapshot(new SessionSnapshotSource(_mainWindow, _adapter));

    /// <summary>
    /// Called on a normal window close. Stops the timer and deletes the session's snapshot —
    /// there is nothing left to recover from a clean shutdown.
    /// </summary>
    public void OnWindowClosed()
    {
        _timer?.Stop();
        _timer = null;
        _coordinator.DeleteSnapshot();
    }

    /// <summary>
    /// Called from <see cref="MainWindow"/>'s save-completion path immediately after a clean save.
    /// A just-saved workbook has nothing left to recover — delete the snapshot right away instead
    /// of waiting for <see cref="OnWindowClosed"/>, mirroring the WPF host's
    /// <c>NotifyAutosaveSaved</c> (called from <c>MarkWorkbookSaved</c>). Safe to call even if no
    /// snapshot was ever written (the underlying delete is a best-effort no-op in that case).
    /// </summary>
    public void NotifyAutosaveSaved() => _coordinator.DeleteSnapshot();

    /// <summary>
    /// Adapts <see cref="MainWindow"/>'s public <c>Session</c> (a <c>WorkbookSession</c>) to the
    /// neutral <see cref="IAutosaveSnapshotSource"/>, serializing via <see cref="NativeJsonAdapter"/>
    /// — the same serializer WPF's <c>AutosaveService</c> uses, so a recovered <c>.fxl</c> snapshot
    /// is readable regardless of which shell wrote it.
    /// </summary>
    private sealed class SessionSnapshotSource : IAutosaveSnapshotSource
    {
        private readonly MainWindow _mainWindow;
        private readonly NativeJsonAdapter _adapter;

        public SessionSnapshotSource(MainWindow mainWindow, NativeJsonAdapter adapter)
        {
            _mainWindow = mainWindow;
            _adapter = adapter;
        }

        public string? OriginalFilePath => _mainWindow.Session.CurrentFilePath;
        public string DisplayName => _mainWindow.Session.DisplayName;
        public bool IsDirty => _mainWindow.Session.IsDirty;
        public int DirtyGeneration => _mainWindow.Session.DirtyGeneration;

        public void WriteSnapshot(string snapshotPath)
        {
            using var fs = AutosaveSnapshotCoordinator.OpenSnapshotStream(snapshotPath);
            _adapter.Save(_mainWindow.Session.Workbook, fs);
        }
    }
}
