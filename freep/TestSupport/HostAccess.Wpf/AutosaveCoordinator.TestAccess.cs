namespace FreeP.App.Host;

/// <summary>
/// Test-only view of <see cref="AutosaveCoordinator"/>'s snapshot identity. Lives here rather than
/// in shipping source so <c>HostAccessOwnershipTests.ShippingSourceAndAssembly_ExcludeHostTestHooks</c>
/// stays satisfied -- the compile item is conditioned on <c>FreePHostAccess</c>.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();

    /// <summary>
    /// Releases this coordinator's snapshot ownership lock without deleting the snapshot -- exactly
    /// what an actual process crash does (the OS releases file locks on exit; nothing runs the
    /// clean-exit deletion path). Without this, a snapshot written earlier in the same test process
    /// stays "live owned" and <c>AutosaveSnapshotStore.ExcludeLiveOwned</c> filters it out of
    /// recovery, even though the coordinator that wrote it is done with it for the test's purposes.
    /// </summary>
    internal void SimulateCrashForTests() => _session.Dispose();
}
