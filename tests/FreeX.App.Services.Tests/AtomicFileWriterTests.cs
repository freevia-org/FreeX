using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public void WriteAllText_CreatesFileWithContentIncludingMissingDirectories()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Library", "Application Support", "FreeX", "options.json");

        AtomicFileWriter.WriteAllText(path, "payload");

        File.ReadAllText(path).Should().Be("payload");
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllText_RepeatedWritesLeaveOnlyTargetFile()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        for (var index = 0; index < 5; index++)
            AtomicFileWriter.WriteAllText(path, $"payload-{index}");

        File.ReadAllText(path).Should().Be("payload-4");
        Directory.EnumerateFileSystemEntries(temp.Path).Should().ContainSingle().Which.Should().Be(path);
        Directory.EnumerateFileSystemEntries(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllText_StaleFixedTempArtifactDoesNotBlockWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var staleFixedTempPath = path + ".tmp";
        Directory.CreateDirectory(staleFixedTempPath);

        AtomicFileWriter.WriteAllText(path, "payload");

        File.ReadAllText(path).Should().Be("payload");
        Directory.Exists(staleFixedTempPath).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(temp.Path, ".recent.json.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllText_WhenMoveFailsCleansTempArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        var blockedPath = Path.Combine(temp.Path, "options.json");
        Directory.CreateDirectory(blockedPath);

        var act = () => AtomicFileWriter.WriteAllText(blockedPath, "payload");

        act.Should().Throw<Exception>();
        Directory.EnumerateFileSystemEntries(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllBytes_CreatesMissingDirectoriesAndAtomicallyReplacesTarget()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "exports", "deck.pdf");

        AtomicFileWriter.WriteAllBytes(path, [1, 2, 3]);
        AtomicFileWriter.WriteAllBytes(path, [4, 5]);

        File.ReadAllBytes(path).Should().Equal(4, 5);
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void CreateTempPath_AndReplaceTarget_SupportRendererOwnedStreamingWrites()
    {
        using var temp = new TestTemporaryDirectory();
        var targetPath = Path.Combine(temp.Path, "report.xps");
        File.WriteAllText(targetPath, "old");

        var tempPath = AtomicFileWriter.CreateTempPath(targetPath);
        File.WriteAllText(tempPath, "new");
        AtomicFileWriter.ReplaceTarget(tempPath, targetPath);

        Path.GetDirectoryName(tempPath).Should().Be(Path.GetDirectoryName(targetPath));
        Path.GetFileName(tempPath).Should().StartWith(".report.xps.").And.EndWith(".tmp");
        File.ReadAllText(targetPath).Should().Be("new");
        File.Exists(tempPath).Should().BeFalse();
    }

    [Fact]
    public void ExportAtomicWriter_IsAThinAdapterOverTheSingleAtomicIoOwner()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.Shell",
            "ExportAtomicWriter.cs"));

        source.Should().Contain("AtomicFileWriter.CreateTempPath(targetPath)");
        source.Should().Contain("AtomicFileWriter.WriteAllBytes(targetPath, bytes)");
        source.Should().Contain("AtomicFileWriter.ReplaceTarget(sourceTempPath, destinationPath)");
        source.Should().NotContain("File.");
        source.Should().NotContain("Directory.");
        source.Should().NotContain("Path.");
        source.Should().NotContain("Guid.");
    }
}
