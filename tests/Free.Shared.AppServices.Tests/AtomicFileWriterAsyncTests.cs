namespace Free.Shared.AppServices.Tests;

public sealed class AtomicFileWriterAsyncTests
{
    [Fact]
    public async Task WriteAllBytesAsync_AtomicallyReplacesTargetAndCleansTempArtifact()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "exports", "deck.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            await AtomicFileWriter.WriteAllBytesAsync(path, [4, 5]);

            (await File.ReadAllBytesAsync(path)).Should().Equal(4, 5);
            Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllBytesAsync_WhenCanceledPreservesTargetAndCleansTempArtifact()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "deck.pdf");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var act = () => AtomicFileWriter.WriteAllBytesAsync(path, [4, 5], cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllBytesAsync(path)).Should().Equal(1, 2, 3);
            Directory.EnumerateFileSystemEntries(root, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllBytesAsync_WhenReplaceFailsCleansTempArtifact()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var blockedPath = Path.Combine(root, "deck.pdf");
            Directory.CreateDirectory(blockedPath);

            var act = () => AtomicFileWriter.WriteAllBytesAsync(blockedPath, [1, 2, 3]);

            await act.Should().ThrowAsync<Exception>();
            Directory.Exists(blockedPath).Should().BeTrue();
            Directory.EnumerateFileSystemEntries(root, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FreeX.AtomicFileWriter.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
