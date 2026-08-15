using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Proofing;

/// <summary>
/// Neutral FreeW persistence for the user spelling dictionary. The file is a UTF-8, word-per-line
/// <c>.lex</c> store; shell-specific spell-check registration remains in the WPF host.
/// </summary>
public sealed class CustomDictionaryStore
{
    public const string FileName = "customdictionary.lex";

    private readonly CustomDictionary _dictionary = new();
    private readonly string? _storePath;
    private readonly ICustomDictionaryFileSystem _fileSystem;

    public CustomDictionaryStore(string? storePath, ICustomDictionaryFileSystem? fileSystem = null)
    {
        _storePath = storePath;
        _fileSystem = fileSystem ?? RealCustomDictionaryFileSystem.Instance;
        TryLoad();
    }

    public IReadOnlyList<string> Words => _dictionary.Words;
    public string? DictionaryPath => _storePath;

    public static CustomDictionaryStore Load()
    {
        string? path = null;
        try
        {
            path = Path.Combine(
                PlatformApplicationDataPathProvider.LocalInstance.GetApplicationDataDirectory(),
                AppStoragePathPlanner.ProductDirectoryName,
                FileName);
        }
        catch
        {
            // An unavailable data folder falls back to a session-only dictionary.
        }

        return new CustomDictionaryStore(path);
    }

    public bool Contains(string word) => _dictionary.Contains(word);

    public bool Add(string word)
    {
        if (!_dictionary.Add(word))
            return false;

        TrySave();
        return true;
    }

    public bool Remove(string word)
    {
        if (!_dictionary.Remove(word))
            return false;

        TrySave();
        return true;
    }

    /// <summary>Writes the current dictionary and returns its path, or null when persistence is unavailable.</summary>
    public string? EnsurePersisted()
    {
        if (string.IsNullOrEmpty(_storePath))
            return null;

        return TrySave() || _fileSystem.Exists(_storePath) ? _storePath : null;
    }

    private void TryLoad()
    {
        if (string.IsNullOrEmpty(_storePath) || !_fileSystem.Exists(_storePath))
            return;

        try
        {
            foreach (var line in _fileSystem.ReadAllLines(_storePath))
                _dictionary.Add(line);
        }
        catch
        {
            // Corrupt or unreadable state starts a fresh session.
        }
    }

    private bool TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                _fileSystem.CreateDirectory(directory);

            _fileSystem.WriteAllLinesAtomically(_storePath, _dictionary.Words);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public interface ICustomDictionaryFileSystem
{
    bool Exists(string path);
    string[] ReadAllLines(string path);
    void WriteAllLinesAtomically(string path, IEnumerable<string> lines);
    void CreateDirectory(string path);
}

public sealed class RealCustomDictionaryFileSystem : ICustomDictionaryFileSystem
{
    public static readonly RealCustomDictionaryFileSystem Instance = new();

    private RealCustomDictionaryFileSystem()
    {
    }

    public bool Exists(string path) => File.Exists(path);
    public string[] ReadAllLines(string path) => File.ReadAllLines(path);
    public void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
    {
        var materialized = lines.ToArray();
        var content = string.Join(Environment.NewLine, materialized);
        if (materialized.Length > 0)
            content += Environment.NewLine;

        AtomicFileWriter.WriteAllText(path, content);
    }
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
