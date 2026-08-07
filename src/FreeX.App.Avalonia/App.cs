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
using Free.Shared.Shell.Avalonia;
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

    internal static InteractionValidationOptions? InteractionValidationOptions { get; set; }

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

        // Route the shared shell's OK/Cancel button text and generic message-box titles
        // (AvaloniaDialogButtonRowFactory.CreateOkCancel, AvaloniaUserMessageDialog) through
        // FreeX's own localized resource catalog instead of the shared shell's neutral-English
        // ShellStrings.Current default — mirrors the WPF host's
        // AppLocalization.Bootstrap.InstallSharedSeams() (App.xaml.cs). Must run before any
        // window/dialog can be shown, so it goes first, ahead of even the brand theme setup below.
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

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
                onAfterFault: AvaloniaAutosaveCoordinator.TryEmergencySnapshots);
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

            if (InteractionValidationOptions is { } interactionValidationOptions)
                InteractionValidationCoordinator.Start(mainWindow, interactionValidationOptions, Diagnostics);
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
    /// Checks for crash-recovery snapshots from previous (now-dead) sessions and offers each one
    /// individually, loading accepted candidates into live windows. Mirrors
    /// <c>the WPF host.App.xaml.cs</c>'s <c>OfferStartupRecovery</c>: candidates are first collapsed
    /// by <see cref="DeduplicateCandidatesByDocument"/> so that multiple autosave snapshots which all
    /// belong to the SAME underlying document (e.g. "New Window" siblings over one shared
    /// <c>WorkbookSession</c> — see <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>, which
    /// gives every sibling its own <see cref="AvaloniaAutosaveCoordinator"/> and therefore its own
    /// snapshot file) collapse to a single, newest-per-document candidate. What remains after that
    /// are provably INDEPENDENT documents (different <c>Workbook.Id</c>/<see cref="AutosaveSidecar.DocumentId"/>),
    /// e.g. a sibling window that was detached via File &gt; Open or File &gt; New
    /// (<c>MainWindow.cs</c>'s <c>ReplaceSession</c> call sites) before the crash — each of those is
    /// offered on its own, never silently discarded in favor of another. Candidates whose original
    /// on-disk file was saved more recently than the snapshot are then dropped (see
    /// <see cref="FilterCandidatesWithNewerOriginal"/>), so recovery never offers to silently
    /// overwrite a newer manual save with stale snapshot content (R120, porting the WPF host's
    /// R74-services-autosave-recovery-4-1 fix). The first accepted candidate
    /// is restored into <paramref name="mainWindow"/>; any further accepted candidates are restored
    /// into freshly opened windows so accepting more than one recovery never overwrites another.
    /// Declined or unreadable candidates are deleted so they are never re-offered. Must never throw —
    /// startup recovery is best-effort and must not affect normal startup.
    /// </summary>
    private static async Task OfferStartupRecoveryAsync(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        try
        {
            // This process's OWN just-started coordinator has not written a snapshot yet at this
            // point in startup, so every candidate here necessarily belongs to a previous launch —
            // no self-recovery filtering is needed.
            var candidates = FilterCandidatesWithNewerOriginal(
                DeduplicateCandidatesByDocument(snapshotStore.EnumerateCandidates()));
            if (candidates.Count == 0)
                return;

            var anyAccepted = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var displayName = candidate.Sidecar.DisplayName;
                var remaining = candidates.Count - i;

                // When multiple independent documents remain, mention how many are outstanding so
                // the user is not surprised by repeated prompts — mirrors the WPF host's
                // Startup_RecoveryPromptMultiple/-Named variants.
                string prompt;
                if (remaining > 1)
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? $"FreeX found unsaved changes from a previous session ({remaining} unsaved workbooks found). Recover this one?"
                        : $"FreeX found unsaved changes to \"{displayName}\" from a previous session ({remaining} unsaved workbooks found). Recover this one?";
                }
                else
                {
                    prompt = string.IsNullOrWhiteSpace(displayName)
                        ? "FreeX found unsaved changes from a previous session. Recover them?"
                        : $"FreeX found unsaved changes to \"{displayName}\" from a previous session. Recover them?";
                }

                var accepted = await mainWindow.ShowRecoveryPromptAsync(prompt, "Recover Unsaved Workbook");
                if (accepted)
                {
                    // The first accepted candidate restores into the already-shown main window; any
                    // subsequent accepted candidate opens its own new window so accepting more than
                    // one recovery never overwrites an already-recovered document.
                    var targetWindow = anyAccepted ? OpenRecoveryWindow() : mainWindow;
                    anyAccepted = true;

                    // A load failure (corrupt/unreadable snapshot) leaves the target window's
                    // just-shown default workbook untouched — there is nothing else to do; the bad
                    // snapshot is deleted below either way so it is never re-offered.
                    await targetWindow.LoadRecoverySnapshotAsync(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath);
                }

                // Whether declined, or accepted and (successfully or not) loaded, this candidate is
                // never re-offered on a future launch.
                try { AutosaveSnapshotStore.DeleteCandidate(candidate); } catch { /* best-effort */ }
            }
        }
        catch
        {
            // Startup recovery must never affect normal startup.
        }
    }

    /// <summary>
    /// Opens a brand-new, independent <see cref="MainWindow"/> (its own fresh default session, NOT
    /// a shared sibling view) to host a subsequent accepted recovery candidate, wiring its autosave
    /// coordinator exactly like <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c> /
    /// <see cref="OnFrameworkInitializationCompleted"/> already do for every other live window.
    /// </summary>
    private static MainWindow OpenRecoveryWindow()
    {
        var window = new MainWindow(StartupArguments);
        var snapshotStore = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
        var autosaveCoordinator = new AvaloniaAutosaveCoordinator(window, snapshotStore);
        window.AttachAutosaveCoordinator(autosaveCoordinator);
        window.Closed += (_, _) => autosaveCoordinator.OnWindowClosed();
        autosaveCoordinator.Start();
        window.Show();
        window.Activate();
        return window;
    }

    /// <summary>
    /// Collapses recovery candidates that all belong to the same underlying document down to a
    /// single one, deleting the rest — ports the WPF host's <c>DeduplicateCandidatesByDocument</c>
    /// (App.xaml.cs) verbatim in behavior. A workbook shared across "New Window" views
    /// (<c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>) gets one autosave snapshot PER
    /// WINDOW (autosave ownership is per-window, not per-document — see
    /// <see cref="AvaloniaAutosaveCoordinator"/>'s constructor, which mints a unique snapshot id per
    /// instance even when several coordinators share the same <c>WorkbookSession</c>'s workbook). If
    /// the process crashes, that leaves multiple snapshot files with the same
    /// <see cref="AutosaveSidecar.OriginalFilePath"/>/<see cref="AutosaveSidecar.DisplayName"/> on
    /// disk for what was really one shared, dirtied document — without this step,
    /// <see cref="OfferStartupRecoveryAsync"/> would offer each one individually, and accepting more
    /// than one would load the same document into two independent windows with disconnected
    /// sessions. Recovery only ever needs to restore ONE copy of a document, so we keep just the
    /// newest snapshot per document identity and delete its siblings up front.
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
                try { AutosaveSnapshotStore.DeleteCandidate(existing); } catch { /* best-effort */ }
                newestByDocument[documentKey] = candidate;
            }
            else
            {
                try { AutosaveSnapshotStore.DeleteCandidate(candidate); } catch { /* best-effort */ }
            }
        }

        return ordered.Select(key => newestByDocument[key]).ToList();
    }

    /// <summary>
    /// Drops any candidate whose ORIGINAL on-disk file was saved more recently than the crash
    /// snapshot itself (R120-avalonia-startup-recovery-newer-original-1, porting the WPF host's
    /// <c>FilterCandidatesWithNewerOriginal</c>/R74-services-autosave-recovery-4-1 verbatim in
    /// behavior). This happens when the user saved the document normally after the crash that
    /// produced the snapshot (e.g. reopened the file by hand and saved over it, or another
    /// window/session saved it) — offering that snapshot would let the user unknowingly clobber
    /// their own newer manual save with stale recovered content. Excel never offers recovery in
    /// this situation. A candidate whose original file is missing (never saved, moved, or deleted)
    /// or whose on-disk timestamp is older than or equal to the snapshot is unaffected and still
    /// offered exactly as before. Filtered candidates are deleted outright rather than left on disk
    /// to be silently re-checked (and re-skipped) forever — their content is already superseded by
    /// the newer file on disk, so nothing of value would be recovered by keeping them.
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
    /// Identity key grouping candidates that are recovery snapshots of the same document — ports the
    /// WPF host's <c>GetDocumentIdentityKey</c>. A saved workbook is keyed by its original file path
    /// (case-insensitive); an unsaved workbook by its display name. Either way, the key is further
    /// scoped to the originating process launch (see <see cref="GetLaunchScope"/>) and to the
    /// sidecar's <see cref="AutosaveSidecar.DocumentId"/> (populated from
    /// <c>MainWindow.DocumentId</c>, i.e. the in-memory <c>Workbook.Id</c>): genuine "New Window"
    /// siblings share the exact same Workbook instance and therefore the same DocumentId, while two
    /// independent windows (e.g. one detached via File &gt; Open/File &gt; New) each get their own
    /// freshly created/deserialized Workbook and therefore different DocumentIds. Candidates whose
    /// DocumentId is missing (a snapshot written by a build predating this field) are never treated
    /// as provably the same document as another candidate — see
    /// <see cref="GetDocumentIdentityComponent"/>. Candidates that have neither a path nor a name
    /// (should not normally happen) each get their own unique key so they are never incorrectly
    /// merged with an unrelated candidate.
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
    /// The DocumentId contribution to <see cref="GetDocumentIdentityKey"/>. When the sidecar carries
    /// a <see cref="AutosaveSidecar.DocumentId"/> (every snapshot written by this build does — see
    /// <c>SessionSnapshotSource.DocumentId</c> below), it is authoritative proof of whether two
    /// candidates share the same underlying Workbook instance. When it is missing, the candidate's
    /// own snapshot path is used instead so it is NEVER treated as provably the same document as
    /// another candidate and silently merged/deleted — better to keep (and offer) an extra candidate
    /// than to silently destroy an unrelated window's unsaved edits.
    /// </summary>
    private static string GetDocumentIdentityComponent(AutosaveRecoveryCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Sidecar.DocumentId)
            ? "unknown:" + candidate.SnapshotPath
            : candidate.Sidecar.DocumentId;

    /// <summary>
    /// Extracts the "{processId}-{launchTag}" scope from a snapshot's file name, e.g.
    /// "recovery-12345-a1b2c3d4-e5f6a7b8.fxl" -&gt; "12345-a1b2c3d4" (matching
    /// <see cref="AvaloniaAutosaveCoordinator"/>'s snapshot-id format). This identifies the
    /// originating process launch (not the individual window within it), so sibling windows of the
    /// same crashed session still share a scope and can be deduplicated, while two different
    /// processes/launches never do. Falls back to the full snapshot path when the file name does not
    /// match the expected pattern, so an unrecognized name is always treated as its own distinct
    /// scope rather than accidentally merged with anything else.
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
/// Recovery loads into a fresh <c>WorkbookSession</c>, associates the original file path, and
/// explicitly marks the recovered document dirty so the modified indicator and close prompt are
/// preserved. Each live workbook view has its own coordinator and snapshot identity; crash
/// handling fans out to all coordinators.
/// </para>
/// </summary>
internal sealed class AvaloniaAutosaveCoordinator
{
    // Matches the WPF host's DispatcherTimer interval (see AutosaveService.DefaultInterval).
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly object ActiveCoordinatorsGate = new();
    private static readonly List<AvaloniaAutosaveCoordinator> ActiveCoordinators = [];

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

        // Keep a unique snapshot per live workbook view so sibling windows own independent
        // autosave lifecycles and recovery artifacts.
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = Guid.NewGuid().ToString("N")[..8];
        _coordinator = new AutosaveSnapshotCoordinator(
            _store,
            $"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}");
        lock (ActiveCoordinatorsGate)
            ActiveCoordinators.Add(this);
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

    /// <summary>Attempts an emergency snapshot for every live workbook view.</summary>
    public static void TryEmergencySnapshots()
    {
        AvaloniaAutosaveCoordinator[] coordinators;
        lock (ActiveCoordinatorsGate)
            coordinators = ActiveCoordinators.ToArray();

        foreach (var coordinator in coordinators)
            coordinator.TryEmergencySnapshot();
    }

    /// <summary>
    /// Called on a normal window close. Stops the timer and deletes the session's snapshot —
    /// there is nothing left to recover from a clean shutdown.
    /// </summary>
    public void OnWindowClosed()
    {
        _timer?.Stop();
        _timer = null;
        _coordinator.DeleteSnapshot();
        lock (ActiveCoordinatorsGate)
            ActiveCoordinators.Remove(this);
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

        // Stable identity of the in-memory Workbook this snapshot came from (see
        // App.GetDocumentIdentityKey's doc comment): two windows sharing the SAME Workbook.Id are
        // genuine "New Window" siblings over one shared document; two windows with DIFFERENT
        // Workbook.Ids are independent documents even when they happen to share a saved file path.
        public string? DocumentId => _mainWindow.Session.Workbook.Id.Value.ToString();

        public void WriteSnapshot(string snapshotPath)
        {
            using var fs = AutosaveSnapshotCoordinator.OpenSnapshotStream(snapshotPath);
            _adapter.Save(_mainWindow.Session.Workbook, fs);
        }
    }
}
