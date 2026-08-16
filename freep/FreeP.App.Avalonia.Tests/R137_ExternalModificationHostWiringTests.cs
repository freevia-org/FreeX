using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// r137-remediation2, host level: the Avalonia shell's half of FreeP's external-modification
/// wiring. The session-level tests pin the mechanism; these prove the real window captures the
/// write time at open and routes the conflict to its message service instead of overwriting a file
/// another program changed underneath it.
/// </summary>
public sealed class R137_ExternalModificationHostWiringTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.Avalonia.R137-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task Save_AfterAnotherProgramChangedTheFile_DeclinedPromptLeavesItIntact()
    {
        var path = WritePptx("Shared.pptx", "original");
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.No };
        var saved = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(messages);
            (await window.OpenPathAsyncForTests(path)).Should().BeTrue();

            window.PresentationForTests.Properties.Title = "my edit";
            window.MarkDirtyForTests();
            WriteExternalChange(path, "someone else's edit");

            saved = await window.FileSaveAsyncForTests();
        });

        saved.Should().BeFalse();
        messages.Messages.Should().ContainSingle();
        messages.Messages[0].Buttons.Should().Be(UserMessageButtons.YesNo);
        messages.Messages[0].Title.Should().Be("FreeP");
        messages.Messages[0].Message.Should().Contain("Shared.pptx");
        PptxPackageReader.Read(path).Properties.Title.Should().Be(
            "someone else's edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [Fact]
    public async Task Save_AfterAnotherProgramChangedTheFile_ConfirmedPromptOverwrites()
    {
        var path = WritePptx("Shared.pptx", "original");
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.Yes };
        var saved = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(messages);
            (await window.OpenPathAsyncForTests(path)).Should().BeTrue();

            window.PresentationForTests.Properties.Title = "my edit";
            window.MarkDirtyForTests();
            WriteExternalChange(path, "someone else's edit");

            saved = await window.FileSaveAsyncForTests();
        });

        saved.Should().BeTrue();
        messages.Messages.Should().ContainSingle();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my edit");
    }

    [Fact]
    public async Task Save_WhenTheFileWasNotChanged_NeverPrompts()
    {
        var path = WritePptx("Untouched.pptx", "original");
        var messages = new RecordingUserMessageService();
        var saved = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(messages);
            (await window.OpenPathAsyncForTests(path)).Should().BeTrue();

            window.PresentationForTests.Properties.Title = "my edit";
            window.MarkDirtyForTests();

            saved = await window.FileSaveAsyncForTests();
        });

        saved.Should().BeTrue();
        messages.Messages.Should().BeEmpty();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my edit");
    }

    private MainWindow CreateWindow(IUserMessageService messageService) =>
        new(
            Array.Empty<string>(),
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "recent.json")),
            messageService: messageService);

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(TempDirectory, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    /// <summary>
    /// Simulates a second writer (another FreeP/PowerPoint instance, a sync client, a colleague on
    /// a shared path) touching the file after it was opened, with a real mtime change.
    /// </summary>
    private static void WriteExternalChange(string path, string title)
    {
        var beforeWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        File.SetLastWriteTimeUtc(path, beforeWriteTimeUtc + TimeSpan.FromMinutes(1));
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }

    private static async Task RunOnUiThread(Func<Task> action) =>
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
}
