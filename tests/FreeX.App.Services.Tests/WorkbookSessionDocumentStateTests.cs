using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionDocumentStateTests
{
    [Fact]
    public void SiblingViewsShareDirtyGenerationAndSavedPath()
    {
        using var root = new WorkbookSessionFactory().CreateNew(240, 320);
        using var sibling = root.CreateSiblingView(240, 320);

        root.MarkDirtyForRecovery();

        root.IsDirty.Should().BeTrue();
        sibling.IsDirty.Should().BeTrue();
        sibling.DirtyGeneration.Should().Be(1);

        sibling.MarkDirtyForRecovery();

        root.DirtyGeneration.Should().Be(2);
        sibling.DirtyGeneration.Should().Be(2);

        var savedPath = Path.Combine(Path.GetTempPath(), "shared-document-state.fxl");
        root.MarkSaved(savedPath);

        root.IsDirty.Should().BeFalse();
        sibling.IsDirty.Should().BeFalse();
        root.CurrentFilePath.Should().Be(savedPath);
        sibling.CurrentFilePath.Should().Be(savedPath);
    }

    [Fact]
    public void SaveCompletionFromSiblingEditPreservesDirtyStateAndSharesFileContext()
    {
        using var root = new WorkbookSessionFactory().CreateNew(240, 320);
        using var sibling = root.CreateSiblingView(240, 320);
        root.MarkDirtyForRecovery();
        var generationAtSaveStart = root.DirtyGeneration;

        sibling.MarkDirtyForRecovery();
        var savedPath = Path.Combine(Path.GetTempPath(), "stale-save-completion.fxl");

        root.TryMarkSavedIfNoEditsArrived(generationAtSaveStart, savedPath).Should().BeFalse();
        root.IsDirty.Should().BeTrue();
        sibling.IsDirty.Should().BeTrue();
        root.CurrentFilePath.Should().Be(savedPath);
        sibling.CurrentFilePath.Should().Be(savedPath);
    }
}
