using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TestTemporaryDirectoryTests
{
    [Fact]
    public void Constructor_CreatesDirectory_WithOrWithoutPrefix()
    {
        using var unprefixed = new TestTemporaryDirectory();
        using var prefixed = new TestTemporaryDirectory("FreeX.SharedTempTests-");

        Directory.Exists(unprefixed.Path).Should().BeTrue();
        Directory.Exists(prefixed.Path).Should().BeTrue();
        System.IO.Path.GetFileName(prefixed.Path)
            .Should().StartWith("FreeX.SharedTempTests-");
    }

    [Fact]
    public void Dispose_RemovesDirectoryRecursively_AndIsIdempotent()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeX.SharedTempTests-");
        var path = temporaryDirectory.Path;
        var nestedDirectory = System.IO.Path.Combine(path, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(System.IO.Path.Combine(nestedDirectory, "marker.txt"), "marker");

        temporaryDirectory.Dispose();
        temporaryDirectory.Dispose();

        Directory.Exists(path).Should().BeFalse();
    }
}
