using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r137-remediation2, host level: proves FreeW's WPF shell actually reaches the
/// external-modification guard through its own public entry points. The workflow- and
/// coordinator-level tests pin the mechanism; these pin the WIRING -- that
/// <see cref="FileCommands"/> supplies the write time it captured at open, and that the resulting
/// conflict surfaces as the shared prompt on the injected <see cref="IUserMessageService"/> rather
/// than silently overwriting the other writer's file.
/// </summary>
public sealed class R137_ExternalModificationHostWiringTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory =
        new("FreeW.R137ExternalModification-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [StaFact]
    public void Save_AfterAnotherProgramChangedTheFile_PromptsAndDeclinedLeavesTheOtherWriterIntact()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Shared.docx", "original");
        Assert.True(file.OpenPath(path));

        editor.LoadModel(Document("my edit"));
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        messages.NextResult = UserMessageResult.No;

        var saved = file.Save();

        Assert.False(saved);
        var prompt = Assert.Single(messages.Messages);
        Assert.Equal(UserMessageButtons.YesNo, prompt.Buttons);
        Assert.Equal(UserMessageIcon.Warning, prompt.Icon);
        Assert.Equal("FreeW", prompt.Title);
        Assert.Contains("Shared.docx", prompt.Message);
        Assert.Equal(
            "someone else's edit",
            DocxReader.Read(path).PlainText.Trim());
    }

    [StaFact]
    public void Save_AfterAnotherProgramChangedTheFile_ConfirmedOverwritesAndRebasesTheBaseline()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Shared.docx", "original");
        Assert.True(file.OpenPath(path));

        editor.LoadModel(Document("my edit"));
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        messages.NextResult = UserMessageResult.Yes;

        Assert.True(file.Save());

        Assert.Single(messages.Messages);
        Assert.Equal("my edit", DocxReader.Read(path).PlainText.Trim());

        // The successful save rebased the tracked write time to what it just wrote, so an
        // immediate second save of the same document must not re-prompt.
        editor.LoadModel(Document("my second edit"));
        file.MarkDirty();

        Assert.True(file.Save());

        Assert.Single(messages.Messages);
        Assert.Equal("my second edit", DocxReader.Read(path).PlainText.Trim());
    }

    [StaFact]
    public void Save_WhenTheFileWasNotChanged_NeverPrompts()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Untouched.docx", "original");
        Assert.True(file.OpenPath(path));

        editor.LoadModel(Document("my edit"));
        file.MarkDirty();

        Assert.True(file.Save());

        Assert.Empty(messages.Messages);
        Assert.Equal("my edit", DocxReader.Read(path).PlainText.Trim());
    }

    // Save a Copy writes to a path this document's identity does not track, so there is no prior
    // observation to compare -- it must never be blocked by whatever is on disk at that target.
    [StaFact]
    public void SaveCopy_ToADifferentPath_NeverPromptsEvenWhenThatPathWasChanged()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Source.docx", "original");
        Assert.True(file.OpenPath(path));
        var copyPath = WriteDocx("Copy.docx", "someone else's copy");

        editor.LoadModel(Document("my edit"));

        Assert.True(file.SaveCopyToPath(copyPath));

        Assert.Empty(messages.Messages);
        Assert.Equal("my edit", DocxReader.Read(copyPath).PlainText.Trim());
    }

    // Close-with-save reaches the same guarded Save, so a second writer is caught there too rather
    // than only on an explicit File > Save.
    [StaFact]
    public void ConfirmCloseAllowed_SavingOnClose_StillAsksBeforeOverwritingAnExternalChange()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Closing.docx", "original");
        Assert.True(file.OpenPath(path));

        editor.LoadModel(Document("my edit"));
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        // Yes to "save changes before closing", then Yes to "overwrite the other writer".
        messages.NextResult = UserMessageResult.Yes;

        Assert.True(file.ConfirmCloseAllowed());

        Assert.Equal(2, messages.Messages.Count);
        Assert.Equal(UserMessageButtons.YesNoCancel, messages.Messages[0].Buttons);
        Assert.Equal(UserMessageButtons.YesNo, messages.Messages[1].Buttons);
        Assert.Contains("Closing.docx", messages.Messages[1].Message);
        Assert.Equal("my edit", DocxReader.Read(path).PlainText.Trim());
    }

    // A recovered autosave snapshot targets the ORIGINAL path, whose current on-disk write time is
    // the correct baseline: recover-then-save is an ordinary save, not a conflict.
    [StaFact]
    public void Save_AfterRecoveringASnapshot_DoesNotPromptForTheRecoveredOriginal()
    {
        var (file, editor, messages) = CreateHarness();
        var path = WriteDocx("Recovered.docx", "on disk");
        var snapshotPath = WriteDocx("Recovered.snapshot.docx", "unsaved work");

        Assert.True(file.OpenSnapshot(snapshotPath, path));
        Assert.True(file.IsDirty);

        editor.LoadModel(Document("recovered edit"));

        Assert.True(file.Save());

        Assert.Empty(messages.Messages);
        Assert.Equal("recovered edit", DocxReader.Read(path).PlainText.Trim());
    }

    private (FileCommands File, DocumentView Editor, RecordingUserMessageService Messages) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var messages = new RecordingUserMessageService();
        var file = new FileCommands(
            window,
            editor,
            () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(TempDirectory, "recent.json")),
            messageService: messages,
            confirmSaveCompatibility: _ => true);
        return (file, editor, messages);
    }

    private string WriteDocx(string name, string text)
    {
        var path = Path.Combine(TempDirectory, name);
        DocxWriter.Write(Document(text), path);
        return path;
    }

    /// <summary>
    /// Simulates a second writer (another FreeW/Word instance, a sync client, a colleague on a
    /// shared path) touching the file after it was opened, with a real mtime change rather than a
    /// fabricated one.
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
}
