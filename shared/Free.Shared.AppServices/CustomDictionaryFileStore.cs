using Free.Shared.TextSearch;

namespace Free.Shared.AppServices;

/// <summary>
/// Shared persistence for a sister app's user spelling dictionary: a UTF-8, word-per-line
/// <c>.lex</c> file under the product's own data folder, loaded once at construction and rewritten
/// atomically on every successful change. Matching follows Microsoft Office's custom dictionaries
/// (CUSTOM.DIC): words are trimmed and compared case-insensitively, so adding "Foo" and then "foo"
/// keeps a single entry. A null store path (the default used by tests) makes every operation an
/// in-memory no-op so unit tests never touch the real user data folder unless they opt in.
/// </summary>
public class CustomDictionaryFileStore
{
    public const string DefaultFileName = "customdictionary.lex";

    private readonly CustomDictionary _dictionary = new();
    private readonly AtomicLineSetStore _store;

    public CustomDictionaryFileStore(string? storePath, IAtomicLineSetFileSystem? fileSystem = null)
    {
        _store = new AtomicLineSetStore(storePath, fileSystem);
        foreach (var line in _store.Load())
            _dictionary.Add(line);
    }

    /// <summary>The stored words in case-insensitive alphabetical order.</summary>
    public IReadOnlyList<string> Words => _dictionary.Words;

    public string? DictionaryPath => _store.StorePath;

    /// <summary>
    /// The default store path rooted at the ambient product's local app-data folder, or null when that
    /// folder is unavailable (the store then behaves as a session-only dictionary).
    /// </summary>
    public static string? ResolveDefaultPath(string fileName = DefaultFileName)
    {
        try
        {
            return Path.Combine(
                PlatformApplicationDataPathProvider.LocalInstance.GetApplicationDataDirectory(),
                AppStoragePathPlanner.ProductDirectoryName,
                fileName);
        }
        catch
        {
            return null;
        }
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
        if (string.IsNullOrEmpty(_store.StorePath))
            return null;

        return TrySave() || _store.PersistedFileExists()
            ? _store.StorePath
            : null;
    }

    private bool TrySave() => _store.TrySave(_dictionary.Words);
}
