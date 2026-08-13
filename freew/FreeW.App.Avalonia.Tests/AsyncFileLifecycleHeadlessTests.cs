using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class AsyncFileLifecycleHeadlessTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.Avalonia.Tests-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Theory]
    [InlineData(true, "Ada Lovelace")]
    [InlineData(false, "stale author")]
    public async Task StartupDocument_HonorsUpdateFieldsSettingAndRemainsClean(
        bool updateFields,
        string expectedText)
    {
        var documentPath = Path.Combine(TempDirectory, $"UpdateFields-{updateFields}.docx");
        var settingsPath = Path.Combine(TempDirectory, "settings.json");
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.UpdateFieldsOnOpen = updateFields;
        source.Properties.Author = "Ada Lovelace";
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("stale author") { FieldKind = RunFieldKind.Author });
        source.Blocks.Add(paragraph);
        DocxWriter.Write(source, documentPath);

        {
            string? text = null;
            string? currentPath = null;
            var dirty = true;

            await RunOnUiThread(() =>
            {
                var window = new MainWindow(
                    [documentPath],
                    new FreeWOptions(),
                    ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath));
                var callbacks = window.BuildBackstageCallbacks();
                text = window.Editor.PlainText.Trim();
                currentPath = callbacks.CurrentPath;
                dirty = callbacks.GetIsDirty();
                return Task.CompletedTask;
            });

            text.Should().Be(expectedText);
            currentPath.Should().Be(documentPath);
            dirty.Should().BeFalse();
        }
    }

    [Fact]
    public async Task StartupDocument_RetainsPathTitleAndDirectSaveRouting()
    {
        var documentPath = Path.Combine(TempDirectory, "Field Shortcut Fixture.docx");
        var settingsPath = Path.Combine(TempDirectory, "settings.json");
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Startup content"));
        DocxWriter.Write(source, documentPath);

        {
            string? currentPath = null;
            string? displayName = null;
            string? cleanTitle = null;
            var saveResult = false;

            await RunOnUiThread(async () =>
            {
                var window = new MainWindow(
                    [documentPath],
                    new FreeWOptions(),
                    ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath));
                var callbacks = window.BuildBackstageCallbacks();
                currentPath = callbacks.CurrentPath;
                displayName = callbacks.DisplayName;
                cleanTitle = window.Title;

                window.Editor.InsertText("Updated ");
                saveResult = await window.SaveForTests();
            });

            saveResult.Should().BeTrue();
            currentPath.Should().Be(documentPath);
            displayName.Should().Be(Path.GetFileNameWithoutExtension(documentPath));
            cleanTitle.Should().Be($"{Path.GetFileName(documentPath)} \u2014 FreeW");
            DocxReader.Read(documentPath).PlainText.Should().Contain("Updated");
        }
    }

    [Theory]
    [InlineData(SaveChangesPrompt.DontSave, true, false)]
    [InlineData(SaveChangesPrompt.Cancel, false, true)]
    public async Task MainWindow_NewDocumentAsync_PreservesDirtyGateSemantics(
        SaveChangesPrompt prompt,
        bool expectedResult,
        bool expectedDirty)
    {
        var result = false;
        var dirty = false;
        var text = string.Empty;
        var title = string.Empty;
        var beforeText = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(prompt);
            window.Editor.InsertText("FreeW async new sentinel");
            beforeText = window.Editor.PlainText;

            result = await window.NewDocumentAsyncForTests();
            dirty = window.BuildBackstageCallbacks().GetIsDirty();
            text = window.Editor.PlainText;
            title = window.Title ?? string.Empty;

            if (!expectedResult)
                text.Should().Be(beforeText);
        });

        result.Should().Be(expectedResult);
        dirty.Should().Be(expectedDirty);
        if (expectedResult)
        {
            text.Should().BeEmpty();
            title.Should().Contain("FreeW").And.NotContain("*");
        }
        else
        {
            text.Should().Be(beforeText);
            title.Should().Contain("*");
        }
    }

    [Fact]
    public async Task MainWindow_ImportPdfTextAsync_DirtyGateCancelStopsBeforePicker()
    {
        var pickerCalls = 0;
        var imported = true;
        var dirty = false;
        var beforeText = string.Empty;
        var afterText = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = new MainWindow(
                [],
                new FreeWOptions(),
                CreateOptionsStore(),
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.Cancel),
                pickPdfImportPathAsync: () =>
                {
                    pickerCalls++;
                    return Task.FromResult<string?>("ShouldNotOpen.pdf");
                });
            window.Editor.InsertText("Unsaved PDF import sentinel");
            beforeText = window.Editor.PlainText;

            imported = await window.ImportPdfTextAsyncForTests();
            afterText = window.Editor.PlainText;
            dirty = window.BuildBackstageCallbacks().GetIsDirty();
        });

        imported.Should().BeFalse();
        pickerCalls.Should().Be(0);
        afterText.Should().Be(beforeText);
        dirty.Should().BeTrue();
    }

    [Fact]
    public async Task MainWindow_ImportPdfTextAsync_PickerCancelPreservesDocument()
    {
        var imported = true;
        var beforeText = string.Empty;
        var afterText = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = new MainWindow(
                [],
                new FreeWOptions(),
                CreateOptionsStore(),
                pickPdfImportPathAsync: () => Task.FromResult<string?>(null));
            beforeText = window.Editor.PlainText;

            imported = await window.ImportPdfTextAsyncForTests();
            afterText = window.Editor.PlainText;
        });

        imported.Should().BeFalse();
        afterText.Should().Be(beforeText);
    }

    [Fact]
    public async Task MainWindow_ImportPdfTextAsync_DiscardThenUsesSharedPersistenceWorkflow()
    {
        var pdfPath = Path.Combine(TempDirectory, "Imported.pdf");
        await File.WriteAllTextAsync(pdfPath, "Imported through shared persistence");

        {
            var imported = false;
            var dirty = false;
            string? currentPath = "not-cleared";
            var documentText = string.Empty;
            var status = string.Empty;

            await RunOnUiThread(async () =>
            {
                var persistence = new DocumentPersistenceWorkflow(
                    pdfImportAdapters: [new TextPdfImportAdapter()]);
                var window = new MainWindow(
                    [],
                    new FreeWOptions(),
                    CreateOptionsStore(),
                    promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
                    documentPersistence: persistence,
                    pickPdfImportPathAsync: () => Task.FromResult<string?>(pdfPath));
                window.Editor.InsertText("Replace this unsaved text");

                imported = await window.ImportPdfTextAsyncForTests();
                var callbacks = window.BuildBackstageCallbacks();
                dirty = callbacks.GetIsDirty();
                currentPath = callbacks.CurrentPath;
                documentText = window.Editor.PlainText.Trim();
                status = window.CountsStatusForTests;
            });

            imported.Should().BeTrue();
            dirty.Should().BeTrue();
            currentPath.Should().BeNull();
            documentText.Should().Be("Imported through shared persistence");
            status.Should().Be("Imported PDF text from Imported.pdf");
        }
    }

    [Fact]
    public async Task MainWindow_ImportPdfTextAsync_UnsupportedPathPreservesDocumentAndReportsError()
    {
        var imported = true;
        var beforeText = string.Empty;
        var afterText = string.Empty;
        var status = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = new MainWindow(
                [],
                new FreeWOptions(),
                CreateOptionsStore(),
                pickPdfImportPathAsync: () => Task.FromResult<string?>("Unsupported.txt"));
            beforeText = window.Editor.PlainText;

            imported = await window.ImportPdfTextAsyncForTests();
            afterText = window.Editor.PlainText;
            status = window.CountsStatusForTests;
        });

        imported.Should().BeFalse();
        afterText.Should().Be(beforeText);
        status.Should().StartWith("PDF import failed:");
    }

    [Theory]
    [InlineData(SaveChangesPrompt.DontSave, true)]
    [InlineData(SaveChangesPrompt.Cancel, false)]
    public async Task DirtyClose_UsesAsyncConfirmAndRestoresOwnerFocus(
        SaveChangesPrompt prompt,
        bool expectedResume)
    {
        var requestCloseCalls = 0;
        var restoreFocusCalls = 0;
        var resumedCloseCancelled = true;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(prompt);
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? coordinator = null;
            coordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    resumedCloseCancelled = coordinator!.ShouldCancelClosing();
                    settled.TrySetResult();
                },
                restoreOwnerFocus: () =>
                {
                    restoreFocusCalls++;
                    settled.TrySetResult();
                });

            coordinator.ShouldCancelClosing().Should().BeTrue();
            await settled.Task;
        });

        requestCloseCalls.Should().Be(expectedResume ? 1 : 0);
        restoreFocusCalls.Should().Be(expectedResume ? 0 : 1);
        if (expectedResume)
            resumedCloseCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task ReentrantDirtyClose_SharesOneAsyncDecision()
    {
        var prompt = new TaskCompletionSource<SaveChangesPrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCalls = 0;
        var requestCloseCalls = 0;
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await RunOnUiThread(async () =>
        {
            var workflow = CreateWorkflow(promptSaveChangesAsync: _ =>
            {
                promptCalls++;
                return prompt.Task;
            });
            workflow.MarkDirty();
            SisterAvaloniaAsyncWindowCloseCoordinator? coordinator = null;
            coordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
                () => workflow.ConfirmCloseAllowedAsync("closing"),
                requestClose: () =>
                {
                    requestCloseCalls++;
                    coordinator!.ShouldCancelClosing().Should().BeFalse();
                    resumed.TrySetResult();
                },
                restoreOwnerFocus: () => resumed.TrySetException(
                    new InvalidOperationException("Discard should resume close.")));

            coordinator.ShouldCancelClosing().Should().BeTrue();
            coordinator.ShouldCancelClosing().Should().BeTrue();
            await Task.Yield();
            promptCalls.Should().Be(1);
            prompt.SetResult(SaveChangesPrompt.DontSave);
            await resumed.Task;
        });

        promptCalls.Should().Be(1);
        requestCloseCalls.Should().Be(1);
    }

    private MainWindow CreateWindow(SaveChangesPrompt prompt) =>
        new(
            [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(UniqueSettingsPath()),
            promptSaveChangesAsync: _ => Task.FromResult(prompt));

    private ApplicationOptionsStore<FreeWOptions> CreateOptionsStore() =>
        ApplicationOptionsStore<FreeWOptions>.ForPath(UniqueSettingsPath());

    private string UniqueSettingsPath() =>
        Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "settings.json");

    private static SisterAvaloniaFileCommandWorkflow CreateWorkflow(
        SaveChangesPrompt prompt = SaveChangesPrompt.Cancel,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null) =>
        new(
            owner: new Window(),
            titleSpec: new SisterAvaloniaFileTitleSpec("FreeW", " - "),
            maxRecentEntries: static () => 10,
            onChanged: static () => { },
            saveAsync: static () => Task.FromResult(true),
            promptSaveChangesAsync: promptSaveChangesAsync ?? (_ => Task.FromResult(prompt)));

    private static async Task RunOnUiThread(Func<Task> action)
    {
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
    }

    private sealed class TextPdfImportAdapter : IDocumentFileAdapter
    {
        public string Extension => ".pdf";

        public string FormatName => "PDF Document";

        public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
            [new FileFormatDescriptor(".pdf", "PDF Document", CanOpen: true, CanSave: false)];

        public TextDocument Load(Stream stream)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph(reader.ReadToEnd()));
            return document;
        }

        public void Save(TextDocument document, Stream stream) =>
            throw new NotSupportedException();
    }
}
