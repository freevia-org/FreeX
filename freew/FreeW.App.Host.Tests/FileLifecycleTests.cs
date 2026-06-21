using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
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

    private (Window window, DocumentView editor, FileCommands file, Func<int> changeCount) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var changes = 0;
        var recentStorePath = Path.Combine(_tempDir, "recent.json");
        var file = new FileCommands(
            window,
            editor,
            () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(recentStorePath));
        return (window, editor, file, () => changes);
    }

    [StaFact]
    public void FreshDocument_IsCleanWithUntitledName()
    {
        var (_, _, file, _) = CreateHarness();

        Assert.False(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Equal("Untitled", file.DisplayName);
    }

    [StaFact]
    public void MarkDirty_SetsDirtyAndNotifiesOnce()
    {
        var (_, _, file, changeCount) = CreateHarness();

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
        var (_, _, file, _) = CreateHarness();

        var proceeded = file.New();

        Assert.True(proceeded);
        Assert.False(file.IsDirty);
        Assert.Null(file.CurrentPath);
        Assert.Equal("Untitled", file.DisplayName);
    }

    [StaFact]
    public void OpenPath_LoadsFileAndMarksSavedWithPath()
    {
        var (_, _, file, _) = CreateHarness();
        var path = WriteDocx("Opened.docx", "Hello from disk");

        var opened = file.OpenPath(path);

        Assert.True(opened);
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
        Assert.Equal("Opened", file.DisplayName);
    }

    [StaFact]
    public void Save_AfterEdit_WritesToExistingPathAndClearsDirty()
    {
        var (_, _, file, _) = CreateHarness();
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
        var (_, _, file, _) = CreateHarness();
        var path = WriteDocx("Clean.docx", "Unchanged");
        Assert.True(file.OpenPath(path));

        // Clean + has path → planner returns NothingToDo; FreeW still re-serializes, but stays clean.
        var saved = file.Save();

        Assert.True(saved);
        Assert.False(file.IsDirty);
        Assert.Equal(path, file.CurrentPath);
    }

    [StaFact]
    public void OpenSnapshot_MarksDirtyAndTargetsOriginalPath()
    {
        var (_, _, file, _) = CreateHarness();
        var original = Path.Combine(_tempDir, "Original.docx");
        var snapshot = WriteDocx("snapshot.docx", "Recovered content");

        file.OpenSnapshot(snapshot, original);

        Assert.True(file.IsDirty);
        Assert.Equal(original, file.CurrentPath);
        Assert.Equal("Original", file.DisplayName);
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
}
