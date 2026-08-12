namespace FreeW.App.Avalonia;

internal sealed partial class AutosaveAdapter
{
    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();
    internal void SimulateCrashForTests() => _session.Dispose();
}

internal sealed partial class RecoveryPromptDialog
{
    public static Func<string, bool>? TestResponder { get; set; }

    static partial void ResolveResponseOverride(string message, ref bool handled, ref bool response)
    {
        if (TestResponder is not { } responder)
            return;

        handled = true;
        response = responder(message);
    }
}
