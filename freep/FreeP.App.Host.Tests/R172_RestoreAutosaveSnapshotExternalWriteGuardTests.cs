using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round-172 finding shared-autosave-recovery/F1: <c>PresentationFileCommandSession
/// .RestoreAutosaveSnapshot</c> loaded the recovered presentation and pointed <c>CurrentPath</c> at
/// the original file, but never re-armed the external-modification write-time guard
/// (<c>_currentFileSourceLastWriteTimeUtc</c>). That field stayed at its constructor-default
/// <c>null</c>, and <c>ExternalFileWriteConflictPolicy</c> treats a null baseline as "not changed" --
/// so the very first save after a crash-recovery silently overwrote a copy of the original file that
/// had changed on disk while the app was gone, with no prompt and no way to tell afterward.
/// Mirrors FreeW's <c>FreeWDocumentFileWorkflow.OpenSnapshotAsync</c> rationale (recompute the
/// baseline from the ORIGINAL path's current on-disk write time, not the snapshot's) and FreeX WPF's
/// <c>MainWindow.SetCurrentFilePathForRecovery</c>.
/// </summary>
public sealed class R172_RestoreAutosaveSnapshotExternalWriteGuardTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.R172.AutosaveGuard-");
    private string TempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private static (PresentationFileCommandSession File, Window Window) NewSession(string tempDir)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var file = WpfPresentationFileCommandSessionFactory.Create(
            window,
            () => model,
            loaded => model = loaded,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".json")),
            videoEncoderCapability: LinuxVideoEncoderCapability.Unavailable("Test encoder handoff deferred."),
            nativePrintCapability: PresentationNativePrintHandoffHostCapabilities.Deferred(
                "WPF print host",
                "Test printer handoff deferred."));
        return (file, window);
    }

    /// <summary>
    /// Core regression: after recovery, if the original file changes on disk before the next save,
    /// the guard must fire -- proven by observing the "changed by another program" confirm prompt
    /// actually get asked, and by the save being refused when the user declines it. Before the fix,
    /// the prompt was never asked at all and the save silently succeeded over the newer content.
    /// </summary>
    [StaFact]
    public async Task RestoreAutosaveSnapshot_ThenSave_PromptsAndRefusesWhenOriginalChangedOnDisk()
    {
        var (file, _) = NewSession(TempDir);
        {
            var originalPath = Path.Combine(TempDir, "Quarterly.pptx");
            PptxPackageWriter.Write(Presentation.CreateEmpty(), originalPath);
            // Back-date the write time so the write we perform below to simulate the "someone else
            // saved over it" gesture is guaranteed to observe a strictly later timestamp even on
            // filesystems with coarse (e.g. 2s FAT-style) write-time resolution.
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(-5));

            var snapshotPath = Path.Combine(TempDir, "snapshot.fxl");
            PptxPackageWriter.Write(Presentation.CreateEmpty(), snapshotPath);

            var restored = file.RestoreAutosaveSnapshot(snapshotPath, originalPath);
            restored.Should().BeTrue();
            file.CurrentPath.Should().Be(originalPath);

            // Simulate "another program changed the original file while FreeP was crashed/relaunching".
            await Task.Delay(50);
            File.WriteAllText(originalPath, "external writer content");
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow);

            var promptCount = 0;
            HeadlessMessageBox.Handler = (_, _) =>
            {
                promptCount++;
                return UserMessageResult.No; // decline the overwrite
            };
            try
            {
                var result = await file.SavePathAsync(originalPath);

                promptCount.Should().Be(
                    1,
                    "the guard must ask before overwriting a file that changed on disk since recovery -- " +
                    "before the fix the null baseline made ExternalFileWriteConflictPolicy think nothing " +
                    "had changed, so no prompt was ever raised");
                result.Cancelled.Should().BeTrue(
                    "declining the overwrite prompt must refuse the save, not silently proceed");
                result.Message.Should().Contain("changed by another program");
                File.ReadAllText(originalPath).Should().Be(
                    "external writer content",
                    "a declined overwrite must leave the externally-written content on disk untouched");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        }
    }

    /// <summary>
    /// Sibling no-regression: when the original file has NOT changed on disk since recovery, the
    /// guard must stay quiet and the save must proceed without any prompt -- the fix must not turn
    /// every ordinary recover-then-save into a spurious conflict.
    /// </summary>
    [StaFact]
    public async Task RestoreAutosaveSnapshot_ThenSave_SavesWithoutPromptWhenOriginalUnchanged()
    {
        var (file, _) = NewSession(TempDir);
        {
            var originalPath = Path.Combine(TempDir, "Quarterly.pptx");
            PptxPackageWriter.Write(Presentation.CreateEmpty(), originalPath);

            var snapshotPath = Path.Combine(TempDir, "snapshot.fxl");
            PptxPackageWriter.Write(Presentation.CreateEmpty(), snapshotPath);

            var restored = file.RestoreAutosaveSnapshot(snapshotPath, originalPath);
            restored.Should().BeTrue();

            var promptCount = 0;
            HeadlessMessageBox.Handler = (_, _) =>
            {
                promptCount++;
                return UserMessageResult.Yes;
            };
            try
            {
                var result = await file.SavePathAsync(originalPath);

                promptCount.Should().Be(0, "no external change occurred, so the guard must not prompt");
                result.Succeeded.Should().BeTrue();
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        }
    }
    /// <summary>
    /// r172 remediation. The fix above closed only the recover-into-a-NEW-window route, which reads
    /// the snapshot through RestoreAutosaveSnapshot. The common route -- the startup offer and the
    /// Backstage command recovering into the CURRENT window -- hands the shell an already-read
    /// presentation, so it cannot call that method; it went through the shell callback, which only
    /// marked the document dirty and left the guard baseline null. This drives the second route.
    /// </summary>
    [StaFact]
    public async Task AdoptRecoveredPresentation_ThenSave_PromptsAndRefusesWhenOriginalChangedOnDisk()
    {
        var (file, _) = NewSession(TempDir);
        {
            var originalPath = Path.Combine(TempDir, "CurrentWindow.pptx");
            PptxPackageWriter.Write(Presentation.CreateEmpty(), originalPath);
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(-5));

            // What the current-window recovery callback does: the shell has already loaded the
            // recovered presentation into the view, and hands the session only the original path.
            file.AdoptRecoveredPresentation(originalPath);
            file.CurrentPath.Should().Be(originalPath);

            await Task.Delay(50);
            File.WriteAllText(originalPath, "external writer content");
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow);

            var promptCount = 0;
            HeadlessMessageBox.Handler = (_, _) =>
            {
                promptCount++;
                return UserMessageResult.No;
            };
            try
            {
                var result = await file.SavePathAsync(originalPath);

                promptCount.Should().Be(
                    1,
                    "recovering into the CURRENT window must arm the same guard the new-window route arms");
                result.Cancelled.Should().BeTrue();
                File.ReadAllText(originalPath).Should().Be("external writer content");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        }
    }
}
