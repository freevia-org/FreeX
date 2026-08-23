namespace Free.Shared.AppServices;

/// <summary>
/// File-system operations used by <see cref="AtomicLineSetStore"/>.
/// </summary>
public interface IAtomicLineSetFileSystem
{
    bool FileExists(string path);
    string[] ReadAllLines(string path);
    void CreateDirectory(string path);
    void WriteAllTextAtomically(string path, string content);
}

/// <summary>
/// Physical line-set storage backed by <see cref="AtomicFileWriter"/>.
/// </summary>
public sealed class PhysicalAtomicLineSetFileSystem : IAtomicLineSetFileSystem
{
    public static PhysicalAtomicLineSetFileSystem Instance { get; } = new();

    private PhysicalAtomicLineSetFileSystem()
    {
    }

    public bool FileExists(string path) => File.Exists(path);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllTextAtomically(string path, string content) =>
        AtomicFileWriter.WriteAllText(path, content);
}

/// <summary>
/// Best-effort persistence for ordered, word-per-line data. Product-specific normalization,
/// comparison, and ordering remain with the caller.
/// </summary>
public sealed class AtomicLineSetStore
{
    private readonly string? _storePath;
    private readonly IAtomicLineSetFileSystem _fileSystem;

    public AtomicLineSetStore(
        string? storePath,
        IAtomicLineSetFileSystem? fileSystem = null)
    {
        _storePath = storePath;
        _fileSystem = fileSystem ?? PhysicalAtomicLineSetFileSystem.Instance;
    }

    public string? StorePath => _storePath;

    /// <summary>
    /// Reads the persisted lines, or returns an empty collection when no path/file is available or
    /// any file-system operation fails.
    /// </summary>
    public IReadOnlyList<string> Load()
    {
        if (string.IsNullOrEmpty(_storePath))
            return [];

        try
        {
            return _fileSystem.FileExists(_storePath)
                ? _fileSystem.ReadAllLines(_storePath)
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Writes the supplied lines atomically, preserving their order and exact text. Returns false
    /// instead of throwing when persistence is unavailable.
    /// </summary>
    public bool TrySave(IEnumerable<string>? lines)
    {
        if (string.IsNullOrEmpty(_storePath) || lines is null)
            return false;

        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                _fileSystem.CreateDirectory(directory);

            _fileSystem.WriteAllTextAtomically(_storePath, Serialize(lines));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Checks for an existing persisted file without surfacing file-system failures.</summary>
    public bool PersistedFileExists()
    {
        if (string.IsNullOrEmpty(_storePath))
            return false;

        try
        {
            return _fileSystem.FileExists(_storePath);
        }
        catch
        {
            return false;
        }
    }

    internal static string Serialize(IEnumerable<string> lines)
    {
        var materialized = lines.ToArray();
        var content = string.Join(Environment.NewLine, materialized);
        return materialized.Length == 0
            ? content
            : content + Environment.NewLine;
    }
}
