using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class FileByteReadWorkflowTests
{
    [Fact]
    public async Task ReadLocalPathAsync_ReturnsFileBytes()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            var result = await FileByteReadWorkflow.ReadLocalPathAsync(path);

            result.Outcome.Should().Be(FileByteReadOutcome.Succeeded);
            result.Bytes.Should().Equal(1, 2, 3);
            result.Exception.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadStreamAsync_ClassifiesEmptyStream()
    {
        var result = await FileByteReadWorkflow.ReadStreamAsync(
            () => Task.FromResult<Stream>(new MemoryStream()));

        result.Outcome.Should().Be(FileByteReadOutcome.Empty);
        result.IsReadable.Should().BeTrue();
        result.Bytes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadStreamAsync_ReturnsOpenFailure()
    {
        var exception = new IOException("read failed");

        var result = await FileByteReadWorkflow.ReadStreamAsync(
            () => Task.FromException<Stream>(exception));

        result.Outcome.Should().Be(FileByteReadOutcome.Failed);
        result.Exception.Should().BeSameAs(exception);
        result.FailureMessage.Should().Be("read failed");
    }

    [Fact]
    public async Task ReadStreamAsync_ClassifiesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await FileByteReadWorkflow.ReadStreamAsync(
            () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])),
            cancellation.Token);

        result.Outcome.Should().Be(FileByteReadOutcome.Canceled);
        result.Bytes.Should().BeEmpty();
    }
}
