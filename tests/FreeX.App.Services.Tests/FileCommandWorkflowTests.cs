using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileCommandWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeX.FileCommandWorkflowTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

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
    public async Task SaveAsync_UsesCurrentPathOrSaveAsFromPlanner()
    {
        var workflow = CreateWorkflow();
        var path = Path.Combine(_tempDir, "saved.fxp");
        var savedPaths = new List<string>();
        var saveAsCount = 0;

        (await workflow.SaveAsync(
            p =>
            {
                savedPaths.Add(p);
                return Task.FromResult(true);
            },
            () =>
            {
                saveAsCount++;
                return Task.FromResult(true);
            }))
            .Should()
            .BeTrue();
        saveAsCount.Should().Be(1);

        workflow.MarkSavedWithPath(path, suppressRecentFiles: true);
        (await workflow.SaveAsync(
            p =>
            {
                savedPaths.Add(p);
                return Task.FromResult(true);
            },
            () => Task.FromResult(false)))
            .Should()
            .BeTrue();

        savedPaths.Should().Equal(path);
    }

    [Fact]
    public async Task OpenAsync_DirtyDocument_UsesSharedDirtyGateBeforePromptingForPath()
    {
        var promptedForPath = false;
        var opened = false;
        var workflow = CreateWorkflow(prompt: _ => SaveChangesPrompt.Cancel);
        workflow.MarkDirty();

        var proceeded = await workflow.OpenAsync(
            "opening another document",
            () =>
            {
                promptedForPath = true;
                return Task.FromResult<string?>("ignored.fxp");
            },
            _ =>
            {
                opened = true;
                return Task.FromResult(true);
            });

        proceeded.Should().BeFalse();
        promptedForPath.Should().BeFalse();
        opened.Should().BeFalse();
    }

    [Fact]
    public void CurrentFileName_DerivesDialogSourceNameFromCurrentPath()
    {
        var workflow = CreateWorkflow();
        var path = Path.Combine(_tempDir, "Quarterly Draft.fxp");

        workflow.CurrentFileName.Should().BeNull();
        workflow.CurrentFileNameWithoutExtensionOr("Document").Should().Be("Document");

        workflow.MarkSavedWithPath(path, suppressRecentFiles: true);

        workflow.CurrentFileName.Should().Be("Quarterly Draft.fxp");
        workflow.CurrentFileNameWithoutExtensionOr("Document").Should().Be("Quarterly Draft");
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
