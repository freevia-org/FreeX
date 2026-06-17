using System.IO;
using System.Text;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The WPF/IO persistence wrapper around the pure <see cref="CustomDictionary"/>. It loads/saves the
/// user's added spelling words as a UTF-8 word-per-line <c>customdictionary.lex</c> file under FreeW's
/// own data folder — the same location pattern Quick Parts, the recent-files store, and autosave use
/// (AppData/Local › FreeW, via <see cref="PlatformApplicationDataPathProvider.LocalInstance"/> and
/// <see cref="AppStoragePathPlanner.ProductDirectoryName"/>, which is "FreeW" because Program.Main set
/// AppProduct = "FreeW"). The <c>.lex</c> format is exactly what WPF's spell checker consumes through
/// <c>SpellCheck.CustomDictionaries</c>, so the same file both persists FreeW's word list and registers
/// as the editor's custom dictionary (see <see cref="DictionaryPath"/>). All persistence is best-effort:
/// a failed load yields an empty dictionary and a failed save is swallowed, so adding a word never
/// disrupts editing. If the data folder cannot be reached at all, the store behaves as an in-memory
/// (session-only) dictionary with no backing file.
/// </summary>
internal sealed class CustomDictionaryStore
{
    private const string FileName = "customdictionary.lex";

    private readonly CustomDictionary _dictionary = new();
    private readonly string? _storePath;

    private CustomDictionaryStore(string? storePath) => _storePath = storePath;

    /// <summary>The added words (case-insensitive alphabetical order) currently in the dictionary.</summary>
    public IReadOnlyList<string> Words => _dictionary.Words;

    /// <summary>
    /// The on-disk path of the <c>.lex</c> file feeding WPF's spell checker, or null when the data
    /// folder could not be resolved (an in-memory, session-only dictionary). The file is created the
    /// first time a word is added; callers that register it as a WPF custom dictionary should ensure it
    /// exists first (see <see cref="EnsureFileExists"/>).
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

        var store = new CustomDictionaryStore(path);
        store.TryLoad();
        return store;
    }

    private void TryLoad()
    {
        if (string.IsNullOrEmpty(_storePath) || !File.Exists(_storePath))
            return;
        try
        {
            foreach (var line in File.ReadAllLines(_storePath))
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

    /// <summary>
    /// Ensure the backing <c>.lex</c> file exists on disk (writing the current — possibly empty — word
    /// list), so its Uri can be registered with WPF's <c>SpellCheck.CustomDictionaries</c>, which
    /// requires the file to be present. Returns the file path on success, or null when there is no
    /// backing file (in-memory fallback) or the write failed.
    /// </summary>
    public string? EnsureFileExists()
    {
        if (string.IsNullOrEmpty(_storePath))
            return null;
        return TrySave() ? _storePath : (File.Exists(_storePath) ? _storePath : null);
    }

    private bool TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return false; // in-memory session-only fallback
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // A WPF custom dictionary (.lex) is a plain UTF-8 word-per-line file. Write without a BOM so
            // the first word is not corrupted by a leading marker.
            File.WriteAllLines(_storePath, _dictionary.Words, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch
        {
            // Persistence is best-effort; never block editing on a failed dictionary write.
            return false;
        }
    }
}
