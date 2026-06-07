namespace FreeX.App.Services;

public sealed record WorkbookReplaceResult(
    bool Success,
    string? ErrorMessage,
    int ReplacedCount)
{
    public static WorkbookReplaceResult Failed(string errorMessage) =>
        new(false, errorMessage, 0);

    public static WorkbookReplaceResult Replaced(int replacedCount) =>
        new(true, null, replacedCount);
}
