using Free.Shared.Shell.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Test-only view of <see cref="AutosaveAdapter"/>'s snapshot identity, plus a stand-in for the
/// recovery prompt so headless tests never have to show a real modal. Lives here rather than in
/// shipping source so <c>HostAccessOwnershipTests.ShippingSourceAndAssembly_ExcludeHostTestHooks</c>
/// stays satisfied -- the compile item is conditioned on <c>FreePHostAccess</c>.
/// </summary>
internal sealed partial class AutosaveAdapter
{
    internal static int ActiveAdapterCountForTests => AvaloniaAutosaveHost<Presentation>.ActiveCount;
    internal string SnapshotIdForTests => _host.Session.SnapshotId;
    internal void SnapshotNowForTests() => _host.Session.Snapshot();

    /// <summary>
    /// Releases this adapter's snapshot ownership lock without deleting the snapshot -- exactly what
    /// an actual process crash does. Without this, a snapshot written earlier in the same test
    /// process stays "live owned" and is filtered out of recovery.
    /// </summary>
    internal void SimulateCrashForTests() => _host.Session.Dispose();

    static partial void ResolvePromptResponder(ref Func<string, bool>? responder) =>
        responder = RecoveryPromptDialog.TestResponder;
}

internal static class RecoveryPromptDialog
{
    public static Func<string, bool>? TestResponder { get; set; }
}
