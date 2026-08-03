using FreeW.Core.Model;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

/// <summary>Builds the shared document statistics rows consumed by both FreeW shell renderers.</summary>
public static class BackstageInfoStatisticsPlanner
{
    public static IReadOnlyList<BackstageFieldRow> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var stats = WordCount.Of(document);
        return
        [
            new("Words", stats.Words.ToString()),
            new("Characters", stats.CharactersWithSpaces.ToString()),
            new("Paragraphs", stats.Paragraphs.ToString()),
        ];
    }
}
