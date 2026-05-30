using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public void WriteAllText_CreatesFileWithContentIncludingMissingDirectories()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "nested", "recent.json");

            AtomicFileWriter.WriteAllText(path, "payload");

            File.ReadAllText(path).Should().Be("payload");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFileAndLeavesNoTempArtifact()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "recent.json");

            AtomicFileWriter.WriteAllText(path, "first");
            AtomicFileWriter.WriteAllText(path, "second");

            File.ReadAllText(path).Should().Be("second");
            Directory.GetFiles(root).Should().ContainSingle().Which.Should().Be(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
