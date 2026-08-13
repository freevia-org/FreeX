namespace FreeX.App.Services;

public sealed record CustomDictionaryEditorModel(
    IReadOnlyList<string> Words,
    string? SelectedWord,
    string? PendingWord,
    bool CanAdd,
    bool CanRemove,
    bool CanClear);

public sealed class CustomDictionaryEditorSession
{
    private List<string> _words = [];
    private string? _selectedWord;
    private string? _pendingWord;

    public CustomDictionaryEditorSession(IEnumerable<string>? persistedWords)
    {
        Reset(persistedWords);
    }

    public CustomDictionaryEditorModel Model => CreateModel();

    public CustomDictionaryEditorModel Reset(IEnumerable<string>? persistedWords)
    {
        _words = AppOptions.NormalizeSpellCheckCustomDictionaryWords(persistedWords);
        _selectedWord = null;
        _pendingWord = null;
        return CreateModel();
    }

    public CustomDictionaryEditorModel SetPendingWord(string? word)
    {
        _pendingWord = word;
        return CreateModel();
    }

    public CustomDictionaryEditorModel SelectWord(string? word)
    {
        _selectedWord = FindWord(word);
        return CreateModel();
    }

    public CustomDictionaryEditorModel AddPendingWord()
    {
        var normalizedWord = AppOptions.NormalizeSpellCheckCustomDictionaryWord(_pendingWord);
        if (normalizedWord is not null)
        {
            var dictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(_words);
            SpellCheckWorkflowPlanner.AddCustomDictionaryWord(_words, dictionary, normalizedWord);
            _selectedWord = FindWord(normalizedWord);
        }

        _pendingWord = null;
        return CreateModel();
    }

    public CustomDictionaryEditorModel RemoveSelectedWord()
    {
        if (_selectedWord is not null)
        {
            _selectedWord = SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext(
                _words,
                _selectedWord);
        }

        return CreateModel();
    }

    public CustomDictionaryEditorModel Clear()
    {
        SpellCheckWorkflowPlanner.ClearCustomDictionaryWords(_words);
        _selectedWord = null;
        return CreateModel();
    }

    private CustomDictionaryEditorModel CreateModel()
    {
        _selectedWord = FindWord(_selectedWord);
        return new(
            _words.ToArray(),
            _selectedWord,
            _pendingWord,
            AppOptions.NormalizeSpellCheckCustomDictionaryWord(_pendingWord) is not null,
            _selectedWord is not null,
            _words.Count > 0);
    }

    private string? FindWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;

        foreach (var candidate in _words)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
