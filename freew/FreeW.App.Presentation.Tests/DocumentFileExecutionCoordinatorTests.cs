using System.Text;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentFileExecutionCoordinatorTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(DocumentFileExecutionCoordinatorTests));
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task OpenAsync_OrdersPersistenceProjectionFieldUpdateAndCompletion()
    {
        var events = new List<string>();
        var loadedDocument = Document("opened");
        loadedDocument.UpdateFieldsOnOpen = true;
        var adapter = new RecordingAdapter(".docx", events)
        {
            LoadDocument = loadedDocument,
        };
        var path = Path.Combine(TempDirectory, "Opened.docx");
        await File.WriteAllTextAsync(path, "payload");
        var coordinator = Coordinator(adapter);

        var result = await coordinator.OpenAsync(new DocumentOpenExecutionRequest(
            path,
            SuppressRecentFiles: true,
            LoadDocumentAsync: (document, _) => Record(events, $"load:{document.PlainText.Trim()}"),
            CompleteOpenAsync: (open, suppressRecent, _) =>
                Record(events, $"complete:{Path.GetFileName(open.SavedPath)}:{suppressRecent}"),
            PrepareFieldContextAsync: (savedPath, _) =>
                Record(events, $"field-context:{Path.GetFileName(savedPath)}"),
            UpdateFieldsAsync: _ => Record(events, "update-fields")));

        result.Succeeded.Should().BeTrue();
        result.Operation.Status.Should().Be(OperationStatus.Completed);
        result.Operation.Path.Should().Be(path);
        events.Should().Equal(
            "persist-open",
            "load:opened",
            "field-context:Opened.docx",
            "update-fields",
            "complete:Opened.docx:True");
    }

    [Fact]
    public async Task OpenAsync_UnsupportedFormatDoesNotInvokeNativeCallbacks()
    {
        var events = new List<string>();
        var coordinator = Coordinator(new RecordingAdapter(".docx", events));

        var result = await coordinator.OpenAsync(new DocumentOpenExecutionRequest(
            Path.Combine(TempDirectory, "Unsupported.bin"),
            SuppressRecentFiles: false,
            LoadDocumentAsync: (_, _) => Record(events, "load"),
            CompleteOpenAsync: (_, _, _) => Record(events, "complete")));

        result.Outcome.Should().Be(DocumentFileExecutionOutcome.UnsupportedFormat);
        result.Operation.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Operation.Validation!.Detail.Should().Be(DocumentFileExecutionOutcome.UnsupportedFormat);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_OrdersPrepareCompatibilityPersistenceAndCompletion()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".txt", events);
        var coordinator = Coordinator(adapter);
        var target = Target(adapter, "Saved.txt");

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("save"),
            target,
            DocumentSaveExecutionKind.Save,
            PrepareDocumentAsync: _ => Record(events, "prepare"),
            ConfirmCompatibilityAsync: (plan, _) =>
                Record(events, $"confirm:{plan.RequiresConfirmation}", result: true),
            CompleteSaveAsync: (savedTarget, _) =>
                Record(events, $"complete:{Path.GetFileName(savedTarget.Path)}")));

        result.Succeeded.Should().BeTrue();
        events.Should().Equal("prepare", "confirm:True", "persist-save", "complete:Saved.txt");
        File.ReadAllText(target.Path).Should().Be("save");
    }

    [Fact]
    public async Task SaveAsync_DeclinedCompatibilityStopsBeforePersistenceAndCompletion()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".txt", events);
        var coordinator = Coordinator(adapter);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("save"),
            Target(adapter, "Declined.txt"),
            DocumentSaveExecutionKind.Save,
            PrepareDocumentAsync: _ => Record(events, "prepare"),
            ConfirmCompatibilityAsync: (_, _) => Record(events, "confirm", result: false),
            CompleteSaveAsync: (_, _) => Record(events, "complete")));

        result.Outcome.Should().Be(DocumentFileExecutionOutcome.CompatibilityDeclined);
        result.Operation.Status.Should().Be(OperationStatus.Declined);
        result.Operation.Path.Should().EndWith("Declined.txt");
        events.Should().Equal("prepare", "confirm");
    }

    [Fact]
    public async Task SaveCopyAsync_PersistsWithoutPublishingSavedMetadata()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".docx", events);
        var coordinator = Coordinator(adapter);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("copy"),
            Target(adapter, "Copy.docx"),
            DocumentSaveExecutionKind.SaveCopy,
            PrepareDocumentAsync: _ => Record(events, "prepare"),
            CompleteSaveAsync: (_, _) => Record(events, "complete")));

        result.Succeeded.Should().BeTrue();
        result.Operation.Status.Should().Be(OperationStatus.Completed);
        result.Operation.Path.Should().EndWith("Copy.docx");
        events.Should().Equal("prepare", "persist-save");
    }

    // r137-remediation2 (coordinator level): ExpectedLastWriteTimeUtc is the argument the hosts
    // thread through DocumentSaveExecutionRequest. These pin the coordinator's own decisions --
    // prompt, decline, confirm, and the null-callback default -- independent of any host.
    [Fact]
    public async Task SaveAsync_ExternallyModifiedTarget_DeclinedPromptStopsBeforePersistence()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".txt", events);
        var coordinator = Coordinator(adapter);
        var target = Target(adapter, "Shared.txt");
        var staleWriteTimeUtc = await WriteExternallyModifiedAsync(target.Path);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("my edit"),
            target,
            DocumentSaveExecutionKind.Save,
            ConfirmCompatibilityAsync: (_, _) => Record(events, "confirm-compatibility", result: true),
            CompleteSaveAsync: (_, _) => Record(events, "complete"),
            ExpectedLastWriteTimeUtc: staleWriteTimeUtc,
            ConfirmExternallyModifiedOverwriteAsync: (path, _) =>
                Record(events, $"confirm-overwrite:{Path.GetFileName(path)}", result: false)));

        result.Outcome.Should().Be(DocumentFileExecutionOutcome.ExternalWriteConflict);
        result.Operation.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Operation.Path.Should().Be(target.Path);
        events.Should().Equal("confirm-compatibility", "confirm-overwrite:Shared.txt");
        File.ReadAllText(target.Path).Should().Be(
            "someone else's edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [Fact]
    public async Task SaveAsync_ExternallyModifiedTarget_ConfirmedPromptPersistsOverTheOtherWriter()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".docx", events);
        var coordinator = Coordinator(adapter);
        var target = Target(adapter, "Shared.docx");
        var staleWriteTimeUtc = await WriteExternallyModifiedAsync(target.Path);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("my edit"),
            target,
            DocumentSaveExecutionKind.Save,
            CompleteSaveAsync: (_, _) => Record(events, "complete"),
            ExpectedLastWriteTimeUtc: staleWriteTimeUtc,
            ConfirmExternallyModifiedOverwriteAsync: (_, _) =>
                Record(events, "confirm-overwrite", result: true)));

        result.Succeeded.Should().BeTrue();
        events.Should().Equal("confirm-overwrite", "persist-save", "complete");
        File.ReadAllText(target.Path).Should().Be("my edit");
    }

    // A host that wires no prompt must still refuse the overwrite rather than silently winning the
    // race -- the guard defaults to the safe answer, not to the pre-guard behaviour.
    [Fact]
    public async Task SaveAsync_ExternallyModifiedTargetWithNoPromptCallback_RefusesTheOverwrite()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".docx", events);
        var coordinator = Coordinator(adapter);
        var target = Target(adapter, "Shared.docx");
        var staleWriteTimeUtc = await WriteExternallyModifiedAsync(target.Path);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("my edit"),
            target,
            DocumentSaveExecutionKind.Save,
            CompleteSaveAsync: (_, _) => Record(events, "complete"),
            ExpectedLastWriteTimeUtc: staleWriteTimeUtc));

        result.Outcome.Should().Be(DocumentFileExecutionOutcome.ExternalWriteConflict);
        events.Should().BeEmpty();
        File.ReadAllText(target.Path).Should().Be("someone else's edit");
    }

    // The guard is opt-in per call: Save-As/Save-Copy targets pass null because there is nothing to
    // compare, and must never be blocked by whatever happens to be on disk at that path.
    [Fact]
    public async Task SaveAsync_WithoutExpectedWriteTime_NeverPromptsEvenWhenTheTargetChanged()
    {
        var events = new List<string>();
        var adapter = new RecordingAdapter(".docx", events);
        var coordinator = Coordinator(adapter);
        var target = Target(adapter, "Shared.docx");
        await WriteExternallyModifiedAsync(target.Path);

        var result = await coordinator.SaveAsync(new DocumentSaveExecutionRequest(
            Document("my edit"),
            target,
            DocumentSaveExecutionKind.SaveCopy,
            ExpectedLastWriteTimeUtc: null,
            ConfirmExternallyModifiedOverwriteAsync: (_, _) =>
                Record(events, "confirm-overwrite", result: false)));

        result.Succeeded.Should().BeTrue();
        events.Should().Equal("persist-save");
        File.ReadAllText(target.Path).Should().Be("my edit");
    }

    /// <summary>
    /// Writes <paramref name="path"/> twice with a real mtime change in between and returns the
    /// FIRST (now stale) write time -- what a caller would have captured at open before a second
    /// writer touched the file.
    /// </summary>
    private static async Task<DateTime> WriteExternallyModifiedAsync(string path)
    {
        await File.WriteAllTextAsync(path, "original");
        var openedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "someone else's edit");
        File.SetLastWriteTimeUtc(path, openedWriteTimeUtc + TimeSpan.FromMinutes(1));
        return openedWriteTimeUtc;
    }

    [Fact]
    public void Compatibility_results_map_legacy_outcomes_through_the_shared_envelope()
    {
        var exception = new IOException("save failed");
        var saveAs = new DocumentSaveExecutionResult(
            DocumentFileExecutionOutcome.SaveAsRequired,
            CompatibilityPlan: null,
            Exception: null);
        var failed = new DocumentOpenExecutionResult(
            DocumentFileExecutionOutcome.Failed,
            OpenResult: null,
            Exception: exception);

        saveAs.Outcome.Should().Be(DocumentFileExecutionOutcome.SaveAsRequired);
        saveAs.Operation.Status.Should().Be(OperationStatus.ValidationFailed);
        saveAs.Operation.Validation!.Detail.Should().Be(DocumentFileExecutionOutcome.SaveAsRequired);
        failed.Outcome.Should().Be(DocumentFileExecutionOutcome.Failed);
        failed.Operation.Status.Should().Be(OperationStatus.Failed);
        failed.Operation.Error!.Detail.Should().Be(DocumentFileExecutionOutcome.Failed);
        failed.Exception.Should().BeSameAs(exception);
    }

    private DocumentFileExecutionCoordinator Coordinator(IDocumentFileAdapter adapter) =>
        new(new DocumentPersistenceWorkflow([adapter]));

    private DocumentSaveTarget Target(IDocumentFileAdapter adapter, string name) =>
        new(
            Path.Combine(TempDirectory, name),
            adapter,
            adapter.Formats.Single());

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static ValueTask Record(List<string> events, string value)
    {
        events.Add(value);
        return ValueTask.CompletedTask;
    }

    private static ValueTask<bool> Record(List<string> events, string value, bool result)
    {
        events.Add(value);
        return ValueTask.FromResult(result);
    }

    private sealed class RecordingAdapter(string extension, List<string> events) : IDocumentFileAdapter
    {
        public string Extension => extension;

        public string FormatName => extension == ".txt" ? "Plain Text" : "Word Document";

        public TextDocument LoadDocument { get; init; } = Document("loaded");

        public TextDocument Load(Stream stream)
        {
            events.Add("persist-open");
            return LoadDocument;
        }

        public void Save(TextDocument document, Stream stream)
        {
            events.Add("persist-save");
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(document.PlainText.Trim());
        }
    }
}
