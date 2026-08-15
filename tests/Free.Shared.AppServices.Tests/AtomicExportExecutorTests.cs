using System.Text;

namespace Free.Shared.AppServices.Tests;

public sealed class AtomicExportExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "atomic-export-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_CreatesDestinationAndReturnsRendererArtifact()
    {
        var destination = Path.Combine(_root, "exports", "report.pdf");
        var executor = new AtomicExportExecutor();

        var result = await executor.ExecuteAsync(
            destination,
            async (output, token) =>
            {
                await output.WriteAsync(Encoding.ASCII.GetBytes("new export"), token);
                return new TestArtifact(3);
            });

        result.Status.Should().Be(OperationStatus.Completed);
        result.Value.Should().Be(new TestArtifact(3));
        result.Path.Should().Be(Path.GetFullPath(destination));
        (await File.ReadAllTextAsync(destination)).Should().Be("new export");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_OverwritesOnlyAfterRendererCompletesAndStreamCloses()
    {
        var destination = CreateExistingDestination("old export");
        var replacementObservedClosedStream = false;
        var executor = new AtomicExportExecutor(
            AtomicFileWriter.CreateTempLease,
            (temporaryPath, targetPath) =>
            {
                using (File.Open(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    replacementObservedClosedStream = true;
                AtomicFileWriter.ReplaceTarget(temporaryPath, targetPath);
            });

        var result = await executor.ExecuteAsync(
            destination,
            async (output, token) =>
            {
                File.ReadAllText(destination).Should().Be("old export");
                await output.WriteAsync(Encoding.ASCII.GetBytes("replacement"), token);
                return 12;
            });

        result.Succeeded.Should().BeTrue();
        replacementObservedClosedStream.Should().BeTrue();
        File.ReadAllText(destination).Should().Be("replacement");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, AtomicExportValidationIssue.DestinationPathMissing)]
    [InlineData("", AtomicExportValidationIssue.DestinationPathMissing)]
    [InlineData("   ", AtomicExportValidationIssue.DestinationPathMissing)]
    [InlineData("\0", AtomicExportValidationIssue.DestinationPathInvalid)]
    public async Task ExecuteAsync_RejectsMissingOrInvalidDestination(
        string? destination,
        AtomicExportValidationIssue expectedIssue)
    {
        var rendered = false;

        var result = await new AtomicExportExecutor().ExecuteAsync(
            destination,
            (_, _) =>
            {
                rendered = true;
                return ValueTask.FromResult(0);
            });

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Validation!.Detail.Should().Be(expectedIssue);
        rendered.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDirectoryDestination()
    {
        Directory.CreateDirectory(_root);

        var result = await new AtomicExportExecutor().ExecuteAsync(
            _root,
            (_, _) => ValueTask.FromResult(0));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Validation!.Detail.Should().Be(AtomicExportValidationIssue.DestinationIsDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDestinationBelowAFile()
    {
        Directory.CreateDirectory(_root);
        var fileParent = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(fileParent, "occupied");

        var result = await new AtomicExportExecutor().ExecuteAsync(
            Path.Combine(fileParent, "report.pdf"),
            (_, _) => ValueTask.FromResult(0));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Validation!.Detail.Should().Be(AtomicExportValidationIssue.DestinationParentIsFile);
    }

    [Fact]
    public async Task ExecuteAsync_RendererFailurePreservesDestinationAndCleansTemporaryFile()
    {
        var destination = CreateExistingDestination("old export");

        var result = await new AtomicExportExecutor().ExecuteAsync<int>(
            destination,
            async (output, token) =>
            {
                await output.WriteAsync(Encoding.ASCII.GetBytes("partial"), token);
                throw new InvalidOperationException("renderer failed");
            });

        result.Status.Should().Be(OperationStatus.Failed);
        result.Error!.Detail.Stage.Should().Be(AtomicExportFailureStage.Rendering);
        result.Exception.Should().BeOfType<InvalidOperationException>();
        File.ReadAllText(destination).Should().Be("old export");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_FlushFailurePreservesDestinationAndCleansTemporaryFile()
    {
        var destination = CreateExistingDestination("old export");
        var fileSystem = new CallbackFileSystem(_root)
        {
            WrapStream = stream => new CallbackStream(
                stream,
                flushAsync: (_, _) => throw new IOException("flush failed")),
        };
        var executor = CreateExecutor(fileSystem);

        var result = await executor.ExecuteAsync(
            destination,
            async (output, token) =>
            {
                await output.WriteAsync(new byte[] { 1, 2, 3 }, token);
                return 3;
            });

        result.Status.Should().Be(OperationStatus.Failed);
        result.Error!.Detail.Stage.Should().Be(AtomicExportFailureStage.Flushing);
        File.ReadAllText(destination).Should().Be("old export");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReplacementFailurePreservesDestinationAndCleansTemporaryFile()
    {
        var destination = CreateExistingDestination("old export");
        var executor = new AtomicExportExecutor(
            AtomicFileWriter.CreateTempLease,
            (_, _) => throw new IOException("replacement failed"));

        var result = await executor.ExecuteAsync(
            destination,
            async (output, token) =>
            {
                await output.WriteAsync(Encoding.ASCII.GetBytes("new export"), token);
                return 10;
            });

        result.Status.Should().Be(OperationStatus.Failed);
        result.Error!.Detail.Stage.Should().Be(AtomicExportFailureStage.ReplacingDestination);
        File.ReadAllText(destination).Should().Be("old export");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledStopsBeforeTemporaryFileCreation()
    {
        var destination = Path.Combine(_root, "report.pdf");
        var temporaryFileCreated = false;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var executor = new AtomicExportExecutor(
            path =>
            {
                temporaryFileCreated = true;
                return AtomicFileWriter.CreateTempLease(path);
            },
            AtomicFileWriter.ReplaceTarget);

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) => ValueTask.FromResult(0),
            cancellation.Token);

        result.Status.Should().Be(OperationStatus.Cancelled);
        temporaryFileCreated.Should().BeFalse();
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringTemporaryCreationMapsAndCleans()
    {
        var destination = Path.Combine(_root, "report.pdf");
        using var cancellation = new CancellationTokenSource();
        var executor = new AtomicExportExecutor(
            path =>
            {
                var temporaryFile = AtomicFileWriter.CreateTempLease(path);
                cancellation.Cancel();
                return temporaryFile;
            },
            AtomicFileWriter.ReplaceTarget);

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) => ValueTask.FromResult(0),
            cancellation.Token);

        result.Status.Should().Be(OperationStatus.Cancelled);
        File.Exists(destination).Should().BeFalse();
        TemporaryFiles(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringRenderingPreservesDestinationAndCleans()
    {
        var destination = CreateExistingDestination("old export");
        using var cancellation = new CancellationTokenSource();

        var result = await new AtomicExportExecutor().ExecuteAsync<int>(
            destination,
            async (output, token) =>
            {
                await output.WriteAsync(new byte[] { 1, 2, 3 }, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return 3;
            },
            cancellation.Token);

        AssertCanceledAndPreserved(result, destination);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterRenderingStopsBeforeFlushAndReplacement()
    {
        var destination = CreateExistingDestination("old export");
        using var cancellation = new CancellationTokenSource();
        var replacementCalled = false;
        var executor = new AtomicExportExecutor(
            AtomicFileWriter.CreateTempLease,
            (_, _) => replacementCalled = true);

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(4);
            },
            cancellation.Token);

        AssertCanceledAndPreserved(result, destination);
        replacementCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringFlushStopsBeforeReplacementAndCleans()
    {
        var destination = CreateExistingDestination("old export");
        using var cancellation = new CancellationTokenSource();
        var replacementCalled = false;
        var fileSystem = new CallbackFileSystem(_root)
        {
            WrapStream = stream => new CallbackStream(
                stream,
                flushAsync: (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }),
        };
        var executor = CreateExecutor(
            fileSystem,
            (_, _) => replacementCalled = true);

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) => ValueTask.FromResult(4),
            cancellation.Token);

        AssertCanceledAndPreserved(result, destination);
        replacementCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationWhileClosingStopsBeforeReplacementAndCleans()
    {
        var destination = CreateExistingDestination("old export");
        using var cancellation = new CancellationTokenSource();
        var replacementCalled = false;
        var fileSystem = new CallbackFileSystem(_root)
        {
            WrapStream = stream => new CallbackStream(
                stream,
                disposeAsync: async inner =>
                {
                    await inner.DisposeAsync();
                    cancellation.Cancel();
                }),
        };
        var executor = CreateExecutor(
            fileSystem,
            (_, _) => replacementCalled = true);

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) => ValueTask.FromResult(4),
            cancellation.Token);

        AssertCanceledAndPreserved(result, destination);
        replacementCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAtReplacementPreservesDestinationAndCleans()
    {
        var destination = CreateExistingDestination("old export");
        using var cancellation = new CancellationTokenSource();
        var executor = new AtomicExportExecutor(
            AtomicFileWriter.CreateTempLease,
            (_, _) =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            });

        var result = await executor.ExecuteAsync(
            destination,
            (_, _) => ValueTask.FromResult(4),
            cancellation.Token);

        AssertCanceledAndPreserved(result, destination);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string CreateExistingDestination(string content)
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "report.pdf");
        File.WriteAllText(destination, content);
        return destination;
    }

    private static AtomicExportExecutor CreateExecutor(
        ITemporaryResourceFileSystem fileSystem,
        Action<string, string>? replace = null) =>
        new(
            path =>
            {
                var fullPath = Path.GetFullPath(path);
                return TemporaryFileLease.Create(
                    $".{Path.GetFileName(fullPath)}.",
                    ".tmp",
                    Path.GetDirectoryName(fullPath),
                    fileSystem);
            },
            replace ?? AtomicFileWriter.ReplaceTarget);

    private static string[] TemporaryFiles(string destination) =>
        Directory.Exists(Path.GetDirectoryName(destination))
            ? Directory.GetFiles(
                Path.GetDirectoryName(destination)!,
                $".{Path.GetFileName(destination)}.*.tmp")
            : [];

    private static void AssertCanceledAndPreserved<TArtifact>(
        OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure> result,
        string destination)
    {
        result.Status.Should().Be(OperationStatus.Cancelled);
        result.Exception.Should().BeOfType<OperationCanceledException>();
        File.ReadAllText(destination).Should().Be("old export");
        TemporaryFiles(destination).Should().BeEmpty();
    }

    private sealed record TestArtifact(int PageCount);

    private sealed class CallbackFileSystem(string temporaryDirectoryPath)
        : ITemporaryResourceFileSystem
    {
        public Func<Stream, Stream>? WrapStream { get; init; }

        public string GetTemporaryDirectoryPath() => temporaryDirectoryPath;

        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public Stream CreateNewFile(string path) => new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        public Stream OpenFileForWrite(string path, bool useAsync, int bufferSize)
        {
            Stream stream = new FileStream(
                path,
                FileMode.Truncate,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                useAsync);
            return WrapStream?.Invoke(stream) ?? stream;
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void DeleteFile(string path) => File.Delete(path);

        public void DeleteDirectory(string path, bool recursive) =>
            Directory.Delete(path, recursive);
    }

    private sealed class CallbackStream(
        Stream inner,
        Func<Stream, CancellationToken, Task>? flushAsync = null,
        Func<Stream, ValueTask>? disposeAsync = null) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            flushAsync?.Invoke(inner, cancellationToken) ?? inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            if (disposeAsync is not null)
                await disposeAsync(inner);
            else
                await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
