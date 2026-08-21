using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// shared-window-lifecycle F1, host level: View &gt; New Window (<see cref="FileCommands.LoadDocumentWindow"/>,
/// driven from <c>MainWindow.OpenNewWindow</c>) hands the new window an independent in-memory snapshot
/// while keeping the SAME <see cref="FileCommands.CurrentPath"/> as the source window
/// (<see cref="FreeWDocumentWindowPlanner"/> class remarks). Before the fix, <c>LoadDocumentWindow</c>
/// only called <c>SisterWpfFileCommandWorkflow.ApplyDocumentState</c> and never touched the new
/// window's own <see cref="FreeWDocumentFileWorkflow"/>, so its external-modification guard baseline
/// stayed at its default null forever -- the guard is gated behind
/// <c>expectedLastWriteTimeUtc is {{ }}</c> (DocumentFileExecutionCoordinator/DocumentPersistenceWorkflow),
/// so a null baseline means the new window's first save always writes straight through with zero
/// conflict detection, even when the source window saved to the same path in the meantime.
/// </summary>
public sealed class R160_NewWindowSaveConflictBaselineTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory =
        new("FreeW.R160NewWindowConflict-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // The exact user gesture from the finding: window A opens a shared file, spins off a second
    // window via View > New Window, window A saves an edit (mutating the shared path on disk),
    // then window B -- unaware of that intervening save -- tries to save its own edit. Before the
    // fix this silently clobbered window A's save; the guard must now prompt and, on decline,
    // must leave window A's save intact.
    [StaFact]
    public void NewWindowSecondSave_AfterSourceWindowSavedInBetween_PromptsAndDeclinedKeepsFirstSave()
    {
        var (windowA, editorA, messagesA) = CreateHarness();
        var path = WriteDocx("Shared.docx", "original");
        Assert.True(windowA.OpenPath(path));

        // Mirrors MainWindow.OpenNewWindow(): _documentWindowPlanner.CreateNext(_editor.Model,
        // _file.CurrentPath, _file.IsDirty) then newWindow._file.LoadDocumentWindow(plan).
        var planner = new FreeWDocumentWindowPlanner();
        var plan = planner.CreateNext(editorA.Model, windowA.CurrentPath, windowA.IsDirty);
        var (windowB, editorB, messagesB) = CreateHarness();
        windowB.LoadDocumentWindow(plan);

        Assert.Equal(path, windowB.CurrentPath);

        // Window A edits and saves -- this is the "someone else changed the file" event from
        // window B's point of view, produced by the normal Save path (rebases A's OWN baseline,
        // irrelevant here) rather than a fabricated external writer.
        editorA.LoadModel(Document("window A's edit"));
        windowA.MarkDirty();
        Assert.True(windowA.Save());
        // Force a real, detectable mtime delta regardless of filesystem timestamp resolution.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        // Window B, still on its pre-edit snapshot, edits and saves.
        editorB.LoadModel(Document("window B's edit"));
        windowB.MarkDirty();
        messagesB.NextResult = UserMessageResult.No;

        var savedFromB = windowB.Save();

        Assert.False(savedFromB, "a declined external-modification prompt must not let window B overwrite window A's save");
        var prompt = Assert.Single(messagesB.Messages);
        Assert.Equal(UserMessageButtons.YesNo, prompt.Buttons);
        Assert.Contains("Shared.docx", prompt.Message);
        Assert.Equal(
            "window A's edit",
            DocxReader.Read(path).PlainText.Trim());
    }

    // Sibling/no-regression: a brand-new, never-saved document (CurrentPath null) opened via New
    // Window must NOT spuriously prompt on its first save -- there is no prior path to compare
    // against, matching the existing Save-As/never-saved behaviour the guard already carves out.
    // (SaveCopyToPath is the same headless-testable, dialog-free write path R137's own host-level
    // tests use for this reason.)
    [StaFact]
    public void NewWindowForUnsavedDocument_FirstSaveNeverPrompts()
    {
        var (windowA, editorA, _) = CreateHarness();
        editorA.LoadModel(Document("untitled content"));

        var planner = new FreeWDocumentWindowPlanner();
        var plan = planner.CreateNext(editorA.Model, windowA.CurrentPath, windowA.IsDirty);
        Assert.Null(plan.CurrentPath);

        var (windowB, editorB, messagesB) = CreateHarness();
        windowB.LoadDocumentWindow(plan);
        Assert.Null(windowB.CurrentPath);

        editorB.LoadModel(Document("window B's untitled edit"));
        var savePath = Path.Combine(TempDirectory, "FirstSave.docx");

        Assert.True(windowB.SaveCopyToPath(savePath));

        Assert.Empty(messagesB.Messages);
        Assert.Equal("window B's untitled edit", DocxReader.Read(savePath).PlainText.Trim());
    }

    private (FileCommands File, DocumentView Editor, RecordingUserMessageService Messages) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var messages = new RecordingUserMessageService();
        var file = new FileCommands(
            window,
            editor,
            () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(TempDirectory, Guid.NewGuid().ToString("N") + ".json")),
            messageService: messages,
            confirmSaveCompatibility: _ => true);
        return (file, editor, messages);
    }

    private string WriteDocx(string name, string text)
    {
        var path = Path.Combine(TempDirectory, name);
        DocxWriter.Write(Document(text), path);
        return path;
    }

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }
}
