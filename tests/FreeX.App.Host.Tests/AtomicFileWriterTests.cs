using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public void WriteAllText_CreatesFileWithContentIncludingMissingDirectories()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "recent.json");

        AtomicFileWriter.WriteAllText(path, "payload");

        File.ReadAllText(path).Should().Be("payload");
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFileAndLeavesNoTempArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        AtomicFileWriter.WriteAllText(path, "first");
        AtomicFileWriter.WriteAllText(path, "second");

        File.ReadAllText(path).Should().Be("second");
        Directory.GetFiles(temp.Path).Should().ContainSingle().Which.Should().Be(path);
    }
}
