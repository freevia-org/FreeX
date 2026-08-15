namespace Free.Shared.Shell;

public sealed record BackstageCoreProperties(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords);

public static class BackstageCorePropertiesPlanner
{
    public const string TitleLabel = "Title";
    public const string AuthorLabel = "Author";
    public const string SubjectLabel = "Subject";
    public const string KeywordsLabel = "Keywords";

    public static IReadOnlyList<BackstageFieldRow> Build(
        BackstageCoreProperties properties,
        BackstageCorePropertiesTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        text ??= BackstageCorePropertiesTextSpec.NeutralEnglish;

        return [
            new(text.TitleLabel, ValueOrEmpty(properties.Title, text.EmptyValue)),
            new(text.AuthorLabel, ValueOrEmpty(properties.Author, text.EmptyValue)),
            new(text.SubjectLabel, ValueOrEmpty(properties.Subject, text.EmptyValue)),
            new(text.KeywordsLabel, ValueOrEmpty(properties.Keywords, text.EmptyValue)),
        ];
    }

    private static string ValueOrEmpty(string? value, string emptyValue) =>
        string.IsNullOrWhiteSpace(value) ? emptyValue : value;
}
