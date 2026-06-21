namespace Free.Shared.Shell.Wpf;

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
            new(TitleLabel, BackstageVisualKit.Or(properties.Title)),
            new(AuthorLabel, BackstageVisualKit.Or(properties.Author)),
            new(SubjectLabel, BackstageVisualKit.Or(properties.Subject)),
            new(KeywordsLabel, BackstageVisualKit.Or(properties.Keywords)),
        ];
    }
}
