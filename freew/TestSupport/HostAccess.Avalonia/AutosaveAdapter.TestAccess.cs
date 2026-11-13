using FreeW.Core.Model;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

internal sealed partial class AutosaveAdapter
{
    internal static int ActiveAdapterCountForTests => AvaloniaAutosaveHost<TextDocument>.ActiveCount;
    internal string SnapshotIdForTests => _host.Session.SnapshotId;
    internal void SnapshotNowForTests() => _host.Session.Snapshot();
    internal void SimulateCrashForTests() => _host.Session.Dispose();

    static partial void ResolvePromptResponder(ref Func<string, bool>? responder) =>
        responder = RecoveryPromptDialog.TestResponder;
}

/// <summary>
/// Test stand-in for the recovery prompt so headless tests never have to show a real modal. Lives
/// here rather than in shipping source so the compile item stays conditioned on the host-access flag.
/// </summary>
internal static class RecoveryPromptDialog
{
    public static Func<string, bool>? TestResponder { get; set; }
}
