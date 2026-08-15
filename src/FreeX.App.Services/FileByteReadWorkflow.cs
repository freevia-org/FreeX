namespace FreeX.App.Services;

public enum FileByteReadOutcome
{
    Succeeded,
    Empty,
    Canceled,
    Failed
}

public sealed record FileByteReadResult(
    FileByteReadOutcome Outcome,
    byte[] Bytes,
    Exception? Exception = null)
{
    public bool IsReadable => Outcome is FileByteReadOutcome.Succeeded or FileByteReadOutcome.Empty;

    public string FailureMessage => Exception?.Message ?? Outcome switch
    {
        FileByteReadOutcome.Empty => "The selected file is empty.",
        FileByteReadOutcome.Canceled => "The file read was canceled.",
        _ => "The selected file could not be read."
    };
}

/// <summary>
/// Owns renderer-neutral file and picker-stream reads while leaving empty-file and error presentation
/// policy to the calling workarea.
/// </summary>
public static class FileByteReadWorkflow
{
    public static Task<FileByteReadResult> ReadLocalPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return ReadStreamAsync(
            () => Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)),
            cancellationToken);
    }

    public static async Task<FileByteReadResult> ReadStreamAsync(
        Func<Task<Stream>> openReadAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openReadAsync);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = await openReadAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            var bytes = memory.ToArray();
            return new FileByteReadResult(
                bytes.Length == 0 ? FileByteReadOutcome.Empty : FileByteReadOutcome.Succeeded,
                bytes);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new FileByteReadResult(FileByteReadOutcome.Canceled, [], ex);
        }
        catch (Exception ex)
        {
            return new FileByteReadResult(FileByteReadOutcome.Failed, [], ex);
        }
    }
}
