namespace Free.Shared.AppServices;

/// <summary>
/// File-system operations used by temporary resource leases. Tests can inject a deterministic
/// implementation without changing production ownership behavior.
/// </summary>
public interface ITemporaryResourceFileSystem
{
    string GetTemporaryDirectoryPath();

    bool FileExists(string path);

    bool DirectoryExists(string path);

    Stream CreateNewFile(string path);

    Stream OpenFileForWrite(string path, bool useAsync, int bufferSize);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);
}

/// <summary>
/// Owns a temporary resource until it is explicitly kept/committed or released.
/// </summary>
public abstract class TemporaryResourceLease : IDisposable
{
    private const int OwnedState = 0;
    private const int KeptState = 1;
    private const int ReleasedState = 2;
    private int _state = OwnedState;

    protected TemporaryResourceLease(string path) => Path = path;

    public string Path { get; }

    public bool OwnsResource => Volatile.Read(ref _state) == OwnedState;

    public bool IsKept => Volatile.Read(ref _state) == KeptState;

    /// <summary>
    /// Relinquishes cleanup ownership and leaves the resource at its current path.
    /// </summary>
    public void Keep() => Interlocked.CompareExchange(ref _state, KeptState, OwnedState);

    /// <summary>
    /// Marks a successfully handed-off or moved resource as committed.
    /// </summary>
    public void Commit() => Keep();

    /// <summary>
    /// Performs one best-effort cleanup attempt. Repeated release/dispose calls are no-ops.
    /// </summary>
    public void Release()
    {
        if (Interlocked.CompareExchange(ref _state, ReleasedState, OwnedState) != OwnedState)
            return;

        TryDeleteResource();
    }

    public void Dispose() => Release();

    protected void ThrowIfOwnershipReleased()
    {
        if (!OwnsResource)
            throw new ObjectDisposedException(GetType().Name);
    }

    protected abstract void TryDeleteResource();
}

/// <summary>
/// Owns an immediately reserved temporary file.
/// </summary>
public sealed class TemporaryFileLease : TemporaryResourceLease
{
    private const int MaxCreateAttempts = 16;
    private const int DefaultBufferSize = 81920;
    private readonly ITemporaryResourceFileSystem _fileSystem;

    private TemporaryFileLease(string path, ITemporaryResourceFileSystem fileSystem)
        : base(path) => _fileSystem = fileSystem;

    public static TemporaryFileLease Create(
        string prefix,
        string extension,
        string? directoryPath = null,
        ITemporaryResourceFileSystem? fileSystem = null,
        Func<string>? uniqueTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(extension);
        ValidateFileNameComponent(prefix, nameof(prefix), allowEmpty: true);
        ValidateFileNameComponent(extension, nameof(extension), allowEmpty: true);

        fileSystem ??= PhysicalTemporaryResourceFileSystem.Instance;
        uniqueTokenFactory ??= CreateUniqueToken;
        var directory = System.IO.Path.GetFullPath(
            directoryPath ?? fileSystem.GetTemporaryDirectoryPath());
        fileSystem.CreateDirectory(directory);

        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var token = uniqueTokenFactory();
            ValidateFileNameComponent(token, nameof(uniqueTokenFactory), allowEmpty: false);
            var candidate = System.IO.Path.Combine(directory, prefix + token + extension);
            if (fileSystem.FileExists(candidate) || fileSystem.DirectoryExists(candidate))
                continue;

            try
            {
                using (fileSystem.CreateNewFile(candidate))
                {
                }

                return new TemporaryFileLease(candidate, fileSystem);
            }
            catch (IOException) when (
                fileSystem.FileExists(candidate) || fileSystem.DirectoryExists(candidate))
            {
                // Another creator reserved this name after the existence check.
            }
        }

        throw new IOException($"Could not reserve a unique temporary file in '{directory}'.");
    }

    /// <summary>
    /// Reserves a unique name, removes the reservation, and retains cleanup ownership for a
    /// native or external writer that requires the destination not to exist before launch.
    /// </summary>
    public static TemporaryFileLease CreateForExternalWriter(
        string prefix,
        string extension,
        string? directoryPath = null,
        ITemporaryResourceFileSystem? fileSystem = null,
        Func<string>? uniqueTokenFactory = null)
    {
        var lease = Create(prefix, extension, directoryPath, fileSystem, uniqueTokenFactory);
        try
        {
            lease._fileSystem.DeleteFile(lease.Path);
            return lease;
        }
        catch
        {
            lease.Release();
            throw;
        }
    }

    /// <summary>
    /// Atomically reserves an exact caller-supplied path and owns it until released.
    /// </summary>
    public static TemporaryFileLease Reserve(
        string path,
        ITemporaryResourceFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        fileSystem ??= PhysicalTemporaryResourceFileSystem.Instance;
        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            fileSystem.CreateDirectory(directory);

        using (fileSystem.CreateNewFile(fullPath))
        {
        }

        return new TemporaryFileLease(fullPath, fileSystem);
    }

    /// <summary>
    /// Takes cleanup ownership of a caller-supplied path without creating or modifying it.
    /// </summary>
    public static TemporaryFileLease Own(
        string path,
        ITemporaryResourceFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TemporaryFileLease(
            System.IO.Path.GetFullPath(path),
            fileSystem ?? PhysicalTemporaryResourceFileSystem.Instance);
    }

    public Stream OpenWrite(bool useAsync = false, int bufferSize = DefaultBufferSize)
    {
        ThrowIfOwnershipReleased();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        return _fileSystem.OpenFileForWrite(Path, useAsync, bufferSize);
    }

    public void WriteAllBytes(ReadOnlySpan<byte> bytes)
    {
        using var stream = OpenWrite();
        stream.Write(bytes);
    }

    public async ValueTask WriteAllBytesAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        using var stream = OpenWrite(useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    protected override void TryDeleteResource()
    {
        try
        {
            if (_fileSystem.FileExists(Path))
                _fileSystem.DeleteFile(Path);
        }
        catch
        {
            // Cleanup must not replace the operation outcome that caused disposal.
        }
    }

    private static string CreateUniqueToken() => Guid.NewGuid().ToString("N");

    private static void ValidateFileNameComponent(
        string value,
        string parameterName,
        bool allowEmpty)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(System.IO.Path.DirectorySeparatorChar) ||
            value.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Value must be a valid single file-name component.", parameterName);
        }
    }
}

/// <summary>
/// Owns an immediately created temporary directory.
/// </summary>
public sealed class TemporaryDirectoryLease : TemporaryResourceLease
{
    private const int MaxCreateAttempts = 16;
    private readonly ITemporaryResourceFileSystem _fileSystem;
    private readonly bool _recursiveCleanup;

    private TemporaryDirectoryLease(
        string path,
        ITemporaryResourceFileSystem fileSystem,
        bool recursiveCleanup)
        : base(path)
    {
        _fileSystem = fileSystem;
        _recursiveCleanup = recursiveCleanup;
    }

    public static TemporaryDirectoryLease Create(
        string prefix,
        string? parentDirectoryPath = null,
        bool recursiveCleanup = true,
        ITemporaryResourceFileSystem? fileSystem = null,
        Func<string>? uniqueTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            prefix.Contains(System.IO.Path.DirectorySeparatorChar) ||
            prefix.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Value must be a valid single directory-name component.", nameof(prefix));
        }

        fileSystem ??= PhysicalTemporaryResourceFileSystem.Instance;
        uniqueTokenFactory ??= () => Guid.NewGuid().ToString("N");
        var parentDirectory = System.IO.Path.GetFullPath(
            parentDirectoryPath ?? fileSystem.GetTemporaryDirectoryPath());
        fileSystem.CreateDirectory(parentDirectory);

        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var token = uniqueTokenFactory();
            if (string.IsNullOrWhiteSpace(token) ||
                token.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
                token.Contains(System.IO.Path.DirectorySeparatorChar) ||
                token.Contains(System.IO.Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException(
                    "The unique token factory must return a valid single directory-name component.",
                    nameof(uniqueTokenFactory));
            }

            var candidate = System.IO.Path.Combine(parentDirectory, prefix + token);
            var reservationPath = candidate + ".lease";
            if (fileSystem.FileExists(candidate) ||
                fileSystem.DirectoryExists(candidate) ||
                fileSystem.FileExists(reservationPath) ||
                fileSystem.DirectoryExists(reservationPath))
            {
                continue;
            }

            try
            {
                using (fileSystem.CreateNewFile(reservationPath))
                {
                }
            }
            catch (IOException) when (
                fileSystem.FileExists(candidate) ||
                fileSystem.DirectoryExists(candidate) ||
                fileSystem.FileExists(reservationPath) ||
                fileSystem.DirectoryExists(reservationPath))
            {
                // Another creator reserved this name after the existence check.
                continue;
            }

            try
            {
                if (fileSystem.FileExists(candidate) || fileSystem.DirectoryExists(candidate))
                    continue;

                fileSystem.CreateDirectory(candidate);
                if (!fileSystem.DirectoryExists(candidate))
                    throw new IOException($"Temporary directory '{candidate}' was not created.");
                return new TemporaryDirectoryLease(candidate, fileSystem, recursiveCleanup);
            }
            finally
            {
                TryDeleteReservation(fileSystem, reservationPath);
            }
        }

        throw new IOException($"Could not reserve a unique temporary directory in '{parentDirectory}'.");
    }

    public static TemporaryDirectoryLease Reserve(
        string path,
        bool recursiveCleanup = true,
        ITemporaryResourceFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        fileSystem ??= PhysicalTemporaryResourceFileSystem.Instance;
        var fullPath = System.IO.Path.GetFullPath(path);
        if (fileSystem.FileExists(fullPath) || fileSystem.DirectoryExists(fullPath))
            throw new IOException($"Temporary resource path already exists: '{fullPath}'.");

        var parentDirectory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDirectory))
            fileSystem.CreateDirectory(parentDirectory);

        var reservationPath = fullPath + ".lease";
        using (fileSystem.CreateNewFile(reservationPath))
        {
        }

        try
        {
            if (fileSystem.FileExists(fullPath) || fileSystem.DirectoryExists(fullPath))
                throw new IOException($"Temporary resource path already exists: '{fullPath}'.");
            fileSystem.CreateDirectory(fullPath);
            return new TemporaryDirectoryLease(fullPath, fileSystem, recursiveCleanup);
        }
        finally
        {
            TryDeleteReservation(fileSystem, reservationPath);
        }
    }

    public static TemporaryDirectoryLease Own(
        string path,
        bool recursiveCleanup = true,
        ITemporaryResourceFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TemporaryDirectoryLease(
            System.IO.Path.GetFullPath(path),
            fileSystem ?? PhysicalTemporaryResourceFileSystem.Instance,
            recursiveCleanup);
    }

    protected override void TryDeleteResource()
    {
        try
        {
            if (_fileSystem.DirectoryExists(Path))
                _fileSystem.DeleteDirectory(Path, _recursiveCleanup);
        }
        catch
        {
            // Cleanup must not replace the operation outcome that caused disposal.
        }
    }

    private static void TryDeleteReservation(
        ITemporaryResourceFileSystem fileSystem,
        string reservationPath)
    {
        try
        {
            if (fileSystem.FileExists(reservationPath))
                fileSystem.DeleteFile(reservationPath);
        }
        catch
        {
            // The directory lease remains valid; a stale reservation is safe to ignore.
        }
    }
}

internal sealed class PhysicalTemporaryResourceFileSystem : ITemporaryResourceFileSystem
{
    public static PhysicalTemporaryResourceFileSystem Instance { get; } = new();

    private PhysicalTemporaryResourceFileSystem()
    {
    }

    public string GetTemporaryDirectoryPath() => System.IO.Path.GetTempPath();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Stream CreateNewFile(string path) => new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None);

    public Stream OpenFileForWrite(string path, bool useAsync, int bufferSize) => new FileStream(
        path,
        FileMode.Truncate,
        FileAccess.Write,
        FileShare.None,
        bufferSize,
        useAsync);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
}
