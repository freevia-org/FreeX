using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// shared-startup-args F1: <see cref="FreeWApplicationStartup.TryOpenStartupDocument"/> used to be
/// called with <c>MaximumOpenableFiles: 1</c>, so <see cref="MainWindow"/>'s constructor only ever
/// looked at the FIRST resolvable startup-argument path -- every file beyond it (e.g. several
/// documents dragged onto the dock/taskbar icon in one launch, which the OS delivers as multiple path
/// arguments to a single process) was silently dropped, with no error, no window, and no diagnostic --
/// despite FreeW's own Linux packaging (<c>Exec=freew %F</c>) declaring multi-file support, and every
/// other shell in this codebase (FreeX, FreeP, FreeW's own WPF host) opening every one, each in its own
/// window.
///
/// <para>
/// Driving the real <c>IClassicDesktopStyleApplicationLifetime</c> startup path end-to-end (real
/// windows actually appearing on screen) is impractical from a headless unit test -- this harness's
/// <c>FreeWHeadlessApp</c> is a bare <c>Application</c>, not a classic desktop lifetime, so
/// <c>Show()</c> on a second <see cref="MainWindow"/> does not register anywhere a test can observe it
/// (see FreeX's own <c>R159_AvaloniaStartupFileOpenDuplicationTests</c> for the same constraint).
/// Instead these drive the REAL production planning + per-window opening logic
/// (<see cref="FreeWApplicationStartup.PlanStartupDocuments"/> and the primary window's own
/// <see cref="MainWindow.AdditionalStartupEntriesForTests"/>, populated by the exact constructor code
/// path <c>Program</c>/<c>App.cs</c> reach in production) rather than asserting on window counts.
/// </para>
/// </summary>
public sealed class R169_AvaloniaMultiFileStartupArgsTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.Avalonia.R169-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // The exact user gesture from the finding: several distinct files in one launch. The primary
    // window must open the first, and every remaining one must be queued for its own new window --
    // not silently dropped.
    [Fact]
    public async Task SeveralDistinctStartupFiles_OpensTheFirstAndQueuesEveryRemainingOneForItsOwnWindow()
    {
        var firstPath = WriteDocx("First.docx", "first body");
        var secondPath = WriteDocx("Second.docx", "second body");
        var thirdPath = WriteDocx("Third.docx", "third body");
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls, startupArguments: [firstPath, secondPath, thirdPath]);

            window.Editor.Document.PlainText.Should().Be("first body");
            window.StartupOpenFailedForTests.Should().BeFalse();
            window.AdditionalStartupEntriesForTests.Select(entry => entry.Path)
                .Should().Equal(secondPath, thirdPath);
            window.AdditionalStartupEntriesForTests.Should().OnlyContain(entry => entry.OpenInNewWindow);
            await ShowStartupOpenFailureForTests(window);
        });

        errorCalls.Should().BeEmpty();
    }

    // The other half of the same finding: the SAME path given twice in argv (multi-selecting one file
    // and dragging it onto the dock/taskbar icon, which delivers one launch with the path duplicated)
    // must collapse to a single window, not queue a second, unsynchronized one on the same document.
    [Fact]
    public async Task TheSamePathGivenTwice_CollapsesToTheSinglePrimaryWindowWithNoAdditionalEntry()
    {
        var path = WriteDocx("Repeated.docx", "repeated body");
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(errorCalls, startupArguments: [path, path]);

            window.Editor.Document.PlainText.Should().Be("repeated body");
            window.StartupOpenFailedForTests.Should().BeFalse();
            window.AdditionalStartupEntriesForTests.Should().BeEmpty(
                "a path repeated in argv must resolve to a single window, not one window per " +
                "occurrence -- otherwise two windows independently edit the same file with no " +
                "\"already open elsewhere\" warning");
            await ShowStartupOpenFailureForTests(window);
        });

        errorCalls.Should().BeEmpty();
    }

    // Sibling no-regression: a startup path that does not exist on disk, mixed in among distinct
    // valid ones, must neither crash planning nor spawn a phantom window for itself -- it is simply
    // skipped, and every valid file around it still opens normally.
    [Fact]
    public async Task AMissingStartupPathAmongValidOnes_IsSkippedWithoutLosingTheValidFiles()
    {
        var firstPath = WriteDocx("ValidFirst.docx", "valid first body");
        var missingPath = Path.Combine(TempDirectory, "Missing.docx");
        var secondPath = WriteDocx("ValidSecond.docx", "valid second body");
        var errorCalls = new List<(string Summary, string ExceptionMessage)>();

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(
                errorCalls,
                startupArguments: [firstPath, missingPath, secondPath]);

            window.Editor.Document.PlainText.Should().Be("valid first body");
            window.StartupOpenFailedForTests.Should().BeFalse();
            window.AdditionalStartupEntriesForTests.Select(entry => entry.Path).Should().Equal(secondPath);
            await ShowStartupOpenFailureForTests(window);
        });

        errorCalls.Should().BeEmpty();
    }

    private MainWindow CreateWindow(
        List<(string Summary, string ExceptionMessage)> errorCalls,
        IReadOnlyList<string>? startupArguments = null) =>
        new(
            startupArguments ?? [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(UniqueSettingsPath()),
            showFileCommandErrorAsync: (summary, exception) =>
            {
                errorCalls.Add((summary, exception.Message));
                return Task.CompletedTask;
            });

    private string UniqueSettingsPath() =>
        Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "settings.json");

    private static Task ShowStartupOpenFailureForTests(MainWindow window) =>
        window.ShowStartupOpenFailureForTests();

    private static async Task RunOnUiThread(Func<Task> action) =>
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);

    private string WriteDocx(string fileName, string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(TempDirectory, fileName);
        DocxWriter.Write(document, path);
        return path;
    }
}
