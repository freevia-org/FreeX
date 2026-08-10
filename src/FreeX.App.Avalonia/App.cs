using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using FreeX.App.Services;
using FreeX.App.Services.Updates;
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
        AppProduct.Current = FreeXApplicationStartupDescriptor.ProductIdentity;

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
        FreeXApplicationStartupDescriptor.Theme.Apply(
            System.Environment.GetEnvironmentVariable,
            theme => ActiveTheme = theme,
            (theme, resourceKeyPrefix) =>
                Resources.MergedDictionaries.Add(
                    AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Register the process-wide crash hooks BEFORE constructing the window. Window
            // construction is itself a crash site (it builds the whole shell), and a fault there used
            // to happen with no handler installed, so no emergency snapshot ran for work in flight.
            // TryEmergencySnapshots is static and simply finds no coordinator to snapshot when one has
            // not attached yet, so registering this early is safe.
            AppCrashHandlers.Register(
                recordCrash: (exception, source) => Diagnostics?.RecordCrash(exception, source),
                subscribeDispatcher: null,
                onAfterFault: AvaloniaAutosaveCoordinator.TryEmergencySnapshots);

            // Ribbon/menu commands invoked from Avalonia click handlers are executed inside a guard
            // (Avalonia has no dispatcher-level unhandled-exception hook, so an escaping exception
            // would kill the process). Route the faults it catches into the same crash pipeline.
            Free.Shared.Ribbon.RibbonCommandFaultReporter.Handler =
                (exception, commandId) => Diagnostics?.RecordCrash(exception, "ribbon_command:" + commandId);

            var mainWindow = new MainWindow(StartupArguments, deferStartupFileOpen: true);
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
            // Crash hooks were registered above, before the window was constructed.
            autosaveCoordinator.Start();
            mainWindow.Closed += (_, _) => autosaveCoordinator.OnWindowClosed();

            // Startup recovery must run after the window is visible (it hosts the confirmation
            // dialog), so defer it to the next UI-thread dispatch rather than running inline here.
            Dispatcher.UIThread.Post(() => _ = CompleteStartupAsync(mainWindow, snapshotStore, StartupArguments));

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

    private static async Task CompleteStartupAsync(
        MainWindow mainWindow,
        AutosaveSnapshotStore snapshotStore,
        IReadOnlyList<string> startupArguments)
    {
        try
        {
            var recoveryAccepted = await OfferStartupRecoveryAsync(mainWindow, snapshotStore);
            var startupFilePlan = StartupFileOpenPlanner.Plan(startupArguments, recoveryAccepted);

            foreach (var entry in startupFilePlan.Entries)
            {
                var targetWindow = entry.OpenInNewWindow
                    ? OpenIndependentWindow()
                    : mainWindow;
                await targetWindow.OpenStartupFileAsync(entry.Path);
            }

            if (startupFilePlan.ShouldReportMissingPath)
                mainWindow.ReportStartupFileNotFound(startupFilePlan.FirstMissingPath!);
        }
        catch (OperationCanceledException)
        {
            Diagnostics?.RecordEvent("startup_open_canceled", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "startup",
                ["status"] = "canceled"
            });
        }
        catch (Exception ex)
        {
            Diagnostics?.RecordEvent("startup_open_failed", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "startup",
                ["status"] = "error",
                ["reason"] = ex.GetType().Name
            });
        }
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
    /// by <see cref="AutosaveRecoveryCandidateProcessor"/> so that multiple autosave snapshots which all
    /// belong to the SAME underlying document (e.g. "New Window" siblings over one shared
    /// <c>WorkbookSession</c> — see <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>, which
    /// gives every sibling its own <see cref="AvaloniaAutosaveCoordinator"/> and therefore its own
    /// snapshot file) collapse to a single, newest-per-document candidate. What remains after that
    /// are provably INDEPENDENT documents (different <c>Workbook.Id</c>/<see cref="AutosaveSidecar.DocumentId"/>),
    /// e.g. a sibling window that was detached via File &gt; Open or File &gt; New
    /// (<c>MainWindow.cs</c>'s <c>ReplaceSession</c> call sites) before the crash — each of those is
    /// offered on its own, never silently discarded in favor of another. Candidates whose original
    /// on-disk file was saved more recently than the snapshot is then dropped by the same processor,
    /// so recovery never offers to silently
    /// overwrite a newer manual save with stale snapshot content (R120, porting the WPF host's
    /// R74-services-autosave-recovery-4-1 fix). The first accepted candidate
    /// is restored into <paramref name="mainWindow"/>; any further accepted candidates are restored
    /// into freshly opened windows so accepting more than one recovery never overwrites another.
    /// Declined or unreadable candidates are deleted so they are never re-offered. Must never throw —
    /// startup recovery is best-effort and must not affect normal startup.
    /// </summary>
    private static async Task<bool> OfferStartupRecoveryAsync(MainWindow mainWindow, AutosaveSnapshotStore snapshotStore)
    {
        var host = new StartupRecoveryWorkflowHost<MainWindow>(
            PrimaryTarget: mainWindow,
            OfferAsync: async (offer, _) => await mainWindow.ShowRecoveryPromptAsync(
                UiText.Format(offer.PromptKey, offer.PromptArguments),
                UiText.Get(offer.TitleKey)),
            CreateAdditionalTargetAsync: _ => ValueTask.FromResult(OpenRecoveryWindow()),
            RestoreAsync: (target, candidate, _) => target.LoadRecoverySnapshotAsync(
                candidate.SnapshotPath,
                candidate.Sidecar.OriginalFilePath),
            ExecuteRestoreAsync: (operation, _) => new ValueTask(operation()),
            DeleteCandidate: AutosaveSnapshotStore.DeleteCandidate);

        return await StartupRecoveryWorkflow.RunAsync(snapshotStore.EnumerateCandidates(), host);
    }

    /// <summary>
    /// Opens a brand-new, independent <see cref="MainWindow"/> (its own fresh default session, NOT
    /// a shared sibling view) to host a subsequent accepted recovery candidate, wiring its autosave
    /// coordinator exactly like <c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c> /
    /// <see cref="OnFrameworkInitializationCompleted"/> already do for every other live window.
    /// </summary>
    private static MainWindow OpenRecoveryWindow() => OpenIndependentWindow();

    private static MainWindow OpenIndependentWindow()
    {
        var window = new MainWindow([], deferStartupFileOpen: true);
        var snapshotStore = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
        var autosaveCoordinator = new AvaloniaAutosaveCoordinator(window, snapshotStore);
        window.AttachAutosaveCoordinator(autosaveCoordinator);
        window.Closed += (_, _) => autosaveCoordinator.OnWindowClosed();
        autosaveCoordinator.Start();
        window.Show();
        window.Activate();
        return window;
    }

}

/// <summary>
/// Avalonia-shell lifetime wiring around the portable <see cref="AutosaveService"/> used by WPF.
/// Periodic snapshotting while dirty, an emergency snapshot on crash, startup recovery (via
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
    private static readonly object ActiveCoordinatorsGate = new();
    private static readonly List<AvaloniaAutosaveCoordinator> ActiveCoordinators = [];

    private readonly AutosaveService _service;
    private DispatcherTimer? _timer;
    private bool _closed;

    public AvaloniaAutosaveCoordinator(MainWindow mainWindow, AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(store);

        _service = new AutosaveService(store);
        _service.Attach(mainWindow, Guid.NewGuid());

        lock (ActiveCoordinatorsGate)
            ActiveCoordinators.Add(this);
    }

    /// <summary>Starts the periodic autosave timer. Safe to call once, on the UI thread.</summary>
    public void Start()
    {
        if (_timer is not null || _closed)
            return;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutosaveService.DefaultInterval
        };
        _timer.Tick += (_, _) => _service.OnTimerTick();
        _timer.Start();
    }

    /// <summary>Attempts an emergency snapshot for every live workbook view.</summary>
    public static void TryEmergencySnapshots()
    {
        AvaloniaAutosaveCoordinator[] coordinators;
        lock (ActiveCoordinatorsGate)
            coordinators = ActiveCoordinators.ToArray();

        foreach (var coordinator in coordinators)
            coordinator._service.TryEmergencySnapshot();
    }

    /// <summary>
    /// Called on a normal window close. Stops the timer and deletes the session's snapshot —
    /// there is nothing left to recover from a clean shutdown.
    /// </summary>
    public void OnWindowClosed()
    {
        if (_closed)
            return;

        _closed = true;
        _timer?.Stop();
        _timer = null;
        _service.DeleteSnapshot();
        _service.Dispose();
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
    public void NotifyAutosaveSaved() => _service.DeleteSnapshot();
}
