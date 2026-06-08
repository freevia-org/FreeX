namespace FreeX.App.Services;

public sealed record WorkbookFindAllResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<WorkbookFindAllMatch> Matches)
{
    public int MatchCount => Matches.Count;

    public static WorkbookFindAllResult Failed(string errorMessage) =>
        new(false, errorMessage, []);

    public static WorkbookFindAllResult Found(IReadOnlyList<WorkbookFindAllMatch> matches) =>
        new(true, null, matches);
}
