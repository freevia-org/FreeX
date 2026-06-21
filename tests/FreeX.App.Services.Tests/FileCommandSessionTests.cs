using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileCommandSessionTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeX.FileCommandSessionTests", Guid.NewGuid().ToString("N"));

    public FileCommandSessionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void FreshSession_IsCleanUntitledAndPathless()
    {
        var session = new FileCommandSession();

        session.IsDirty.Should().BeFalse();
        session.DirtyGeneration.Should().Be(0);
        session.CurrentPath.Should().BeNull();
        session.DisplayName.Should().Be("Untitled");
    }

    [Fact]
    public void MarkDirtyIfClean_MarksAndNotifiesOnlyOnce()
    {
        var session = new FileCommandSession();
        var changes = 0;

        session.MarkDirtyIfClean(() => changes++).Should().BeTrue();
        session.MarkDirtyIfClean(() => changes++).Should().BeFalse();

        session.IsDirty.Should().BeTrue();
        session.DirtyGeneration.Should().Be(1);
        changes.Should().Be(1);
    }

    [Fact]
    public void MarkDirty_IncrementsDirtyGenerationOnEveryChange()
    {
        var session = new FileCommandSession();

        session.MarkDirty();
        session.MarkDirty();

        session.IsDirty.Should().BeTrue();
        session.DirtyGeneration.Should().Be(2);
    }

    [Fact]
    public void DisplayNameFromPath_UsesFileNameWithoutExtensionOrFallback()
    {
        FileCommandSession.DisplayNameFromPath(@"C:\Docs\Quarterly Review.fxp")
            .Should()
            .Be("Quarterly Review");
        FileCommandSession.DisplayNameFromPath(null).Should().Be("Untitled");
        FileCommandSession.DisplayNameFromPath("   ", "New deck").Should().Be("New deck");
    }

    [Fact]
    public void ConfirmDiscardOrSave_CleanDocument_ProceedsWithoutPrompt()
    {
        var session = new FileCommandSession();

        var allowed = session.ConfirmDiscardOrSave(
            "closing",
            _ => throw new InvalidOperationException("clean documents should not prompt"),
            () => throw new InvalidOperationException("clean documents should not save"));

        allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(SaveChangesPrompt.Cancel, false, false)]
    [InlineData(SaveChangesPrompt.DontSave, true, false)]
    [InlineData(SaveChangesPrompt.Save, true, true)]
    public void ConfirmDiscardOrSave_DirtyDocument_ResolvesPromptAnswer(
        SaveChangesPrompt answer,
        bool expectedAllowed,
        bool expectedSaved)
    {
        var session = new FileCommandSession();
        session.MarkDirty();
        var saved = false;

        var allowed = session.ConfirmDiscardOrSave(
            "closing",
            action =>
            {
                action.Should().Be("closing");
                return answer;
            },
            () =>
            {
                saved = true;
                return true;
            });

        allowed.Should().Be(expectedAllowed);
        saved.Should().Be(expectedSaved);
    }

    [Fact]
    public void MarkSavedWithPath_UpdatesPathAndRegistersRecentFileWithCap()
    {
        var storePath = Path.Combine(_tempDir, "recent.json");
        var session = new FileCommandSession(loadRecentFilesStore: () => RecentFilesStore.Load(storePath));
        var first = Path.Combine(_tempDir, "first.fxp");
        var second = Path.Combine(_tempDir, "second.fxp");

        session.MarkDirty();
        session.MarkSavedWithPath(first, suppressRecentFiles: false, maxRecentEntries: 1);
        session.MarkSavedWithPath(second, suppressRecentFiles: false, maxRecentEntries: 1);

        session.IsDirty.Should().BeFalse();
        session.CurrentPath.Should().Be(second);
        RecentFilesStore.Load(storePath).Entries
            .Select(entry => entry.Path)
            .Should()
            .Equal(second);
    }

    [Fact]
    public void MarkSavedWithPath_SuppressedRecentFiles_UpdatesPathButDoesNotRegister()
    {
        var storePath = Path.Combine(_tempDir, "recent.json");
        var session = new FileCommandSession(loadRecentFilesStore: () => RecentFilesStore.Load(storePath));
        var path = Path.Combine(_tempDir, "snapshot.fxp");

        session.MarkSavedWithPath(path, suppressRecentFiles: true, maxRecentEntries: 5);

        session.CurrentPath.Should().Be(path);
        RecentFilesStore.Load(storePath).Entries.Should().BeEmpty();
    }

    [Fact]
    public void RecentEntries_ReturnsEmptyWhenStoreCannotLoad()
    {
        var session = new FileCommandSession(loadRecentFilesStore: () => throw new InvalidOperationException("boom"));

        session.RecentEntries.Should().BeEmpty();
    }
}
