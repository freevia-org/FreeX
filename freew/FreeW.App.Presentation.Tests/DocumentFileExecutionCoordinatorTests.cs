using System.Text;
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
        events.Should().Equal("prepare", "persist-save");
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
