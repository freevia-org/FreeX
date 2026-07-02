using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// The Avalonia-shell persistence wrapper around the pure <see cref="CustomDictionary"/>. It loads/saves
/// the user's added spelling words as a UTF-8 word-per-line <c>customdictionary.lex</c> file under
/// FreeW's own data folder — the SAME location the WPF host's <c>FreeW.App.Host.CustomDictionaryStore</c>
/// uses (both resolve through <see cref="PlatformApplicationDataPathProvider.LocalInstance"/> and
/// <see cref="AppStoragePathPlanner.ProductDirectoryName"/>, which is "FreeW" because both shells'
/// Program.Main set AppProduct = "FreeW"), so a word added in either shell is available in the other.
/// GB2: previously <see cref="Editing.DocumentView"/> held a plain in-memory <see cref="CustomDictionary"/>
/// that nothing loaded or saved, so added words vanished on restart; this store closes that gap.
/// All persistence is best-effort: a failed load yields an empty dictionary and a failed save is
/// swallowed, so adding a word never disrupts editing. If the data folder cannot be reached at all, the
/// store behaves as an in-memory (session-only) dictionary with no backing file.
/// </summary>
internal sealed class CustomDictionaryStore
{
    private const string FileName = "customdictionary.lex";

    private readonly CustomDictionary _dictionary;
    private readonly string? _storePath;
    private readonly IFileSystem _fileSystem;

    /// <summary>Construct directly over an explicit path (or null for in-memory only) and file system —
    /// used by <see cref="Load"/> for the real app and by tests to verify persistence without touching
    /// the real user data folder.</summary>
    internal CustomDictionaryStore(string? storePath, IFileSystem? fileSystem = null)
    {
        _storePath = storePath;
        _fileSystem = fileSystem ?? RealFileSystem.Instance;
        _dictionary = new CustomDictionary();
        TryLoad();
    }

    /// <summary>The added words (case-insensitive alphabetical order) currently in the dictionary.</summary>
    public IReadOnlyList<string> Words => _dictionary.Words;

    /// <summary>
    /// The on-disk path of the <c>.lex</c> file, or null when the data folder could not be resolved (an
    /// in-memory, session-only dictionary).
    /// </summary>
    public string? DictionaryPath => _storePath;

    /// <summary>
    /// Load the dictionary from FreeW's data folder (creating an empty one if the file is missing or
    /// unreadable). Never throws.
    /// </summary>
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
            // Could not resolve the data folder — fall back to an in-memory (session-only) dictionary.
        }

        return new CustomDictionaryStore(path);
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
            // Corrupt/unreadable store: start from empty rather than blocking the app.
        }
    }

    /// <summary>True when the word (trimmed) is already in the dictionary (case-insensitive).</summary>
    public bool Contains(string word) => _dictionary.Contains(word);

    /// <summary>
    /// Add a word to the custom dictionary and persist the <c>.lex</c> file. Returns true when the word
    /// was newly added (so the caller can refresh spell-checking); false when it was blank or already
    /// present. Persistence is best-effort.
    /// </summary>
    public bool Add(string word)
    {
        if (!_dictionary.Add(word))
            return false;
        TrySave();
        return true;
    }

    /// <summary>Remove a word (case-insensitive), then persist. Best-effort. Returns true when removed.</summary>
    public bool Remove(string word)
    {
        if (!_dictionary.Remove(word))
            return false;
        TrySave();
        return true;
    }

    private bool TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return false; // in-memory session-only fallback
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                _fileSystem.CreateDirectory(directory);

            // A word-per-line file, UTF-8 without a BOM (matches the WPF host's .lex format so the first
            // word is not corrupted by a leading marker and both shells can read each other's file).
            _fileSystem.WriteAllLines(_storePath, _dictionary.Words);
            return true;
        }
        catch
        {
            // Persistence is best-effort; never block editing on a failed dictionary write.
            return false;
        }
    }
}

/// <summary>
/// Thin seam over the handful of file-system calls <see cref="CustomDictionaryStore"/> needs, so tests
/// can verify load/save behaviour with an in-memory fake instead of touching real disk.
/// </summary>
internal interface IFileSystem
{
    bool Exists(string path);
    string[] ReadAllLines(string path);
    void WriteAllLines(string path, IEnumerable<string> lines);
    void CreateDirectory(string path);
}

/// <summary>Real-disk <see cref="IFileSystem"/> used by the shipping app.</summary>
internal sealed class RealFileSystem : IFileSystem
{
    public static readonly RealFileSystem Instance = new();

    private RealFileSystem()
    {
    }

    public bool Exists(string path) => File.Exists(path);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public void WriteAllLines(string path, IEnumerable<string> lines) =>
        File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
