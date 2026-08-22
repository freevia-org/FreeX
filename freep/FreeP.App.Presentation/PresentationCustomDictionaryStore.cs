namespace FreeP.App.Compositor;

/// <summary>
/// Neutral FreeP persistence for the reviewer's custom spelling dictionary. Mirrors FreeW's
/// <c>CustomDictionaryStore</c>: a UTF-8, word-per-line <c>.lex</c> file under FreeP's own app-data
/// folder, so a word added via "Add to Dictionary" survives closing the presentation and restarting
/// the app instead of living only in the in-memory <see cref="PresentationProofingDictionaryState"/>
/// for the lifetime of the <c>PresentationReviewWorkflowSession</c>.
///
/// <para>
/// A null <see cref="DictionaryPath"/> (the default used by tests) makes every operation an in-memory
/// no-op, so unit tests never touch the real user data folder unless they opt in with
/// <see cref="Load"/>.
/// </para>
/// </summary>
public sealed class PresentationCustomDictionaryStore
{
    public const string FileName = "customdictionary.lex";

    private readonly List<string> _words = [];
    private readonly AtomicLineSetStore _store;

    public PresentationCustomDictionaryStore(
        string? storePath,
        IAtomicLineSetFileSystem? fileSystem = null)
    {
        _store = new AtomicLineSetStore(storePath, fileSystem);
        foreach (var line in _store.Load())
        {
            if (!string.IsNullOrEmpty(line) && !_words.Contains(line, StringComparer.Ordinal))
                _words.Add(line);
        }
    }

    /// <summary>The persisted words, already normalized (upper-invariant) by the caller.</summary>
    public IReadOnlyList<string> Words => _words;

    public string? DictionaryPath => _store.StorePath;

    /// <summary>The default disk-backed store, rooted at the ambient product's app-data folder.</summary>
    public static PresentationCustomDictionaryStore Load()
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

        return new PresentationCustomDictionaryStore(path);
    }

    /// <summary>
    /// Records a word as already added to <see cref="PresentationProofingDictionaryState"/> and
    /// persists it. Callers pass the normalized word; a duplicate (ordinal) add is a no-op.
    /// </summary>
    public bool Add(string normalizedWord)
    {
        if (string.IsNullOrEmpty(normalizedWord) || _words.Contains(normalizedWord, StringComparer.Ordinal))
            return false;

        _words.Add(normalizedWord);
        _store.TrySave(_words);
        return true;
    }
}
