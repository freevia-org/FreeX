using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r137-remediation2, host level: the Avalonia shell's half of the external-modification wiring.
/// The workflow- and coordinator-level tests pin the mechanism; these prove the real window
/// captures the write time at open and routes the conflict to its message service, rather than
/// overwriting a file another program changed underneath it.
/// </summary>
public sealed class R137_ExternalModificationHostWiringTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.Avalonia.R137-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task SaveAsync_AfterAnotherProgramChangedTheFile_DeclinedPromptLeavesItIntact()
    {
        var documentPath = WriteDocx("Shared.docx", "original");
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.No };
        var saved = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(documentPath, messages);
            window.Editor.InsertText("my edit ");
            WriteExternalChange(documentPath, "someone else's edit");

            saved = await window.SaveForTests();
        });

        saved.Should().BeFalse();
        messages.Messages.Should().ContainSingle().Which.Buttons.Should().Be(UserMessageButtons.YesNo);
        messages.Messages[0].Message.Should().Contain("Shared.docx");
        messages.Messages[0].Title.Should().Be("FreeW");
        DocxReader.Read(documentPath).PlainText.Should().Contain(
            "someone else's edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [Fact]
    public async Task SaveAsync_AfterAnotherProgramChangedTheFile_ConfirmedPromptOverwrites()
    {
        var documentPath = WriteDocx("Shared.docx", "original");
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.Yes };
        var saved = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(documentPath, messages);
            window.Editor.InsertText("my edit ");
            WriteExternalChange(documentPath, "someone else's edit");

            saved = await window.SaveForTests();
        });

        saved.Should().BeTrue();
        messages.Messages.Should().ContainSingle();
        DocxReader.Read(documentPath).PlainText.Should().Contain("my edit");
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileWasNotChanged_NeverPrompts()
    {
        var documentPath = WriteDocx("Untouched.docx", "original");
        var messages = new RecordingUserMessageService();
        var saved = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(documentPath, messages);
            window.Editor.InsertText("my edit ");

            saved = await window.SaveForTests();
        });

        saved.Should().BeTrue();
        messages.Messages.Should().BeEmpty();
        DocxReader.Read(documentPath).PlainText.Should().Contain("my edit");
    }

    private MainWindow CreateWindow(string documentPath, IUserMessageService messageService) =>
        new(
            [documentPath],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(
                Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "settings.json")),
            suppressStartupRecoveryOffer: true,
            messageService: messageService);

    private string WriteDocx(string name, string text)
    {
        var path = Path.Combine(TempDirectory, name);
        DocxWriter.Write(Document(text), path);
        return path;
    }

    /// <summary>
    /// Simulates a second writer (another FreeW/Word instance, a sync client, a colleague on a
    /// shared path) touching the file after it was opened, with a real mtime change.
    /// </summary>
    private static void WriteExternalChange(string path, string text)
    {
        var beforeWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        DocxWriter.Write(Document(text), path);
        File.SetLastWriteTimeUtc(path, beforeWriteTimeUtc + TimeSpan.FromMinutes(1));
    }

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
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
