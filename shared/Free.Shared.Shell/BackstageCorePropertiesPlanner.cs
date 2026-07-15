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

    public static IReadOnlyList<BackstageFieldRow> Build(BackstageCoreProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return [
            new(TitleLabel, ValueOrDash(properties.Title)),
            new(AuthorLabel, ValueOrDash(properties.Author)),
            new(SubjectLabel, ValueOrDash(properties.Subject)),
            new(KeywordsLabel, ValueOrDash(properties.Keywords)),
        ];
    }

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
