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
    private readonly AtomicLineSetStore _store;

    public CustomDictionaryStore(string? storePath, IAtomicLineSetFileSystem? fileSystem = null)
    {
        _store = new AtomicLineSetStore(storePath, fileSystem);
        foreach (var line in _store.Load())
            _dictionary.Add(line);
    }

    public IReadOnlyList<string> Words => _dictionary.Words;
    public string? DictionaryPath => _store.StorePath;

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
        if (string.IsNullOrEmpty(_store.StorePath))
            return null;

        return TrySave() || _store.PersistedFileExists()
            ? _store.StorePath
            : null;
    }

    private bool TrySave()
    {
        return _store.TrySave(_dictionary.Words);
    }
}
