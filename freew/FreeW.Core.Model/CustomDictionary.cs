namespace FreeW.Core.Model;

/// <summary>
/// A pure, in-memory store of custom (user-added) spelling words. This is the testable core; the
/// WPF/IO layer (see FreeW.App.Host's <c>CustomDictionaryStore</c>) wraps it to persist the words as a
/// UTF-8 word-per-line <c>.lex</c> file under FreeW's data folder and feed that file to WPF's spell
/// checker via <c>SpellCheck.CustomDictionaries</c>. Words are matched and de-duplicated
/// case-insensitively (so adding "Foo" then "foo" keeps a single entry), trimmed on add, and surrounding
/// whitespace/blank entries are ignored. <see cref="Words"/> returns the entries in case-insensitive
/// alphabetical order so the persisted file and any UI list are stable.
/// </summary>
public sealed class CustomDictionary
{
    // Maps the normalised (trimmed) word — keyed case-insensitively — to the exact casing first added,
    // so the persisted dictionary keeps the user's preferred spelling while lookups ignore case.
    private readonly Dictionary<string, string> _words = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Create an empty dictionary.</summary>
    public CustomDictionary()
    {
    }

    /// <summary>Create a dictionary seeded from an existing set of words (e.g. a loaded file).</summary>
    public CustomDictionary(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        foreach (var word in words)
            Add(word);
    }

    /// <summary>The number of stored words.</summary>
    public int Count => _words.Count;

    /// <summary>The stored words, in case-insensitive alphabetical order.</summary>
    public IReadOnlyList<string> Words =>
        _words.Values
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Add a word (trimmed). A null/blank word is ignored. Returns true when a new word was stored,
    /// false when it was blank or already present (case-insensitively) — so a duplicate add is a no-op
    /// rather than an error. The first casing added wins for the persisted/displayed form.
    /// </summary>
    public bool Add(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        var trimmed = word.Trim();
        if (_words.ContainsKey(trimmed))
            return false;
        _words[trimmed] = trimmed;
        return true;
    }

    /// <summary>True when the word (trimmed) is stored, compared case-insensitively.</summary>
    public bool Contains(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return _words.ContainsKey(word.Trim());
    }

    /// <summary>
    /// Remove the word (trimmed, case-insensitive). Returns true when one was removed, false when no
    /// word matched (a missing/blank word is not an error).
    /// </summary>
    public bool Remove(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return _words.Remove(word.Trim());
    }

    /// <summary>Remove every stored word.</summary>
    public void Clear() => _words.Clear();
}
