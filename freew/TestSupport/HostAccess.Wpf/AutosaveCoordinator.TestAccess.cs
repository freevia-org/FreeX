namespace FreeW.App.Host;

internal sealed partial class AutosaveCoordinator
{
    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();
    internal void SimulateCrashForTests() => _session.Dispose();
}
