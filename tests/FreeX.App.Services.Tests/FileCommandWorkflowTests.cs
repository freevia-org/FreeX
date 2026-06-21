using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileCommandWorkflowTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeX.FileCommandWorkflowTests", Guid.NewGuid().ToString("N"));

    public FileCommandWorkflowTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void MarkDirty_NotifiesOnlyWhenTransitioningFromClean()
    {
        var changes = 0;
        var workflow = CreateWorkflow(onChanged: () => changes++);

        workflow.MarkDirty();
        workflow.MarkDirty();

        workflow.IsDirty.Should().BeTrue();
        workflow.DirtyGeneration.Should().Be(1);
        changes.Should().Be(1);
    }

    [Fact]
    public void New_CleanDocument_LoadsAndMarksPathlessSaved()
    {
        var loaded = false;
        var changes = 0;
        var workflow = CreateWorkflow(onChanged: () => changes++);

        var proceeded = workflow.New("creating a new document", () => loaded = true);

        proceeded.Should().BeTrue();
        loaded.Should().BeTrue();
        workflow.IsDirty.Should().BeFalse();
        workflow.CurrentPath.Should().BeNull();
        changes.Should().Be(1);
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Cancel, false, false)]
    [InlineData(SaveChangesPrompt.DontSave, true, false)]
    [InlineData(SaveChangesPrompt.Save, true, true)]
    public void New_DirtyDocument_UsesSharedDirtyGate(
        SaveChangesPrompt prompt,
        bool expectedProceed,
        bool expectedSave)
    {
        var saved = false;
        var loaded = false;
        var workflow = CreateWorkflow(prompt: _ => prompt, save: () => saved = true);
        workflow.MarkDirty();

        var proceeded = workflow.New("creating a new document", () => loaded = true);

        proceeded.Should().Be(expectedProceed);
        saved.Should().Be(expectedSave);
        loaded.Should().Be(expectedProceed);
    }

    [Fact]
    public void Open_CleanDocument_PromptsForPathAndDelegatesOpen()
    {
        var path = Path.Combine(_tempDir, "opened.fxp");
        string? opened = null;
        var workflow = CreateWorkflow();

        var proceeded = workflow.Open("opening another document", () => path, p =>
        {
            opened = p;
            return true;
        });

        proceeded.Should().BeTrue();
        opened.Should().Be(path);
    }

    [Fact]
    public void Save_UsesCurrentPathOrSaveAsFromPlanner()
    {
        var workflow = CreateWorkflow();
        var path = Path.Combine(_tempDir, "saved.fxp");
        var savedPaths = new List<string>();
        var saveAsCount = 0;

        workflow.Save(p => { savedPaths.Add(p); return true; }, () => { saveAsCount++; return true; })
            .Should()
            .BeTrue();
        saveAsCount.Should().Be(1);

        workflow.MarkSavedWithPath(path, suppressRecentFiles: true);
        workflow.Save(p => { savedPaths.Add(p); return true; }, () => false)
            .Should()
            .BeTrue();

        savedPaths.Should().Equal(path);
    }

    [Fact]
    public void MarkSavedWithPath_UsesCurrentRecentFilesCapAndNotifies()
    {
        var cap = 2;
        var changes = 0;
        var storePath = Path.Combine(_tempDir, "recent.json");
        var workflow = CreateWorkflow(
            maxRecentEntries: () => cap,
            onChanged: () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(storePath));
        var first = Path.Combine(_tempDir, "first.fxp");
        var second = Path.Combine(_tempDir, "second.fxp");
        var third = Path.Combine(_tempDir, "third.fxp");

        workflow.MarkSavedWithPath(first, suppressRecentFiles: false);
        cap = 1;
        workflow.MarkSavedWithPath(second, suppressRecentFiles: false);
        workflow.MarkSavedWithPath(third, suppressRecentFiles: true);

        workflow.CurrentPath.Should().Be(third);
        changes.Should().Be(3);
        RecentFilesStore.Load(storePath).Entries
            .Select(entry => entry.Path)
            .Should()
            .Equal(second);
    }

    private static FileCommandWorkflow CreateWorkflow(
        Func<int>? maxRecentEntries = null,
        Action? onChanged = null,
        Func<string, SaveChangesPrompt>? prompt = null,
        Func<bool>? save = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null) =>
        new(
            maxRecentEntries ?? (() => 10),
            onChanged ?? (() => { }),
            prompt ?? (_ => SaveChangesPrompt.Cancel),
            save ?? (() => true),
            loadRecentFilesStore: loadRecentFilesStore);
}
