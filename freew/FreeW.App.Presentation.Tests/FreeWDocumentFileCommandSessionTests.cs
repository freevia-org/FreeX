using System.Text;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWDocumentFileCommandSessionTests : IDisposable
{
    private static readonly SisterAppFileTextSpec Text = new(
        "Open document",
        "Save document",
        "Document",
        "creating a new document",
        "opening another document",
        "opening a document",
        "saving a document",
        "inserting a picture",
        "Insert picture",
        new SisterAppFileStatusTextSpec(
            "{0} unavailable.",
            "{0} requires a local path.",
            "{0} does not support {1}.",
            "Unsupported extension {0}.",
            "{0} failed: {1}",
            "Opened {0}",
            "Saved {0}",
            "Inserted {0}",
            "Save as {0}"));

    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWDocumentFileCommandSessionTests));

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task OpenAsync_OwnsPickerExecutionAndFeedbackCeremony()
    {
        var path = Path.Combine(_temporaryDirectory.Path, "Opened.docx");
        await File.WriteAllTextAsync(path, "payload");
        var lifecycle = CreateLifecycle();
        var loaded = TextDocument.CreateEmpty();
        var feedback = new List<FreeWDocumentFileFeedback>();
        var workflow = CreateWorkflow(
            lifecycle,
            [new RecordingAdapter(".docx", canSave: true, Document("opened"))],
            () => loaded,
            document => loaded = document);
        var session = CreateSession(
            workflow,
            lifecycle,
            pickOpenPathAsync: _ => Task.FromResult<string?>(path),
            presentFeedback: feedback.Add);

        var opened = await session.OpenAsync();

        opened.Should().BeTrue();
        loaded.PlainText.Trim().Should().Be("opened");
        lifecycle.CurrentPath.Should().Be(path);
        feedback.Should().ContainSingle(item => item.Succeeded && item.Message.Contains("Opened.docx"));
    }

    [Fact]
    public async Task SaveAsync_ReadOnlyCurrentFormatFallsThroughToSharedSaveAsPicker()
    {
        var lifecycle = CreateLifecycle();
        var legacyPath = Path.Combine(_temporaryDirectory.Path, "ReadOnly.legacy");
        lifecycle.MarkDirtyWithPath(legacyPath);
        var savePath = Path.Combine(_temporaryDirectory.Path, "Saved.docx");
        FreeWDocumentSavePickerRequest? pickerRequest = null;
        var document = Document("saved");
        var workflow = CreateWorkflow(
            lifecycle,
            [
                new RecordingAdapter(".legacy", canSave: false, document),
                new RecordingAdapter(".docx", canSave: true, document),
            ],
            () => document,
            _ => { });
        var session = CreateSession(
            workflow,
            lifecycle,
            pickSaveTargetAsync: request =>
            {
                pickerRequest = request;
                return Task.FromResult<FreeWDocumentSavePickerResult?>(new(savePath));
            });

        var saved = await session.SaveAsync();

        saved.Should().BeTrue();
        pickerRequest.Should().NotBeNull();
        pickerRequest!.CurrentPath.Should().Be(legacyPath);
        pickerRequest.Title.Should().Be(Text.SavePickerTitle);
        File.ReadAllText(savePath).Should().Be("saved");
        lifecycle.CurrentPath.Should().Be(savePath);
    }

    [Fact]
    public async Task SaveCopyAsync_UsesCopyTitleWithoutChangingCurrentPath()
    {
        var lifecycle = CreateLifecycle();
        var currentPath = Path.Combine(_temporaryDirectory.Path, "Current.docx");
        lifecycle.MarkSavedWithPath(currentPath, suppressRecentFiles: true);
        var copyPath = Path.Combine(_temporaryDirectory.Path, "Copy.docx");
        FreeWDocumentSavePickerRequest? pickerRequest = null;
        var document = Document("copy");
        var workflow = CreateWorkflow(
            lifecycle,
            [new RecordingAdapter(".docx", canSave: true, document)],
            () => document,
            _ => { });
        var session = CreateSession(
            workflow,
            lifecycle,
            pickSaveTargetAsync: request =>
            {
                pickerRequest = request;
                return Task.FromResult<FreeWDocumentSavePickerResult?>(new(copyPath));
            });

        var saved = await session.SaveCopyAsync();

        saved.Should().BeTrue();
        pickerRequest!.Title.Should().Be(FreeWDocumentFileFeedbackPlanner.SaveCopyCommand);
        lifecycle.CurrentPath.Should().Be(currentPath);
        File.ReadAllText(copyPath).Should().Be("copy");
    }

    private FreeWDocumentFileCommandSession CreateSession(
        FreeWDocumentFileWorkflow workflow,
        FileCommandWorkflow lifecycle,
        Func<FreeWDocumentOpenPickerRequest, Task<string?>>? pickOpenPathAsync = null,
        Func<FreeWDocumentSavePickerRequest, Task<FreeWDocumentSavePickerResult?>>? pickSaveTargetAsync = null,
        Action<FreeWDocumentFileFeedback>? presentFeedback = null) =>
        new(
            workflow,
            new FreeWFileCommandLifecyclePorts(
                () => lifecycle.CurrentPath,
                () => lifecycle.CurrentFileName,
                (action, loadAsync) => lifecycle.NewAsync(action, loadAsync),
                lifecycle.OpenAsync,
                lifecycle.SaveAsync),
            new FreeWDocumentFileCommandPorts(
                () => Task.CompletedTask,
                pickOpenPathAsync ?? (_ => Task.FromResult<string?>(null)),
                () => Task.FromResult<string?>(null),
                pickSaveTargetAsync ?? (_ => Task.FromResult<FreeWDocumentSavePickerResult?>(null)),
                presentFeedback ?? (_ => { })),
            Text);

    private FreeWDocumentFileWorkflow CreateWorkflow(
        FileCommandWorkflow lifecycle,
        IReadOnlyList<IDocumentFileAdapter> adapters,
        Func<TextDocument> getDocument,
        Action<TextDocument> loadDocument) =>
        new(
            lifecycle,
            new DocumentPersistenceWorkflow(adapters),
            new FreeWDocumentFilePorts(
                getDocument,
                (document, _) =>
                {
                    loadDocument(document);
                    return ValueTask.CompletedTask;
                }));

    private FileCommandWorkflow CreateLifecycle() =>
        new(
            static () => 10,
            static () => { },
            static _ => SaveChangesPrompt.DontSave,
            static () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(_temporaryDirectory.Path, "recent.json")));

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

        public string FormatName => "Test document";

        public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
            [new(extension, "Test document", CanOpen: true, CanSave: canSave)];

        public TextDocument Load(Stream stream) => loadDocument;

        public void Save(TextDocument document, Stream stream)
        {
            var bytes = Encoding.UTF8.GetBytes(document.PlainText.Trim());
            stream.Write(bytes);
        }
    }
}
