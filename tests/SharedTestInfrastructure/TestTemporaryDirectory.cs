using System;
using System.IO;
using System.Threading;

internal sealed class TestTemporaryDirectory : IDisposable
{
    private const int MaximumAttempts = 60;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public TestTemporaryDirectory(string? prefix = null)
    {
        Path = Create(prefix);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception exception) when (IsRetryableFileSystemException(exception))
            {
                if (attempt < MaximumAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }
    }

    private static string Create(string? prefix)
    {
        if (prefix?.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The temporary-directory prefix must be a valid file-name prefix.", nameof(prefix));
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                string.Concat(prefix, System.IO.Path.GetRandomFileName()));

            try
            {
                if (Directory.Exists(path))
                {
                    continue;
                }

                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception exception) when (IsRetryableFileSystemException(exception))
            {
                lastException = exception;
                if (attempt < MaximumAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        throw new IOException("Failed to create a temporary test directory.", lastException);
    }

    private static bool IsRetryableFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;
}
