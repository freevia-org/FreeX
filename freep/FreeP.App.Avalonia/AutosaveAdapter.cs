using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// FreeP's binding of the shared <see cref="AvaloniaAutosaveHost{TDocument}"/> (periodic loop,
/// emergency fan-out, clean-exit stop, startup/manual recovery, recovery prompt). Mirrors FreeW's
/// <c>AutosaveAdapter</c>; only the document ports, the restore delegate, and the new-window /
/// dirty-gate callbacks are FreeP-specific.
///
/// <para>
/// Call <see cref="Start"/> after the window opens and <see cref="StopAsync"/> after the
/// dirty-gate has passed (on close). On startup, call <see cref="OfferRecoveryAsync"/> to
/// surface any snapshot from a previous crashed session.
/// </para>
/// </summary>
internal sealed partial class AutosaveAdapter : IDisposable
{
    private readonly AvaloniaAutosaveHost<Presentation> _host;

    /// <summary>
    /// Takes the shared <see cref="FileCommandWorkflow"/> rather than FreeP's
    /// <c>PresentationFileCommandSession</c> because dirty/path state is all this adapter reads from
    /// the file layer -- restoring a recovered deck goes through
    /// <paramref name="applyRecoveredPresentation"/>, which the window owns. Mirrors FreeW's
    /// <c>AutosaveAdapter</c>, and keeps the type cheap enough to construct in headless tests.
    /// </summary>
    public AutosaveAdapter(
        Func<Presentation> getPresentation,
        FileCommandWorkflow workflow,
        Action<Presentation, string?> applyRecoveredPresentation,
        Func<AutosaveDocumentPorts<Presentation>, FreePAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null,
        Func<Task<bool>>? confirmDiscardOrSaveAsync = null)
    {
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(applyRecoveredPresentation);

        var ports = new AutosaveDocumentPorts<Presentation>(
            GetOriginalFilePath: () => workflow.CurrentPath,
            GetDisplayName: () => workflow.DisplayName,
            GetIsDirty: () => workflow.IsDirty,
            GetDirtyGeneration: () => workflow.DirtyGeneration,
            ExecuteWithDocument: writePresentation =>
                writePresentation(getPresentation()));

        _host = new AvaloniaAutosaveHost<Presentation>(
            FreePAutosaveProfile.Session,
            ports,
            new AvaloniaAutosaveHostCallbacks<Presentation>(
                // startup-fileopen F2: MainWindow opens a command-line/file-association document
                // into this window before the startup offer runs. It is not dirty, so the manual
                // dirty gate would not protect it either -- a window that already holds an opened
                // deck (a blank new presentation has no path, an opened one does) must route every
                // accepted candidate to its own window instead of silently replacing it.
                CurrentWindowHasExplicitDocument: () => workflow.CurrentPath is not null,
                RestoreIntoCurrentWindow: AutosaveRecoveryRestore<Presentation>.FromDocument(
                    applyRecoveredPresentation),
                RecoverInNewWindowAsync: recoverInNewWindowAsync,
                // Manual "Recover Unsaved Presentations" may run against a dirty presentation, so it
                // runs the same save/discard dirty gate FreeP's WPF host runs.
                ConfirmDiscardOrSaveAsync: confirmDiscardOrSaveAsync,
                PromptResponseOverride: ResolvePromptResponse),
            UiText.Get,
            sessionFactory: sessionPorts => sessionFactory?.Invoke(sessionPorts) ?? new FreePAutosaveSession(sessionPorts));
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's presentation. Must never throw.
    /// </summary>
    public void TryEmergencySnapshot() => _host.TryEmergencySnapshot();

    /// <summary>
    /// Attempts an emergency snapshot for every live window's presentation. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see App.cs's DesktopProfile) so a crash takes the same
    /// best-effort snapshot FreeX's and FreeW's Avalonia hosts do instead of losing every edit since
    /// the last periodic autosave tick.
    /// </summary>
    public static void TryEmergencySnapshots() => AvaloniaAutosaveHost<Presentation>.TrySnapshotAll();

    /// <summary>Start the periodic autosave loop. Safe to call from any thread.</summary>
    public void Start() => _host.Start();

    /// <summary>
    /// Stop the loop and delete the current-session snapshot (clean exit). Awaitable so the
    /// window's close gate can ensure cleanup before the process exits.
    /// </summary>
    public Task StopAsync() => _host.StopAsync();

    /// <summary>
    /// Check for recovery candidates from a previous session and offer each one in order.
    /// Must be called from the UI thread (it may show an Avalonia dialog).
    /// Errors are swallowed -- recovery is best-effort and never blocks startup.
    /// </summary>
    public Task OfferRecoveryAsync(Window owner) => _host.OfferRecoveryAsync(owner);

    /// <summary>
    /// Manual Backstage "Recover Unsaved Presentations" command. Reachable at any point
    /// mid-session, possibly against a dirty presentation, so restoring into THIS window runs the
    /// save/discard dirty gate supplied as <c>confirmDiscardOrSaveAsync</c> first. Tells the user
    /// when there is nothing to recover and surfaces failures instead of swallowing them.
    /// </summary>
    public Task RecoverUnsavedPresentationsAsync(Window owner) => _host.RecoverManuallyAsync(owner);

    public void Dispose() => _host.Dispose();

    private static bool? ResolvePromptResponse(string message)
    {
        Func<string, bool>? responder = null;
        ResolvePromptResponder(ref responder);
        return responder?.Invoke(message);
    }

    /// <summary>Headless-test seam: lets tests answer the recovery prompt without a real modal.</summary>
    static partial void ResolvePromptResponder(ref Func<string, bool>? responder);
}
