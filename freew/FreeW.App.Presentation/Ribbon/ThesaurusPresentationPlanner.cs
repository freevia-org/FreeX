namespace FreeW.App.Presentation.Ribbon;

public sealed record ThesaurusDisplayPlan(
    string SourceWord,
    string HeadingText,
    string StatusText,
    IReadOnlyList<ThesaurusSenseRow> Senses)
{
    public bool HasSourceWord => !string.IsNullOrWhiteSpace(SourceWord);
    public bool HasSynonyms => Senses.Count > 0;
}

public sealed record ThesaurusSenseRow(
    string RawLabel,
    string DisplayLabel,
    IReadOnlyList<ThesaurusActionRow> Actions);

public sealed record ThesaurusActionRow(
    string SourceWord,
    string RawSynonym,
    string DisplayText,
    string InsertToolTip,
    string CopyToolTip)
{
    public string ReplaceToolTip => InsertToolTip;
}

public static class ThesaurusActionRowExtensions
{
    public static bool CanInsert(this ThesaurusActionRow action) =>
        !string.IsNullOrWhiteSpace(action.SourceWord) &&
        !string.IsNullOrWhiteSpace(action.RawSynonym) &&
        !string.IsNullOrWhiteSpace(action.DisplayText);
}

public static class ThesaurusPresentationPlanner
{
    public const string EmptyWordStatus = "Position the cursor on a word and press Shift+F7.";
    public const string NoSynonymsStatus = "No synonyms found for this word.";

    public static ThesaurusDisplayPlan Lookup(string? word) =>
        Build(word, ThesaurusLookup.Instance.Lookup(word));

    public static ThesaurusDisplayPlan Build(string? word, ThesaurusEntry? entry)
    {
        var sourceWord = word?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceWord))
            return new ThesaurusDisplayPlan(string.Empty, string.Empty, EmptyWordStatus, []);

        if (entry is null)
            return new ThesaurusDisplayPlan(sourceWord, sourceWord, NoSynonymsStatus, []);

        var senses = entry.Senses
            .Select(sense => BuildSense(sourceWord, sense))
            .Where(sense => sense.Actions.Count > 0)
            .ToArray();

        return senses.Length == 0
            ? new ThesaurusDisplayPlan(sourceWord, sourceWord, NoSynonymsStatus, [])
            : new ThesaurusDisplayPlan(sourceWord, sourceWord, string.Empty, senses);
    }

    private static ThesaurusSenseRow BuildSense(string sourceWord, ThesaurusSense sense)
    {
        var actions = sense.Synonyms
            .Select(synonym => BuildAction(sourceWord, synonym))
            .Where(action => action.DisplayText.Length > 0)
            .ToArray();

        return new ThesaurusSenseRow(sense.Label, FormatSenseLabel(sense.Label), actions);
    }

    private static ThesaurusActionRow BuildAction(string sourceWord, string synonym)
    {
        var display = FormatSynonym(synonym);
        return new ThesaurusActionRow(
            sourceWord,
            synonym,
            display,
            $"Insert \"{display}\" in place of \"{sourceWord}\"",
            $"Copy \"{display}\" to clipboard");
    }

    public static string FormatSynonym(string synonym) =>
        synonym.Replace('_', ' ').Trim();

    public static string FormatSenseLabel(string label) =>
        label.Trim() switch
        {
            "adj" => "adjective",
            "adv" => "adverb",
            "noun" => "noun",
            "verb" => "verb",
            "prep" => "preposition",
            "pron" => "pronoun",
            var value => value.Replace('_', ' ')
        };
}
