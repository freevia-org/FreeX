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
    private readonly string? _storePath;
    private readonly IPresentationCustomDictionaryFileSystem _fileSystem;

    public PresentationCustomDictionaryStore(
        string? storePath,
        IPresentationCustomDictionaryFileSystem? fileSystem = null)
    {
        _storePath = storePath;
        _fileSystem = fileSystem ?? RealPresentationCustomDictionaryFileSystem.Instance;
        TryLoad();
    }

    /// <summary>The persisted words, already normalized (upper-invariant) by the caller.</summary>
    public IReadOnlyList<string> Words => _words;

    public string? DictionaryPath => _storePath;

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
        TrySave();
        return true;
    }

    private void TryLoad()
    {
        if (string.IsNullOrEmpty(_storePath) || !_fileSystem.Exists(_storePath))
            return;

        try
        {
            foreach (var line in _fileSystem.ReadAllLines(_storePath))
            {
                if (!string.IsNullOrEmpty(line) && !_words.Contains(line, StringComparer.Ordinal))
                    _words.Add(line);
            }
        }
        catch
        {
            // Corrupt or unreadable state starts a fresh session.
        }
    }

    private void TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                _fileSystem.CreateDirectory(directory);

            _fileSystem.WriteAllLinesAtomically(_storePath, _words);
        }
        catch
        {
            // Best-effort: a failed save never blocks proofing.
        }
    }
}

public interface IPresentationCustomDictionaryFileSystem
{
    bool Exists(string path);
    string[] ReadAllLines(string path);
    void WriteAllLinesAtomically(string path, IEnumerable<string> lines);
    void CreateDirectory(string path);
}

public sealed class RealPresentationCustomDictionaryFileSystem : IPresentationCustomDictionaryFileSystem
{
    public static readonly RealPresentationCustomDictionaryFileSystem Instance = new();

    private RealPresentationCustomDictionaryFileSystem()
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
