using System.Text;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWDocumentFileWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWDocumentFileWorkflowTests));
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task OpenPathAsync_LoadsUpdatesFieldsAndPublishesLifecycleMetadata()
    {
        var events = new List<string>();
        var opened = Document("opened");
        opened.UpdateFieldsOnOpen = true;
        var adapter = new RecordingAdapter(".docx", canSave: true, opened);
        var path = Path.Combine(TempDirectory, "Opened.docx");
        await File.WriteAllTextAsync(path, "payload");
        var (workflow, lifecycle) = CreateWorkflow(adapter, events);

        var result = await workflow.OpenPathAsync(path);

        result.Succeeded.Should().BeTrue();
        lifecycle.CurrentPath.Should().Be(path);
        lifecycle.IsDirty.Should().BeFalse();
        lifecycle.RecentEntries.Select(entry => entry.Path).Should().Contain(path);
        events.Should().Equal("load:opened", "file-name:Opened.docx", "update-fields", "changed");
    }

    [Fact]
    public async Task OpenPathAsync_SuppressedRecentStillPublishesCurrentPath()
    {
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("opened"));
        var path = Path.Combine(TempDirectory, "Recovery.docx");
        await File.WriteAllTextAsync(path, "payload");
        var (workflow, lifecycle) = CreateWorkflow(adapter);

        var result = await workflow.OpenPathAsync(path, suppressRecentFiles: true);

        result.Succeeded.Should().BeTrue();
        lifecycle.CurrentPath.Should().Be(path);
        lifecycle.RecentEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SavePathAsync_PublishesNormalSaveButNotSaveCopy()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, lifecycle) = CreateWorkflow(adapter, events, Document("saved"));
        lifecycle.MarkDirty();
        var savePath = Path.Combine(TempDirectory, "Saved.docx");

        var saved = await workflow.SavePathAsync(savePath);
        lifecycle.MarkDirty();
        var copyPath = Path.Combine(TempDirectory, "Copy.docx");
        var copied = await workflow.SavePathAsync(
            copyPath,
            kind: DocumentSaveExecutionKind.SaveCopy);

        saved.Succeeded.Should().BeTrue();
        copied.Succeeded.Should().BeTrue();
        lifecycle.CurrentPath.Should().Be(savePath);
        lifecycle.IsDirty.Should().BeTrue("Save Copy must not clear edits made after the normal save");
        lifecycle.RecentEntries.Select(entry => entry.Path).Should().Contain(savePath).And.NotContain(copyPath);
        File.ReadAllText(savePath).Should().Be("saved");
        File.ReadAllText(copyPath).Should().Be("saved");
        events.Should().ContainInOrder("prepare", "file-name:Saved.docx", "changed", "prepare");
    }

    // r137-remediation2: proves the external-modification guard fires through the REAL entry point
    // (OpenPathAsync captures the write time; a second writer mutates the file on disk; SaveAsync
    // fires the guard on its own) -- nothing in this test passes expectedLastWriteTimeUtc directly,
    // unlike DocumentFileExecutionCoordinatorTests's unit-level coverage of the coordinator itself.
    [Fact]
    public async Task SaveCurrentPathAsync_ExternallyModifiedFile_DeclinedPromptDoesNotOverwrite()
    {
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, _) = CreateWorkflow(adapter, currentDocument: Document("my edit"));
        var path = Path.Combine(TempDirectory, "Shared.docx");
        await File.WriteAllTextAsync(path, "original");

        var opened = await workflow.OpenPathAsync(path);
        opened.Succeeded.Should().BeTrue();

        // Simulate a second writer (another FreeW instance, a sync client) touching the file on
        // disk after we opened it but before we save -- a real mtime change, not a fabricated one.
        await File.WriteAllTextAsync(path, "someone else's edit");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        var declined = await workflow.SaveCurrentPathAsync(path);

        declined.Outcome.Should().Be(DocumentFileExecutionOutcome.ExternalWriteConflict);
        File.ReadAllText(path).Should().Be(
            "someone else's edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [Fact]
    public async Task SaveCurrentPathAsync_ExternallyModifiedFile_ConfirmedPromptOverwrites()
    {
        var confirmedPaths = new List<string>();
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, _) = CreateWorkflow(
            adapter,
            currentDocument: Document("my edit"),
            confirmExternallyModifiedOverwriteAsync: (confirmedPath, _) =>
            {
                confirmedPaths.Add(confirmedPath);
                return ValueTask.FromResult(true);
            });
        var path = Path.Combine(TempDirectory, "Shared.docx");
        await File.WriteAllTextAsync(path, "original");

        var opened = await workflow.OpenPathAsync(path);
        opened.Succeeded.Should().BeTrue();

        await File.WriteAllTextAsync(path, "someone else's edit");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        var confirmed = await workflow.SaveCurrentPathAsync(path);

        confirmed.Succeeded.Should().BeTrue();
        confirmedPaths.Should().Equal(path);
        File.ReadAllText(path).Should().Be("my edit");
    }

    // Save-As to a DIFFERENT path than the one that was opened must never fire the guard: the new
    // target has no prior observation to compare against, even though the ORIGINAL file was changed
    // externally in the meantime.
    [Fact]
    public async Task SavePathAsync_SaveAsToDifferentPath_NeverFiresGuardEvenWhenOriginalWasModified()
    {
        var promptInvoked = false;
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, _) = CreateWorkflow(
            adapter,
            currentDocument: Document("my edit"),
            confirmExternallyModifiedOverwriteAsync: (_, _) =>
            {
                promptInvoked = true;
                return ValueTask.FromResult(false);
            });
        var originalPath = Path.Combine(TempDirectory, "Original.docx");
        await File.WriteAllTextAsync(originalPath, "original");

        var opened = await workflow.OpenPathAsync(originalPath);
        opened.Succeeded.Should().BeTrue();

        await File.WriteAllTextAsync(originalPath, "someone else's edit");
        File.SetLastWriteTimeUtc(originalPath, File.GetLastWriteTimeUtc(originalPath) + TimeSpan.FromMinutes(1));

        var differentPath = Path.Combine(TempDirectory, "SaveAsTarget.docx");
        var savedAs = await workflow.SavePathAsync(differentPath);

        savedAs.Succeeded.Should().BeTrue();
        promptInvoked.Should().BeFalse("Save-As to a different path has no prior observation to compare");
        File.ReadAllText(differentPath).Should().Be("my edit");
    }

    // shared-window-lifecycle F1: ApplyWindowState is the seam View > New Window's host-level
    // LoadDocumentWindow calls to give the new window's OWN workflow instance an external-
    // modification baseline equivalent to what OpenPathAsync would have captured, since New Window
    // never goes through Open. Proves the mechanism directly: establishing the baseline this way,
    // with no OpenPathAsync call at all, still catches a write that happens afterward.
    [Fact]
    public async Task ApplyWindowState_WithExistingPath_EstablishesBaselineThatCatchesALaterExternalWrite()
    {
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, lifecycle) = CreateWorkflow(adapter, currentDocument: Document("window B's edit"));
        var path = Path.Combine(TempDirectory, "Shared.docx");
        await File.WriteAllTextAsync(path, "window A's edit");

        // Mirrors the state LoadDocumentWindow hands a new window: FileCommands.ApplyDocumentState
        // gives the shared path to this workflow's OWN lifecycle (CurrentPath), while
        // ApplyWindowState gives it the matching guard baseline -- this workflow instance never
        // itself called OpenPathAsync/SaveTargetAsync.
        lifecycle.ApplyDocumentState(path, isDirty: false);
        workflow.ApplyWindowState(path);

        // The source window (a different FileCommands/workflow instance) saves to the same path.
        await File.WriteAllTextAsync(path, "window A's SECOND edit");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        var result = await workflow.SaveCurrentPathAsync(path);

        result.Outcome.Should().Be(
            DocumentFileExecutionOutcome.ExternalWriteConflict,
            "without ApplyWindowState this baseline would be null and the write would go through unchecked");
        File.ReadAllText(path).Should().Be("window A's SECOND edit");
    }

    // Sibling/no-regression: a brand-new, never-saved document (CurrentPath null) has nothing to
    // compare against -- ApplyWindowState(null) must leave the guard off, same as a document that
    // was simply never opened.
    [Fact]
    public async Task ApplyWindowState_WithNullPath_NeverFiresGuardOnFirstSave()
    {
        var promptInvoked = false;
        var adapter = new RecordingAdapter(".docx", canSave: true, Document("loaded"));
        var (workflow, _) = CreateWorkflow(
            adapter,
            currentDocument: Document("untitled edit"),
            confirmExternallyModifiedOverwriteAsync: (_, _) =>
            {
                promptInvoked = true;
                return ValueTask.FromResult(false);
            });

        workflow.ApplyWindowState(null);

        var path = Path.Combine(TempDirectory, "FirstSave.docx");
        var result = await workflow.SavePathAsync(path);

        result.Succeeded.Should().BeTrue();
        promptInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCurrentPathAsync_ReadOnlyFormatRequestsSaveAs()
    {
        var adapter = new RecordingAdapter(".legacy", canSave: false, Document("loaded"));
        var (workflow, _) = CreateWorkflow(adapter);

        var result = await workflow.SaveCurrentPathAsync(Path.Combine(TempDirectory, "ReadOnly.legacy"));

        result.RequiresSaveAs.Should().BeTrue();
    }

    [Fact]
    public async Task ImportPdfTextPathAsync_LoadsUntitledDirtyDocument()
    {
        var pdfAdapter = new RecordingAdapter(".pdf", canSave: false, Document("imported"));
        var lifecycle = CreateLifecycle();
        lifecycle.MarkSavedWithPath(Path.Combine(TempDirectory, "Before.docx"), suppressRecentFiles: true);
        var loaded = TextDocument.CreateEmpty();
        var workflow = new FreeWDocumentFileWorkflow(
            lifecycle,
            new DocumentPersistenceWorkflow(adapters: [], pdfImportAdapters: [pdfAdapter]),
            new FreeWDocumentFilePorts(
                () => loaded,
                (document, _) =>
                {
                    loaded = document;
                    return ValueTask.CompletedTask;
                }));
        var path = Path.Combine(TempDirectory, "Imported.pdf");
        await File.WriteAllTextAsync(path, "payload");

        var result = await workflow.ImportPdfTextPathAsync(path);

        result.Succeeded.Should().BeTrue();
        loaded.PlainText.Trim().Should().Be("imported");
        lifecycle.CurrentPath.Should().BeNull();
        lifecycle.IsDirty.Should().BeTrue();
    }

    private (FreeWDocumentFileWorkflow Workflow, FileCommandWorkflow Lifecycle) CreateWorkflow(
        IDocumentFileAdapter adapter,
        List<string>? events = null,
        TextDocument? currentDocument = null,
        Func<string, CancellationToken, ValueTask<bool>>? confirmExternallyModifiedOverwriteAsync = null)
    {
        events ??= [];
        currentDocument ??= TextDocument.CreateEmpty();
        var lifecycle = CreateLifecycle(() => events.Add("changed"));
        var workflow = new FreeWDocumentFileWorkflow(
            lifecycle,
            new DocumentPersistenceWorkflow([adapter]),
            new FreeWDocumentFilePorts(
                () => currentDocument,
                (document, _) =>
                {
                    events.Add($"load:{document.PlainText.Trim()}");
                    return ValueTask.CompletedTask;
                },
                PrepareDocumentAsync: _ =>
                {
                    events.Add("prepare");
                    return ValueTask.CompletedTask;
                },
                UpdateFieldsAsync: _ =>
                {
                    events.Add("update-fields");
                    return ValueTask.CompletedTask;
                },
                SetCurrentFileName: name => events.Add($"file-name:{name}"),
                ConfirmExternallyModifiedOverwriteAsync: confirmExternallyModifiedOverwriteAsync));
        return (workflow, lifecycle);
    }

    private FileCommandWorkflow CreateLifecycle(Action? onChanged = null) =>
        new(
            maxRecentEntries: static () => 10,
            onChanged: onChanged ?? (() => { }),
            promptSaveChanges: static _ => SaveChangesPrompt.DontSave,
            save: static () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(TempDirectory, "recent.json")));

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private sealed class RecordingAdapter(
        string extension,
        bool canSave,
        TextDocument loadDocument) : IDocumentFileAdapter
    {
        public string Extension => extension;

        public string FormatName => extension == ".pdf" ? "PDF Document" : "Test Document";

        public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
            [new(extension, extension == ".pdf" ? "PDF Document" : "Test Document", CanOpen: true, CanSave: canSave)];

        public TextDocument Load(Stream stream) => loadDocument;

        public void Save(TextDocument document, Stream stream)
        {
            if (!canSave)
                throw new NotSupportedException();

            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(document.PlainText.Trim());
        }
    }
}
