using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round-172 finding shared-autosave-recovery/F3: <see cref="AutosaveAdapter.CompleteDocumentRecovery"/>
/// used to hand-roll the restore (<c>_editor.LoadDocument(document); _workflow.MarkDirtyWithPath
/// (originalPath);</c>) instead of routing through <see cref="FreeWDocumentFileWorkflow.OpenSnapshotAsync"/>
/// -- the one place that re-arms the external-modification write-time guard from the ORIGINAL file's
/// current on-disk write time. <c>CurrentPath</c> ended up correctly pointed at the original file
/// (both objects share the same underlying <see cref="FileCommandWorkflow"/>), but the guard field
/// living inside the window's real <see cref="FreeWDocumentFileWorkflow"/> was never touched, so the
/// very first save after a crash-recovery silently overwrote a copy of the original file that had
/// changed on disk while the app was gone.
/// </summary>
public sealed class R172_RecoveryExternalWriteGuardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Func<Task> action) => HeadlessUiThread.RunAsync(action);

    private static FileCommandWorkflow NewWorkflow(string tempDir) =>
        new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".json")));

    private static void WriteCandidate(
        AutosaveSnapshotStore store, string id, string displayName, string originalPath, TextDocument document)
    {
        var snapshotPath = store.GetSnapshotPath(id);
        var sidecarPath = store.GetSidecarPath(id);
        DocxWriter.Write(document, snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalPath,
            DisplayName = displayName,
            // Must be newer than originalPath's on-disk write time -- AutosaveRecoveryCandidateProcessor
            // .FilterSupersededByNewerOriginal quarantines (deletes) any candidate whose ORIGINAL file
            // looks newer than the snapshot's own timestamp, on the theory that the original already has
            // more recent work than the crash snapshot. A stale fixed literal here would make the
            // candidate vanish from PlanRecoveries before recovery ever runs.
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }

    private static TextDocument DocumentWithText(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    /// <summary>
    /// Core regression: after a manual "Recover Unsaved" into the current window, if the original
    /// file changes on disk before the next save, the guard must fire -- proven by observing the
    /// "changed by another program" confirm prompt actually get asked, and by the save being refused
    /// when the user declines it. Before the fix, the guard field the save path reads was never
    /// re-armed on recovery, so no prompt was ever raised and the save proceeded silently.
    /// </summary>
    [Fact]
    public async Task RecoverUnsavedDocuments_ThenSave_PromptsAndRefusesWhenOriginalChangedOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            var originalPath = Path.Combine(dir, "Report.docx");
            DocxWriter.Write(DocumentWithText("Original content"), originalPath);
            // Back-date the write time so the external write performed below is guaranteed to
            // observe a strictly later timestamp even on filesystems with coarse write-time
            // resolution.
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(-5));

            WriteCandidate(store, "snap-guard", "Report.docx", originalPath, DocumentWithText("RECOVERED SNAPSHOT TEXT"));

            var promptCount = 0;
            FreeWDocumentFileWorkflow? capturedWorkflow = null;

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var ran = await OnUiThread(async () =>
                {
                    var editor = new DocumentView();
                    editor.LoadDocument(TextDocument.CreateEmpty());
                    var workflow = NewWorkflow(dir);
                    var documentFileWorkflow = new FreeWDocumentFileWorkflow(
                        workflow,
                        new DocumentPersistenceWorkflow(),
                        new FreeWDocumentFilePorts(
                            GetDocument: () => editor.Document,
                            LoadDocumentAsync: (document, _) =>
                            {
                                editor.LoadDocument(document);
                                return ValueTask.CompletedTask;
                            },
                            ConfirmExternallyModifiedOverwriteAsync: (_, _) =>
                            {
                                promptCount++;
                                return new ValueTask<bool>(false); // decline the overwrite
                            }));
                    capturedWorkflow = documentFileWorkflow;

                    var adapter = new AutosaveAdapter(
                        editor,
                        workflow,
                        sessionFactory: ports => new FreeWAutosaveSession(ports, store),
                        confirmDiscardOrSaveAsync: () => Task.FromResult(true),
                        documentFileWorkflow: documentFileWorkflow);

                    var owner = new Window();
                    await adapter.RecoverUnsavedDocumentsAsync(owner);

                    editor.PlainText.Should().Contain("RECOVERED SNAPSHOT TEXT",
                        "recovery must actually load the snapshot into the current window's editor");
                    workflow.CurrentPath.Should().Be(originalPath);
                });

                if (!ran)
                    return; // no headless drawing backend in this environment

                // Simulate "another program changed the original file while FreeW was crashed/relaunching".
                await Task.Delay(50);
                File.WriteAllText(originalPath, "external writer content");
                File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow);

                var saveRan = await OnUiThread(async () =>
                {
                    var result = await capturedWorkflow!.SavePathAsync(originalPath);

                    promptCount.Should().Be(
                        1,
                        "the guard must ask before overwriting a file that changed on disk since recovery -- " +
                        "before the fix the recovery path never re-armed it, so no prompt was ever raised");
                    result.Succeeded.Should().BeFalse(
                        "declining the overwrite prompt must refuse the save, not silently proceed");
                    result.Outcome.Should().Be(DocumentFileExecutionOutcome.ExternalWriteConflict);
                });

                if (!saveRan)
                    return;

                File.ReadAllText(originalPath).Should().Be(
                    "external writer content",
                    "a declined overwrite must leave the externally-written content on disk untouched");
            }
            finally
            {
                RecoveryPromptDialog.TestResponder = null;
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Sibling no-regression: when the original file has NOT changed on disk since recovery, the
    /// guard must stay quiet and the save must proceed without any prompt -- the fix must not turn
    /// every ordinary recover-then-save into a spurious conflict.
    /// </summary>
    [Fact]
    public async Task RecoverUnsavedDocuments_ThenSave_SavesWithoutPromptWhenOriginalUnchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            var originalPath = Path.Combine(dir, "Report.docx");
            DocxWriter.Write(DocumentWithText("Original content"), originalPath);

            WriteCandidate(store, "snap-clean", "Report.docx", originalPath, DocumentWithText("RECOVERED SNAPSHOT TEXT"));

            var promptCount = 0;
            FreeWDocumentFileWorkflow? capturedWorkflow = null;

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var ran = await OnUiThread(async () =>
                {
                    var editor = new DocumentView();
                    editor.LoadDocument(TextDocument.CreateEmpty());
                    var workflow = NewWorkflow(dir);
                    var documentFileWorkflow = new FreeWDocumentFileWorkflow(
                        workflow,
                        new DocumentPersistenceWorkflow(),
                        new FreeWDocumentFilePorts(
                            GetDocument: () => editor.Document,
                            LoadDocumentAsync: (document, _) =>
                            {
                                editor.LoadDocument(document);
                                return ValueTask.CompletedTask;
                            },
                            ConfirmExternallyModifiedOverwriteAsync: (_, _) =>
                            {
                                promptCount++;
                                return new ValueTask<bool>(true);
                            }));
                    capturedWorkflow = documentFileWorkflow;

                    var adapter = new AutosaveAdapter(
                        editor,
                        workflow,
                        sessionFactory: ports => new FreeWAutosaveSession(ports, store),
                        confirmDiscardOrSaveAsync: () => Task.FromResult(true),
                        documentFileWorkflow: documentFileWorkflow);

                    var owner = new Window();
                    await adapter.RecoverUnsavedDocumentsAsync(owner);
                });

                if (!ran)
                    return;

                var saveRan = await OnUiThread(async () =>
                {
                    var result = await capturedWorkflow!.SavePathAsync(originalPath);

                    promptCount.Should().Be(0, "no external change occurred, so the guard must not prompt");
                    result.Succeeded.Should().BeTrue();
                });

                if (!saveRan)
                    return;
            }
            finally
            {
                RecoveryPromptDialog.TestResponder = null;
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
