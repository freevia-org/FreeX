using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// round148/failure-surfaces F1 + startup-fileopen F1: the Avalonia shell used to swallow real
/// Save/Open/Import failures (and a failed startup-argument open) into a status-bar-only line that a
/// user mid-typing or looking away would never see, while the WPF host already pops a modal error for
/// the identical gestures. <see cref="MainWindow"/> now routes both through the same
/// <c>showFileCommandErrorAsync</c> port the shared <c>SisterAvaloniaFileCommandWorkflow</c> already
/// exposed but never wired into these two paths.
/// </summary>
public sealed class R148_FileCommandErrorSurfacingTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.Avalonia.R148-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ---- failure-surfaces F1: Save/Open/Import failures must reach a modal, not just the status bar ----

    [Fact]
    public async Task SaveCopyToPathAsync_WhenTheAdapterWriteFails_RoutesTheRealErrorToTheModalPort()
    {
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();
        var targetPath = Path.Combine(TempDirectory, "Copy.docx");
        var saveResult = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(
                errorCalls,
                documentPersistence: new DocumentPersistenceWorkflow([new ThrowingSaveAdapter()]));
            window.Editor.InsertText("content that needs saving");

            saveResult = await window.SaveCopyToPathAsync(targetPath);
        });

        saveResult.Should().BeFalse();
        errorCalls.Should().ContainSingle(
            "a real save failure (disk full/locked/permission-denied -- simulated here via an " +
            "adapter that throws mid-write, the same shape DocxWriter fails in for real) must reach " +
            "the modal error port, not silently disappear into the status bar the way it did before " +
            "this fix");
        errorCalls[0].Summary.Should().Be("Could not save a copy");
        errorCalls[0].ExceptionMessage.Should().Contain(ThrowingSaveAdapter.FailureMessage);
    }

    [Fact]
    public async Task SaveCopyToPathAsync_WhenTheAdapterWriteFails_StillUpdatesTheStatusBarToo()
    {
        // Sibling/no-regression: the pre-existing status-bar text on failure must be unchanged --
        // this fix ADDS the modal, it does not replace the status line.
        var targetPath = Path.Combine(TempDirectory, "Copy.docx");
        var status = string.Empty;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(
                [],
                documentPersistence: new DocumentPersistenceWorkflow([new ThrowingSaveAdapter()]));
            window.Editor.InsertText("content that needs saving");

            await window.SaveCopyToPathAsync(targetPath);
            status = window.CountsStatusForTests;
        });

        status.Should().Be("Save a Copy failed: simulated write failure");
    }

    [Fact]
    public async Task SaveCopyToPathAsync_OnSuccess_NeverCallsTheModalErrorPort()
    {
        // Sibling/no-regression: a successful save must not spuriously pop an error dialog.
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();
        var goodPath = Path.Combine(TempDirectory, "Copy.docx");
        var saveResult = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls);
            window.Editor.InsertText("content that needs saving");

            saveResult = await window.SaveCopyToPathAsync(goodPath);
        });

        saveResult.Should().BeTrue();
        errorCalls.Should().BeEmpty();
    }

    // ---- startup-fileopen F1: a startup argument that fails to open must not be totally silent ----

    [Fact]
    public async Task StartupOpenFailure_ForAMissingStartupArgument_RoutesToTheModalPort()
    {
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();
        var missingPath = Path.Combine(TempDirectory, "Missing.docx");
        var startupOpenFailed = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls, startupArguments: [missingPath]);
            startupOpenFailed = window.StartupOpenFailedForTests;

            await window.ShowStartupOpenFailureForTests();
        });

        startupOpenFailed.Should().BeTrue(
            "a missing startup argument must be recognized as a failed open, not treated the same " +
            "as no argument at all");
        errorCalls.Should().ContainSingle(
            "a startup file-association/command-line argument that could not be opened must alert " +
            "the user instead of silently opening a blank document with zero indication anything " +
            "went wrong");
        errorCalls[0].Summary.Should().Be("Could not open the document");
        errorCalls[0].ExceptionMessage.Should().Contain("Missing.docx");
    }

    [Fact]
    public async Task StartupOpenFailure_WithNoStartupArguments_StaysSilent()
    {
        // Sibling/no-regression: a plain launch with no requested file must keep falling back to the
        // blank sample document with NO alert -- there was nothing the user asked to open.
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();
        var startupOpenFailed = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls, startupArguments: []);
            startupOpenFailed = window.StartupOpenFailedForTests;

            await window.ShowStartupOpenFailureForTests();
        });

        startupOpenFailed.Should().BeFalse();
        errorCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task StartupOpenFailure_ForAnExistingSupportedStartupArgument_StaysSilent()
    {
        // Sibling/no-regression: the ordinary "double-click a real .docx" case must not be flagged as
        // a failure just because SOME startup argument was supplied.
        var documentPath = Path.Combine(TempDirectory, "Real.docx");
        FreeW.Core.IO.DocxWriter.Write(FreeW.Core.Model.TextDocument.CreateEmpty(), documentPath);
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();
        var startupOpenFailed = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls, startupArguments: [documentPath]);
            startupOpenFailed = window.StartupOpenFailedForTests;

            await window.ShowStartupOpenFailureForTests();
        });

        startupOpenFailed.Should().BeFalse();
        errorCalls.Should().BeEmpty();
    }

    private MainWindow CreateWindow(
        List<(string Summary, string ExceptionMessage)> errorCalls,
        IReadOnlyList<string>? startupArguments = null,
        DocumentPersistenceWorkflow? documentPersistence = null) =>
        new(
            startupArguments ?? [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(UniqueSettingsPath()),
            documentPersistence: documentPersistence,
            showFileCommandErrorAsync: (summary, exception) =>
            {
                errorCalls.Add((summary, exception.Message));
                return Task.CompletedTask;
            });

    private string UniqueSettingsPath() =>
        Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "settings.json");

    private static async Task RunOnUiThread(Func<Task> action) =>
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);

    /// <summary>
    /// Stands in for a real disk-full/locked-file/permission-denied Save failure: a normal-looking
    /// .docx adapter whose write throws partway through, exercised through the exact same
    /// AtomicFileWriter -> DocumentFileExecutionCoordinator.SaveAsync path a genuine IO failure hits.
    /// </summary>
    private sealed class ThrowingSaveAdapter : IDocumentFileAdapter
    {
        public const string FailureMessage = "simulated write failure";

        public string Extension => ".docx";

        public string FormatName => "Word Document";

        public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
            [new FileFormatDescriptor(".docx", "Word Document")];

        public TextDocument Load(Stream stream) => throw new NotSupportedException();

        public void Save(TextDocument document, Stream stream) =>
            throw new IOException(FailureMessage);
    }
}
