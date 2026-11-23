using Avalonia.Headless;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWMacOsFileActivationTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.MacOsActivation-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public Task FileUrlsOpenEveryDistinctDocumentWithoutReplacingUnsavedEdits() => RunOnUiThread(async () =>
    {
        var firstPath = WriteDocx("First document.docx", "first document");
        var secondPath = WriteDocx("Second.docx", "second document");
        var window = CreateWindow();
        var opened = new List<MainWindow>();
        try
        {
            window.Editor.InsertText("unsaved edits");
            var originalText = window.Editor.PlainText;
            opened.AddRange(await window.OpenActivatedPathsAsync(
                [new Uri(firstPath).AbsoluteUri, firstPath, new Uri(secondPath).AbsoluteUri]));

            opened.Should().HaveCount(2);
            opened.Select(item => item.Editor.PlainText).Should().Equal("first document", "second document");
            opened.Should().NotContain(window);
            window.Editor.PlainText.Should().Be(originalText);
            window.Editor.CanUndo.Should().BeTrue();
        }
        finally
        {
            foreach (var child in opened)
                child.Close();
            window.Editor.Undo();
            window.Close();
        }
    });

    [Fact]
    public Task ActivatingAnOpenDocumentPreservesItsUnsavedContent() => RunOnUiThread(async () =>
    {
        var path = WriteDocx("Existing.docx", "existing document");
        var window = CreateWindow([path]);
        try
        {
            window.Editor.InsertText("edited ");
            var editedText = window.Editor.PlainText;

            var opened = await window.OpenActivatedPathsAsync([new Uri(path).AbsoluteUri]);

            opened.Should().ContainSingle().Which.Should().BeSameAs(window);
            window.Editor.PlainText.Should().Be(editedText);
            window.Editor.CanUndo.Should().BeTrue();
        }
        finally
        {
            window.Editor.Undo();
            window.Close();
        }
    });

    [Fact]
    public Task FinderStartupDoesNotDuplicatePendingArgumentWindows() => RunOnUiThread(async () =>
    {
        var firstPath = WriteDocx("First.docx", "first");
        var secondPath = WriteDocx("Pending.docx", "pending");
        var window = CreateWindow([firstPath, secondPath])
            .RememberStartupFileActivations([firstPath, secondPath]);
        try
        {
            // The primary's constructor has planned the second window, but its Opened
            // callback has not created that window when the initial Finder event arrives.
            var opened = await window.OpenActivatedPathsAsync([new Uri(secondPath).AbsoluteUri]);

            opened.Should().BeEmpty();
            window.AdditionalStartupEntriesForTests.Select(entry => entry.Path).Should().Equal(secondPath);
            window.Editor.PlainText.Should().Be("first");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task NonFileUrlsAreIgnoredAndMissingFilesAreReported() => RunOnUiThread(async () =>
    {
        var errors = new List<string>();
        var window = CreateWindow(errors: errors);
        try
        {
            var opened = await window.OpenActivatedPathsAsync(
                ["https://example.invalid/document.docx", Path.Combine(_temporaryDirectory.Path, "Missing.docx")]);

            opened.Should().BeEmpty();
            errors.Should().ContainSingle().Which.Should().Contain("Missing.docx");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ActivationAfterPrimaryClosesUsesTheSurvivingDocumentWindow() => RunOnUiThread(async () =>
    {
        var primaryErrors = new List<string>();
        var survivingErrors = new List<string>();
        var primary = CreateWindow(errors: primaryErrors);
        var survivor = CreateWindow(errors: survivingErrors);
        try
        {
            primary.Show();
            survivor.Show();
            var primaryClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            primary.Closed += (_, _) => primaryClosed.TrySetResult();
            primary.Close();
            // Closing waits for the autosave coordinator before the close resumes. The
            // lifecycle under test begins after Closed, while the survivor remains open.
            await primaryClosed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var owner = App.SelectFileActivationWindow([primary, survivor]);
            owner.Should().BeSameAs(survivor);
            await owner!.ObserveFileActivationAsync(async () =>
            {
                await owner.OpenActivatedPathsAsync([Path.Combine(_temporaryDirectory.Path, "Missing.docx")]);
            });

            primaryErrors.Should().BeEmpty();
            survivingErrors.Should().ContainSingle().Which.Should().Contain("Missing.docx");
        }
        finally
        {
            survivor.Close();
        }
    });

    [Fact]
    public Task StartupTemplateSuppressionExpiresWithoutAnInitialFinderEvent() => RunOnUiThread(async () =>
    {
        var templatePath = Path.Combine(_temporaryDirectory.Path, "Letterhead.dotx");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("letterhead template"));
        using (var stream = File.Create(templatePath))
            DocxFileAdapter.Dotx().Save(document, stream);
        var window = CreateWindow([templatePath]).RememberStartupFileActivations([templatePath]);
        var opened = new List<MainWindow>();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            opened.AddRange(await window.OpenActivatedPathsAsync([new Uri(templatePath).AbsoluteUri]));

            opened.Should().ContainSingle();
            opened[0].Should().NotBeSameAs(window);
            opened[0].Editor.PlainText.Should().Be("letterhead template");
            opened[0].DocumentFileWorkflowForTests.CurrentPathForTests.Should().BeNull(
                "a later Finder gesture creates a new untitled document from the template");
        }
        finally
        {
            foreach (var child in opened)
                child.Close();
            window.Close();
        }
    });

    private MainWindow CreateWindow(IReadOnlyList<string>? paths = null, List<string>? errors = null) =>
        new(
            paths ?? [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(
                Path.Combine(_temporaryDirectory.Path, Guid.NewGuid().ToString("N"), "settings.json")),
            promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
            showFileCommandErrorAsync: (_, error) =>
            {
                errors?.Add(error.Message);
                return Task.CompletedTask;
            },
            suppressStartupRecoveryOffer: true);

    private string WriteDocx(string fileName, string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_temporaryDirectory.Path, fileName);
        DocxWriter.Write(document, path);
        return path;
    }

    private static async Task RunOnUiThread(Func<Task> action) =>
        await Session.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
}
