namespace FreeW.App.Host;

internal sealed partial class AutosaveCoordinator
{
    internal string SnapshotIdForTests => _host.Session.SnapshotId;
    internal void SnapshotNowForTests() => _host.Session.Snapshot();
    internal void SimulateCrashForTests() => _host.Session.Dispose();
}
