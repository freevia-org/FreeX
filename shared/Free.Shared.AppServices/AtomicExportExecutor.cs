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

/// <summary>
/// Owns the product-neutral file lifecycle for exports rendered to a stream. Format planning,
/// destination pickers, localized presentation, and product rendering remain with the caller.
/// </summary>
public sealed class AtomicExportExecutor
{
    private readonly Func<string, TemporaryFileLease> _createTemporaryFile;
    private readonly Action<string, string> _replaceDestination;

    public AtomicExportExecutor()
        : this(AtomicFileWriter.CreateTempLease, AtomicFileWriter.ReplaceTarget)
    {
    }

    internal AtomicExportExecutor(
        Func<string, TemporaryFileLease> createTemporaryFile,
        Action<string, string> replaceDestination)
    {
        _createTemporaryFile = createTemporaryFile ??
            throw new ArgumentNullException(nameof(createTemporaryFile));
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
            temporaryFile = _createTemporaryFile(fullDestinationPath!) ??
                throw new InvalidOperationException("The temporary file factory returned null.");

            cancellationToken.ThrowIfCancellationRequested();
            TArtifact artifact;
            await using (var output = temporaryFile.OpenWrite(useAsync: true))
            {
                failureStage = AtomicExportFailureStage.Rendering;
                artifact = await renderAsync(output, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                failureStage = AtomicExportFailureStage.Flushing;
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (output is FileStream fileStream)
                    fileStream.Flush(flushToDisk: true);
                cancellationToken.ThrowIfCancellationRequested();
            }

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
