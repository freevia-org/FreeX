using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for FreeW's P2 file-lifecycle adoption: <see cref="FileCommands"/> now routes its
/// ceremony through the shared <see cref="FileLifecyclePlanner"/> and tracks dirty/path state via the
/// shared <see cref="FileCommandSession"/>.
///
/// <para>
/// These exercise only the dialog-free paths (New on a clean doc, MarkDirty, OpenPath/Save to an
/// existing path, OpenSnapshot) so they run headless without popping native file dialogs. STA because
/// <see cref="DocumentView"/> hosts a RichTextBox/FlowDocument and <see cref="FileCommands"/> needs a
/// <see cref="Window"/> owner. The pure planner decisions themselves are unit-tested in the shared
/// FreeX.App.Services.Tests project.
/// </para>
/// </summary>
public sealed class FileLifecycleTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.FileLifecycleTests", Guid.NewGuid().ToString("N"));

    public FileLifecycleTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private (
        Window window,
        DocumentView editor,
        FileCommands file,
        Func<int> changeCount,
        RecordingUserMessageService messages) CreateHarness(Func<string?>? promptPdfImportPath = null)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var changes = 0;
        var recentStorePath = Path.Combine(_tempDir, "recent.json");
        var messages = new RecordingUserMessageService();
        var file = new FileCommands(
            window,
            editor,
            () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(recentStorePath),
            messageService: messages,
            promptPdfImportPath: promptPdfImportPath);
        return (window, editor, file, () => changes, messages);
    }

    [StaFact]
    public void FreshDocument_IsCleanWithUntitledName()
    {
        var (_, _, file, _, _) = CreateHarness();

        Assert.False(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Equal("Untitled", file.DisplayName);
    }

    [StaFact]
    public void MarkDirty_SetsDirtyAndNotifiesOnce()
    {
        var (_, _, file, changeCount, _) = CreateHarness();

        file.MarkDirty();
        Assert.True(file.IsDirty);
        Assert.Equal(1, changeCount());

        // Idempotent: already-dirty MarkDirty does not re-notify.
        file.MarkDirty();
        Assert.Equal(1, changeCount());
    }

    [StaFact]
    public void New_OnCleanDocument_ProceedsWithoutPromptAndResetsState()
    {
        var (_, _, file, _, _) = CreateHarness();

        var proceeded = file.New();

        Assert.True(proceeded);
        Assert.False(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Equal("Untitled", file.DisplayName);
    }

    [StaFact]
    public void New_OnDirtyDocument_UsesInjectedMessageServiceForSavePrompt()
    {
        var (_, _, file, _, messages) = CreateHarness();
        messages.NextResult = UserMessageResult.No;

        file.MarkDirty();
        var proceeded = file.New();

        Assert.True(proceeded);
        Assert.False(file.IsDirty);
        Assert.Single(messages.Messages);
        var prompt = messages.Messages[0];
        Assert.Equal(
            "Do you want to save changes to Untitled before creating a new document?",
            prompt.Message);
        Assert.Equal("FreeW", prompt.Title);
        Assert.Equal(UserMessageButtons.YesNoCancel, prompt.Buttons);
        Assert.Equal(UserMessageIcon.Warning, prompt.Icon);
    }

    [StaFact]
    public void OpenPath_LoadsFileAndMarksSavedWithPath()
    {
        var (_, _, file, _, _) = CreateHarness();
        var path = WriteDocx("Opened.docx", "Hello from disk");

        var opened = file.OpenPath(path);

        Assert.True(opened);
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
        Assert.Equal("Opened", file.DisplayName);
    }

    [StaTheory]
    [InlineData(true, "Ada Lovelace")]
    [InlineData(false, "stale author")]
    public void OpenPath_HonorsUpdateFieldsSettingAndRemainsClean(bool updateFields, string expectedText)
    {
        var (_, editor, file, _, _) = CreateHarness();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.UpdateFieldsOnOpen = updateFields;
        document.Properties.Author = "Ada Lovelace";
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("stale author") { FieldKind = RunFieldKind.Author });
        document.Blocks.Add(paragraph);
        var path = Path.Combine(_tempDir, $"UpdateFields-{updateFields}.docx");
        DocxWriter.Write(document, path);

        var opened = file.OpenPath(path);

        Assert.True(opened);
        Assert.Equal(expectedText, editor.Model.PlainText.Trim());
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
    }

    [StaFact]
    public void ImportPdfTextPath_LoadsAsUntitledDirtyDocument()
    {
        var (_, editor, file, changeCount, _) = CreateHarness();
        var path = WritePdf("Imported.pdf", "Imported PDF text");

        var imported = file.ImportPdfTextPath(path);

        Assert.True(imported);
        Assert.True(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Equal("Untitled", file.DisplayName);
        Assert.Contains("Imported PDF text", editor.Model.PlainText);
        Assert.Equal(1, changeCount());
    }

    [StaFact]
    public void ImportPdfText_OnDirtyDocument_CancelStopsBeforePicker()
    {
        var pickerCalls = 0;
        var (_, editor, file, _, messages) = CreateHarness(() =>
        {
            pickerCalls++;
            return WritePdf("ShouldNotOpen.pdf", "replacement");
        });
        messages.NextResult = UserMessageResult.Cancel;
        var before = editor.Model.PlainText;
        file.MarkDirty();

        var imported = file.ImportPdfText();

        Assert.False(imported);
        Assert.Equal(0, pickerCalls);
        Assert.Equal(before, editor.Model.PlainText);
        Assert.True(file.IsDirty);
        Assert.Single(messages.Messages);
    }

    [StaFact]
    public void ImportPdfText_OnDirtyDocument_DiscardThenImportsThroughSharedWorkflow()
    {
        var path = WritePdf("Replacement.pdf", "Replacement PDF text");
        var pickerCalls = 0;
        var (_, editor, file, _, messages) = CreateHarness(() =>
        {
            pickerCalls++;
            return path;
        });
        messages.NextResult = UserMessageResult.No;
        file.MarkDirty();

        var imported = file.ImportPdfText();

        Assert.True(imported);
        Assert.Equal(1, pickerCalls);
        Assert.Contains("Replacement PDF text", editor.Model.PlainText);
        Assert.True(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Single(messages.Messages);
    }

    [StaFact]
    public void Save_AfterEdit_WritesToExistingPathAndClearsDirty()
    {
        var (_, _, file, _, _) = CreateHarness();
        var path = WriteDocx("Doc.docx", "Initial");
        Assert.True(file.OpenPath(path));

        file.MarkDirty();
        Assert.True(file.IsDirty);

        // Save resolves (via the shared planner) to UseExistingPath → writes without a dialog.
        var saved = file.Save();

        Assert.True(saved);
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
        // The file is still a readable docx after the save.
        Assert.NotNull(DocxReader.Read(path));
    }

    [StaFact]
    public void Save_OnCleanOpenedDocument_IsNoOpAndStaysClean()
    {
        var (_, _, file, _, _) = CreateHarness();
        var path = WriteDocx("Clean.docx", "Unchanged");
        Assert.True(file.OpenPath(path));

        // Clean + has path → planner returns NothingToDo; FreeW still re-serializes, but stays clean.
        var saved = file.Save();

        Assert.True(saved);
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
    }

    [StaFact]
    public void SaveCopy_WritesSeparateFileWithoutChangingCurrentPathOrDirtyState()
    {
        var (_, editor, file, _, _) = CreateHarness();
        var currentPath = WriteDocx("Current.docx", "Initial");
        var copyPath = Path.Combine(_tempDir, "Copy.docx");
        Assert.True(file.OpenPath(currentPath));

        var edited = TextDocument.CreateEmpty();
        edited.Blocks.Clear();
        edited.Blocks.Add(new Paragraph("Unsaved copy content"));
        editor.LoadModel(edited);
        file.MarkDirty();

        Assert.True(file.SaveCopyToPath(copyPath));

        Assert.True(file.IsDirty);
        Assert.Equal(currentPath, file.CurrentPath);
        Assert.Equal("Unsaved copy content", DocxReader.Read(copyPath).PlainText.Trim());
    }

    [Fact]
    public void WpfFileCommands_DelegateSaveOrderingToSharedExecutionCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "FileCommands.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "DocumentFileExecutionCoordinator.cs"));

        Assert.Contains("private readonly DocumentPersistenceWorkflow _persistence;", source);
        Assert.Contains("private readonly DocumentFileExecutionCoordinator _execution;", source);
        Assert.Contains("_execution.SaveAsync(new DocumentSaveExecutionRequest(", source);
        Assert.Contains("_confirmSaveCompatibility(plan)", source);
        Assert.DoesNotContain("DocumentSaveCompatibilityPlanner.Build", source);
        Assert.DoesNotContain("_persistence.Save(_editor.Model, target)", source);

        var confirmationIndex = coordinator.IndexOf("await request.ConfirmCompatibilityAsync", StringComparison.Ordinal);
        var saveIndex = coordinator.IndexOf("_persistence.Save(request.Document, request.Target);", StringComparison.Ordinal);
        var completionIndex = coordinator.IndexOf("await request.CompleteSaveAsync!", StringComparison.Ordinal);
        Assert.True(confirmationIndex >= 0);
        Assert.True(saveIndex > confirmationIndex);
        Assert.True(completionIndex > saveIndex);
    }

    [StaFact]
    public void OpenSnapshot_MarksDirtyAndTargetsOriginalPath()
    {
        var (_, _, file, _, _) = CreateHarness();
        var original = Path.Combine(_tempDir, "Original.docx");
        var snapshot = WriteDocx("snapshot.docx", "Recovered content");

        var loaded = file.OpenSnapshot(snapshot, original);

        Assert.True(loaded);
        Assert.True(file.IsDirty);
        Assert.Equal(original, file.CurrentPath);
        Assert.Equal("Original", file.DisplayName);
    }

    /// <summary>
    /// Regression test for H2: OpenSnapshot must return false (not throw) when the snapshot file
    /// is corrupt/missing, so the caller (OfferRecovery) can skip deleting the candidate and
    /// preserve the user's only copy of their unsaved document.
    /// </summary>
    [StaFact]
    public void OpenSnapshot_CorruptFile_ReturnsFalseAndDocumentIsUnchanged()
    {
        var (_, _, file, _, messages) = CreateHarness();
        var corruptPath = Path.Combine(_tempDir, "corrupt.docx");
        // Write a file that is not a valid docx so DocxReader.Read throws.
        File.WriteAllText(corruptPath, "this is not a valid docx");

        var loaded = file.OpenSnapshot(corruptPath, originalPath: null);

        // The load must report failure — OfferRecovery gates DeleteCandidate on this bool.
        Assert.False(loaded);
        // The document state must be untouched: still clean and untitled.
        Assert.False(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Single(messages.Messages);
        var error = messages.Messages[0];
        Assert.StartsWith("Could not recover the document:\n", error.Message);
        Assert.Equal("FreeW", error.Title);
        Assert.Equal(UserMessageButtons.Ok, error.Buttons);
        Assert.Equal(UserMessageIcon.Error, error.Icon);
    }

    [StaFact]
    public void RecoverSnapshot_OnCleanDocument_MarksDirtyAndTargetsOriginalPath()
    {
        var (_, _, file, _, _) = CreateHarness();
        var original = Path.Combine(_tempDir, "Original.docx");
        var snapshot = WriteDocx("recover.docx", "Recovered content");

        var recovered = file.RecoverSnapshot(snapshot, original);

        Assert.True(recovered);
        Assert.True(file.IsDirty);
        Assert.Equal(original, file.CurrentPath);
        Assert.Equal("Original", file.DisplayName);
    }

    [StaFact]
    public void MainWindowClose_UsesInjectedMessageServiceForSavePrompt()
    {
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.No };
        var window = new MainWindow(new FreeWOptions(), messageService: messages);

        GetFileCommands(window).MarkDirty();
        window.Close();

        Assert.Single(messages.Messages);
        var prompt = messages.Messages[0];
        Assert.Equal("Do you want to save changes to Untitled before closing?", prompt.Message);
        Assert.Equal("FreeW", prompt.Title);
        Assert.Equal(UserMessageButtons.YesNoCancel, prompt.Buttons);
        Assert.Equal(UserMessageIcon.Warning, prompt.Icon);
    }

    private static FileCommands GetFileCommands(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_file",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (FileCommands)field!.GetValue(window)!;
    }


    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_tempDir, name);
        DocxWriter.Write(doc, path);
        return path;
    }

    private string WritePdf(string name, string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(50, 700), font);

        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

}
