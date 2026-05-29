using System.IO;

namespace FreeX.App.Host;

public enum ShareWorkbookPlanKind
{
    ShareExistingFile,
    SaveAsBeforeShare
}

public sealed record ShareWorkbookPlan(ShareWorkbookPlanKind Kind, string? Path);

public static class ShareWorkbookPlanner
{
    public static ShareWorkbookPlan CreatePlan(string? currentFilePath)
    {
        return TryGetShareableWorkbookPath(currentFilePath, out var shareablePath)
            ? new ShareWorkbookPlan(ShareWorkbookPlanKind.ShareExistingFile, shareablePath)
            : new ShareWorkbookPlan(ShareWorkbookPlanKind.SaveAsBeforeShare, null);
    }

    private static bool TryGetShareableWorkbookPath(string? currentFilePath, out string shareablePath)
    {
        shareablePath = currentFilePath ?? "";
        return !string.IsNullOrWhiteSpace(shareablePath) && File.Exists(shareablePath);
    }
}
