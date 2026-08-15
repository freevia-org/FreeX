namespace Free.Shared.AppServices;

public enum AtomicExportValidationIssue
{
    DestinationPathMissing,
    DestinationPathInvalid,
    DestinationIsDirectory,
    DestinationParentIsFile,
}

public enum AtomicExportFailureStage
{
    DestinationValidation,
    TemporaryFileCreation,
    Rendering,
    Flushing,
    ReplacingDestination,
}

public sealed record AtomicExportFailure(
    AtomicExportFailureStage Stage,
    string? Message = null);

public delegate ValueTask<TArtifact> AtomicExportRenderPort<TArtifact>(
    Stream output,
    CancellationToken cancellationToken);

public delegate ValueTask<TArtifact> AtomicExportPathRenderPort<TArtifact>(
    string outputPath,
    CancellationToken cancellationToken);

/// <summary>
/// Owns the product-neutral file lifecycle for exports rendered to a stream or native output
/// path. Format planning, destination pickers, localized presentation, and product rendering
/// remain with the caller.
/// </summary>
public sealed class AtomicExportExecutor
{
    private readonly Func<string, TemporaryFileLease> _createTemporaryFile;
    private readonly Func<string, TemporaryFileLease> _createTemporaryPathFile;
    private readonly Action<string, string> _replaceDestination;

    public AtomicExportExecutor()
        : this(
            AtomicFileWriter.CreateTempLease,
            CreatePathRenderTemporaryFile,
            AtomicFileWriter.ReplaceTarget)
    {
    }

    internal AtomicExportExecutor(
        Func<string, TemporaryFileLease> createTemporaryFile,
        Action<string, string> replaceDestination)
        : this(createTemporaryFile, CreatePathRenderTemporaryFile, replaceDestination)
    {
    }

    internal AtomicExportExecutor(
        Func<string, TemporaryFileLease> createTemporaryFile,
        Func<string, TemporaryFileLease> createTemporaryPathFile,
        Action<string, string> replaceDestination)
    {
        _createTemporaryFile = createTemporaryFile ??
            throw new ArgumentNullException(nameof(createTemporaryFile));
        _createTemporaryPathFile = createTemporaryPathFile ??
            throw new ArgumentNullException(nameof(createTemporaryPathFile));
        _replaceDestination = replaceDestination ??
            throw new ArgumentNullException(nameof(replaceDestination));
    }

    public async Task<OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>>
        ExecuteAsync<TArtifact>(
            string? destinationPath,
            AtomicExportRenderPort<TArtifact> renderAsync,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderAsync);

        return await ExecuteCoreAsync(
            destinationPath,
            _createTemporaryFile,
            async (temporaryFile, setStage, token) =>
            {
                await using var output = temporaryFile.OpenWrite(useAsync: true);
                setStage(AtomicExportFailureStage.Rendering);
                var artifact = await renderAsync(output, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                setStage(AtomicExportFailureStage.Flushing);
                await output.FlushAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (output is FileStream fileStream)
                    fileStream.Flush(flushToDisk: true);
                token.ThrowIfCancellationRequested();
                return artifact;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a renderer that requires a file-system path, such as a native media encoder.
    /// The renderer receives an owned sibling temporary path with the destination extension;
    /// the destination is replaced only after rendering and durable flushing complete.
    /// </summary>
    public async Task<OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>>
        ExecutePathAsync<TArtifact>(
            string? destinationPath,
            AtomicExportPathRenderPort<TArtifact> renderAsync,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderAsync);

        return await ExecuteCoreAsync(
            destinationPath,
            _createTemporaryPathFile,
            async (temporaryFile, setStage, token) =>
            {
                setStage(AtomicExportFailureStage.Rendering);
                var artifact = await renderAsync(temporaryFile.Path, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                setStage(AtomicExportFailureStage.Flushing);
                await using var output = new FileStream(
                    temporaryFile.Path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous);
                await output.FlushAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                output.Flush(flushToDisk: true);
                token.ThrowIfCancellationRequested();
                return artifact;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>>
        ExecuteCoreAsync<TArtifact>(
            string? destinationPath,
            Func<string, TemporaryFileLease> createTemporaryFile,
            Func<
                TemporaryFileLease,
                Action<AtomicExportFailureStage>,
                CancellationToken,
                ValueTask<TArtifact>> renderAsync,
            CancellationToken cancellationToken)
    {
        string? fullDestinationPath = null;
        TemporaryFileLease? temporaryFile = null;
        var failureStage = AtomicExportFailureStage.DestinationValidation;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var validationIssue = ValidateDestination(destinationPath, out fullDestinationPath);
            if (validationIssue is { } issue)
            {
                return OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>
                    .ValidationFailure(issue, path: fullDestinationPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            failureStage = AtomicExportFailureStage.TemporaryFileCreation;
            temporaryFile = createTemporaryFile(fullDestinationPath!) ??
                throw new InvalidOperationException("The temporary file factory returned null.");

            cancellationToken.ThrowIfCancellationRequested();
            var artifact = await renderAsync(
                temporaryFile,
                stage => failureStage = stage,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            failureStage = AtomicExportFailureStage.ReplacingDestination;
            _replaceDestination(temporaryFile.Path, fullDestinationPath!);
            temporaryFile.Commit();

            return OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>
                .Completed(artifact, fullDestinationPath);
        }
        catch (OperationCanceledException ex)
        {
            return OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>
                .Cancel(path: fullDestinationPath, exception: ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return OperationOutcome<TArtifact, AtomicExportValidationIssue, AtomicExportFailure>
                .Failure(
                    new AtomicExportFailure(failureStage, ex.Message),
                    ex,
                    path: fullDestinationPath);
        }
        finally
        {
            temporaryFile?.Release();
        }
    }

    private static TemporaryFileLease CreatePathRenderTemporaryFile(string destinationPath)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var extension = Path.GetExtension(fullDestinationPath);
        if (string.IsNullOrEmpty(extension))
            extension = ".tmp";

        return TemporaryFileLease.CreateForExternalWriter(
            $".{Path.GetFileNameWithoutExtension(fullDestinationPath)}.",
            extension,
            Path.GetDirectoryName(fullDestinationPath));
    }

    private static AtomicExportValidationIssue? ValidateDestination(
        string? destinationPath,
        out string? fullDestinationPath)
    {
        fullDestinationPath = null;
        if (string.IsNullOrWhiteSpace(destinationPath))
            return AtomicExportValidationIssue.DestinationPathMissing;

        try
        {
            fullDestinationPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return AtomicExportValidationIssue.DestinationPathInvalid;
        }

        if (string.IsNullOrEmpty(Path.GetFileName(fullDestinationPath)))
            return AtomicExportValidationIssue.DestinationPathInvalid;

        if (Directory.Exists(fullDestinationPath))
            return AtomicExportValidationIssue.DestinationIsDirectory;

        for (var parent = Path.GetDirectoryName(fullDestinationPath);
             !string.IsNullOrEmpty(parent);
             parent = Path.GetDirectoryName(parent))
        {
            if (File.Exists(parent))
                return AtomicExportValidationIssue.DestinationParentIsFile;
        }

        return null;
    }
}
