using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 148 REMEDIATION (startup-fileopen F2, WPF host sibling): <see
/// cref="AutosaveCoordinator.OfferRecovery"/>'s "useCurrentWindow" branch used to restore an
/// accepted recovery candidate straight into the caller's window with no check at all -- the exact
/// bug already fixed in FreeP's Avalonia <c>AutosaveAdapter.OfferRecoveryAsync</c>. A command-line
/// or file-association document may already be loaded into this window (via a real
/// <c>FileCommands.OpenPath</c>) before <c>OfferRecovery</c> runs, and that document is not dirty
/// (just opened, not edited), so the silent startup offer must not silently replace it.
/// </summary>
public sealed class R148_OfferRecoveryDoesNotDiscardOpenedDocumentTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.R148OfferRecoveryTests", Guid.NewGuid().ToString("N"));

    public R148_OfferRecoveryDoesNotDiscardOpenedDocumentTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private (AutosaveCoordinator coordinator, FileCommands file) NewWindowHarness(
        AutosaveSnapshotStore store,
        Action<AutosaveRecoveryCandidate>? recoverInNewWindow = null)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var file = new FileCommands(
            window,
            editor,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json")));
        var coordinator = new AutosaveCoordinator(
            editor,
            file,
            ports => new FreeWAutosaveSession(ports, store),
            recoverInNewWindow: recoverInNewWindow is null
                ? null
                : candidate =>
                {
                    recoverInNewWindow(candidate);
                    return true;
                });
        return (coordinator, file);
    }

    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_tempDir, name);
        DocxWriter.Write(doc, path);
        return path;
    }

    /// <summary>
    /// THE FIX: a window that already has an explicitly opened document (simulated the same way
    /// <c>MainWindow</c> leaves it after a successful startup open -- opened via
    /// <c>FileCommands.OpenPath</c>, so <c>CurrentPath</c> is set and the document is NOT dirty)
    /// must not have that document silently replaced by an unrelated recovered snapshot.
    /// </summary>
    [StaFact]
    public void OfferRecovery_RoutesToANewWindowWhenTheCurrentWindowAlreadyHasAnOpenedDocument()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        var (crashed, crashedFile) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.SnapshotNowForTests();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("the crashed window must have left a snapshot behind");
        crashed.SimulateCrashForTests();

        var recoveredInNewWindow = false;
        var (recovering, recoveringFile) = NewWindowHarness(store, _ => recoveredInNewWindow = true);
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };

        // Mirrors what MainWindow does for a command-line/file-association startup file: a real
        // Open, which leaves CurrentPath non-null and the document clean (not dirty).
        var openedPath = WriteDocx("B.docx", "The user's own document");
        recoveringFile.OpenPath(openedPath).Should().BeTrue();
        recoveringFile.IsDirty.Should().BeFalse("a freshly opened document has nothing unsaved yet");

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes; // accept the "recover this?" offer
        try
        {
            var anyAccepted = recovering.OfferRecovery(owningWindow);

            anyAccepted.Should().BeTrue("the candidate is still offered and accepted");
            recoveredInNewWindow.Should().BeTrue(
                "an already-opened document must route the recovery into a NEW window, not overwrite this one");
            recoveringFile.CurrentPath.Should().Be(openedPath,
                "the just-opened document in the current window must never be silently replaced");
            recoveringFile.IsDirty.Should().BeFalse(
                "the current window's clean, just-opened document must be untouched by the recovery");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Sibling no-regression (rule 10's adjacent case): a genuinely fresh window -- no startup file
    /// opened, <c>CurrentPath</c> still null -- must keep the prior behaviour exactly: the silent
    /// startup offer recovers straight into the current window, unconditionally and ungated.
    /// </summary>
    [StaFact]
    public void OfferRecovery_StillRecoversIntoTheCurrentWindowWhenItHasNoOpenedDocument()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        var (crashed, crashedFile) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.SnapshotNowForTests();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        crashed.SimulateCrashForTests();

        var recoveredInNewWindow = false;
        var (recovering, recoveringFile) = NewWindowHarness(store, _ => recoveredInNewWindow = true);
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        // never opened -- CurrentPath stays null

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes;
        try
        {
            var anyAccepted = recovering.OfferRecovery(owningWindow);

            anyAccepted.Should().BeTrue();
            recoveredInNewWindow.Should().BeFalse(
                "the fresh-window path must not be redirected to a new window -- it must recover straight into itself, exactly as before this fix");
            recoveringFile.IsDirty.Should().BeTrue("a recovered document is unsaved work and must stay dirty");
            File.Exists(snapshotPath).Should().BeFalse(
                "a successfully recovered snapshot must be cleaned up so it is not offered again");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }
}
