namespace FreeP.App.Host;

/// <summary>
/// Test-only view of <see cref="AutosaveCoordinator"/>'s snapshot identity. Lives here rather than
/// in shipping source so <c>HostAccessOwnershipTests.ShippingSourceAndAssembly_ExcludeHostTestHooks</c>
/// stays satisfied -- the compile item is conditioned on <c>FreePHostAccess</c>.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    internal string SnapshotIdForTests => _session.SnapshotId;
}
