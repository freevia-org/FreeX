namespace FreeX.App.Presentation.Accessibility;

public readonly record struct CellAnnouncementMetadata(
    bool HasComment = false,
    string? CommentTitle = null,
    bool IsFormula = false,
    bool IsMerged = false,
    bool HasDataValidation = false,
    bool HasHyperlink = false,
    bool IsLocked = false);

public static class CellAnnouncementPlanner
{
    public static string BuildName(
        string address,
        string? value,
        CellAnnouncementMetadata metadata)
    {
        var name = string.IsNullOrWhiteSpace(value) ? address : $"{address}: {value}";

        List<string>? cues = null;
        void AddCue(string cue) => (cues ??= []).Add(cue);

        if (metadata.HasComment && !string.IsNullOrEmpty(metadata.CommentTitle))
            AddCue($"has {metadata.CommentTitle.ToLowerInvariant()}");
        if (metadata.IsFormula)
            AddCue("is a formula");
        if (metadata.IsMerged)
            AddCue("is merged");
        if (metadata.HasDataValidation)
            AddCue("has data validation");
        if (metadata.HasHyperlink)
            AddCue("has a hyperlink");
        if (metadata.IsLocked)
            AddCue("is locked");

        return cues is null ? name : $"{name}, {string.Join(", ", cues)}";
    }
}
